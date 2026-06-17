using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using VPMonitor.Models;

namespace VPMonitor.Core;

public class ProcessManager : IDisposable
{
    private readonly Logger _logger;
    private Process? _targetProcess;
    private readonly Dictionary<int, Process> _childProcesses = new();
    private bool _disposed;

    public Process? TargetProcess => _targetProcess;
    public IReadOnlyDictionary<int, Process> ChildProcesses => _childProcesses;
    public bool IsProcessRunning => _targetProcess != null && !_targetProcess.HasExited;
    public bool IsSuspended { get; private set; }

    public event EventHandler? ProcessExited;
    public event EventHandler<int>? ChildProcessAdded;
    public event EventHandler<int>? ChildProcessRemoved;

    public ProcessManager(Logger logger)
    {
        _logger = logger;
    }

    public bool StartProcess(string exePath, string? arguments = null)
    {
        try
        {
            if (_targetProcess != null && !_targetProcess.HasExited)
            {
                StopProcess();
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _targetProcess = Process.Start(startInfo);
            if (_targetProcess == null)
            {
                _logger.LogError("Failed to start process", exePath);
                return false;
            }

            _targetProcess.EnableRaisingEvents = true;
            _targetProcess.Exited += TargetProcess_Exited;

            IsSuspended = false;
            _logger.LogInfo($"Process started", $"PID: {_targetProcess.Id}, Path: {exePath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error starting process", ex.Message);
            return false;
        }
    }

    public bool AttachToProcess(int processId)
    {
        try
        {
            if (_targetProcess != null && !_targetProcess.HasExited)
            {
                StopProcess();
            }

            _targetProcess = Process.GetProcessById(processId);
            _targetProcess.EnableRaisingEvents = true;
            _targetProcess.Exited += TargetProcess_Exited;

            IsSuspended = false;
            _logger.LogInfo($"Attached to process", $"PID: {_targetProcess.Id}, Name: {_targetProcess.ProcessName}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error attaching to process", ex.Message);
            return false;
        }
    }

    public void StopProcess()
    {
        try
        {
            if (_targetProcess != null && !_targetProcess.HasExited)
            {
                foreach (var child in _childProcesses.Values.ToList())
                {
                    try
                    {
                        if (!child.HasExited) child.Kill();
                    }
                    catch { }
                }

                _targetProcess.Kill();
                _targetProcess.WaitForExit(5000);
                _logger.LogInfo($"Process terminated", $"PID: {_targetProcess.Id}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error stopping process", ex.Message);
        }
        finally
        {
            _childProcesses.Clear();
            _targetProcess?.Dispose();
            _targetProcess = null;
            IsSuspended = false;
        }
    }

    public bool SuspendProcess()
    {
        if (_targetProcess == null || _targetProcess.HasExited) return false;

        try
        {
            SuspendProcessTree(_targetProcess.Id);
            foreach (var child in _childProcesses.Values)
            {
                SuspendProcessTree(child.Id);
            }
            IsSuspended = true;
            _logger.LogInfo("Process suspended", $"PID: {_targetProcess.Id}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error suspending process", ex.Message);
            return false;
        }
    }

    public bool ResumeProcess()
    {
        if (_targetProcess == null || _targetProcess.HasExited) return false;

        try
        {
            ResumeProcessTree(_targetProcess.Id);
            foreach (var child in _childProcesses.Values)
            {
                ResumeProcessTree(child.Id);
            }
            IsSuspended = false;
            _logger.LogInfo("Process resumed", $"PID: {_targetProcess.Id}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error resuming process", ex.Message);
            return false;
        }
    }

    public bool LowerPriority()
    {
        if (_targetProcess == null || _targetProcess.HasExited) return false;

        try
        {
            _targetProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
            foreach (var child in _childProcesses.Values)
            {
                try { child.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch { }
            }
            _logger.LogInfo("Process priority lowered", $"PID: {_targetProcess.Id}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error lowering process priority", ex.Message);
            return false;
        }
    }

    public bool RestorePriority()
    {
        if (_targetProcess == null || _targetProcess.HasExited) return false;

        try
        {
            _targetProcess.PriorityClass = ProcessPriorityClass.Normal;
            foreach (var child in _childProcesses.Values)
            {
                try { child.PriorityClass = ProcessPriorityClass.Normal; }
                catch { }
            }
            _logger.LogInfo("Process priority restored", $"PID: {_targetProcess.Id}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error restoring process priority", ex.Message);
            return false;
        }
    }

    public void RefreshChildProcesses()
    {
        if (_targetProcess == null || _targetProcess.HasExited) return;

        var childIds = GetChildProcessIds(_targetProcess.Id);

        foreach (var childId in childIds)
        {
            if (!_childProcesses.ContainsKey(childId))
            {
                try
                {
                    var child = Process.GetProcessById(childId);
                    _childProcesses[childId] = child;
                    ChildProcessAdded?.Invoke(this, childId);
                    _logger.LogInfo("Child process detected", $"Parent: {_targetProcess.Id}, Child: {childId}, Name: {child.ProcessName}");
                }
                catch { }
            }
        }

        var removedIds = _childProcesses.Keys.Where(id => !childIds.Contains(id) || _childProcesses[id].HasExited).ToList();
        foreach (var removedId in removedIds)
        {
            _childProcesses[removedId].Dispose();
            _childProcesses.Remove(removedId);
            ChildProcessRemoved?.Invoke(this, removedId);
        }
    }

    private static void SuspendProcessTree(int processId)
    {
        var threadIds = GetThreadIds(processId);
        foreach (var threadId in threadIds)
        {
            var hThread = Win32Api.OpenThread(ThreadAccessFlags.SuspendResume, false, threadId);
            if (hThread != IntPtr.Zero)
            {
                Win32Api.SuspendThread(hThread);
                Win32Api.CloseHandle(hThread);
            }
        }
    }

    private static void ResumeProcessTree(int processId)
    {
        var threadIds = GetThreadIds(processId);
        foreach (var threadId in threadIds)
        {
            var hThread = Win32Api.OpenThread(ThreadAccessFlags.SuspendResume, false, threadId);
            if (hThread != IntPtr.Zero)
            {
                while (Win32Api.ResumeThread(hThread) > 0) { }
                Win32Api.CloseHandle(hThread);
            }
        }
    }

    private static List<uint> GetThreadIds(int processId)
    {
        var threadIds = new List<uint>();
        var snapshot = Win32Api.CreateToolhelp32Snapshot(Win32Api.TH32CS_SNAPTHREAD, 0);

        if (snapshot != new IntPtr(-1))
        {
            var te = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(THREADENTRY32)) };

            if (Win32Api.Thread32First(snapshot, ref te))
            {
                do
                {
                    if (te.th32OwnerProcessID == (uint)processId)
                    {
                        threadIds.Add(te.th32ThreadID);
                    }
                } while (Win32Api.Thread32Next(snapshot, ref te));
            }

            Win32Api.CloseHandle(snapshot);
        }

        return threadIds;
    }

    private static List<int> GetChildProcessIds(int parentId)
    {
        var childIds = new List<int>();
        var snapshot = Win32Api.CreateToolhelp32Snapshot(Win32Api.TH32CS_SNAPPROCESS, 0);

        if (snapshot != new IntPtr(-1))
        {
            var pe = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };

            if (Win32Api.Process32First(snapshot, ref pe))
            {
                do
                {
                    if (pe.th32ParentProcessID == (uint)parentId)
                    {
                        childIds.Add((int)pe.th32ProcessID);
                    }
                } while (Win32Api.Process32Next(snapshot, ref pe));
            }

            Win32Api.CloseHandle(snapshot);
        }

        return childIds;
    }

    private void TargetProcess_Exited(object? sender, EventArgs e)
    {
        _logger.LogInfo("Process exited", $"PID: {_targetProcess?.Id}");
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopProcess();
        foreach (var child in _childProcesses.Values)
        {
            child.Dispose();
        }
        _childProcesses.Clear();
    }
}
