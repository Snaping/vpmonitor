using System.Diagnostics;
using System.Management;
using VPMonitor.Models;

namespace VPMonitor.Services;

public class ProcessMonitorService : IDisposable
{
    private Process? _targetProcess;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memoryCounter;
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;
    private readonly int _processId;
    private readonly string _processName;
    private ChildProcessTracker? _childTracker;
    private readonly LogService _logService;
    private bool _disposed;
    private readonly object _lockObject = new();

    public ProcessMetrics CurrentMetrics { get; private set; } = new();
    public event EventHandler<ProcessMetrics>? MetricsUpdated;

    public int ProcessId => _processId;
    public string ProcessName => _processName;
    public Process? TargetProcess => _targetProcess;
    public ChildProcessTracker? ChildTracker => _childTracker;

    public ProcessMonitorService(int processId, LogService logService)
    {
        _processId = processId;
        _logService = logService;
        _targetProcess = Process.GetProcessById(processId);
        _processName = _targetProcess.ProcessName;
        InitializeCounters();
        _childTracker = new ChildProcessTracker(processId, logService);
        _logService.LogProcessEvent("Started monitoring process", processId, _processName);
    }

    public ProcessMonitorService(string executablePath, string arguments, LogService logService)
    {
        _logService = logService;
        var startInfo = new ProcessStartInfo(executablePath, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        _targetProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start process");
        _processId = _targetProcess.Id;
        _processName = _targetProcess.ProcessName;
        InitializeCounters();
        _childTracker = new ChildProcessTracker(_processId, logService);
        _logService.LogProcessEvent("Started and monitoring new process", _processId, _processName, executablePath);
    }

    private void InitializeCounters()
    {
        try
        {
            var instanceName = GetPerformanceCounterInstanceName(_processId);
            if (!string.IsNullOrEmpty(instanceName))
            {
                _cpuCounter = new PerformanceCounter("Process", "% Processor Time", instanceName, true);
                _memoryCounter = new PerformanceCounter("Process", "Private Bytes", instanceName, true);
                _diskReadCounter = new PerformanceCounter("Process", "IO Read Bytes/sec", instanceName, true);
                _diskWriteCounter = new PerformanceCounter("Process", "IO Write Bytes/sec", instanceName, true);

                _cpuCounter.NextValue();
                _memoryCounter.NextValue();
                _diskReadCounter.NextValue();
                _diskWriteCounter.NextValue();
            }
        }
        catch (Exception ex)
        {
            _logService.LogWarning($"Failed to initialize performance counters: {ex.Message}", _processId, _processName);
        }
    }

    private string GetPerformanceCounterInstanceName(int pid)
    {
        try
        {
            var cat = new PerformanceCounterCategory("Process");
            var instances = cat.GetInstanceNames();

            foreach (var instance in instances)
            {
                try
                {
                    using var counter = new PerformanceCounter("Process", "ID Process", instance, true);
                    if ((int)counter.RawValue == pid)
                    {
                        return instance;
                    }
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting instance name: {ex.Message}");
        }
        return null!;
    }

    public ProcessMetrics GetMetrics()
    {
        if (_targetProcess == null || _targetProcess.HasExited)
        {
            return new ProcessMetrics
            {
                ProcessId = _processId,
                ProcessName = _processName,
                Timestamp = DateTime.Now
            };
        }

        lock (_lockObject)
        {
            try
            {
                _targetProcess.Refresh();

                var cpuUsage = 0.0;
                var memoryUsageMB = 0.0;
                var diskReadBytesPerSec = 0.0;
                var diskWriteBytesPerSec = 0.0;

                if (_cpuCounter != null)
                {
                    try
                    {
                        cpuUsage = _cpuCounter.NextValue() / Environment.ProcessorCount;
                    }
                    catch
                    {
                        ReinitializeCounters();
                    }
                }

                if (_memoryCounter != null)
                {
                    try
                    {
                        memoryUsageMB = _memoryCounter.NextValue() / (1024 * 1024);
                    }
                    catch
                    {
                    }
                }
                else
                {
                    memoryUsageMB = _targetProcess.WorkingSet64 / (1024.0 * 1024.0);
                }

                if (_diskReadCounter != null)
                {
                    try
                    {
                        diskReadBytesPerSec = _diskReadCounter.NextValue();
                    }
                    catch
                    {
                    }
                }

                if (_diskWriteCounter != null)
                {
                    try
                    {
                        diskWriteBytesPerSec = _diskWriteCounter.NextValue();
                    }
                    catch
                    {
                    }
                }

                var isSuspended = ProcessHelper.IsProcessSuspended(_targetProcess);
                var childIds = _childTracker?.GetAllChildProcessIds() ?? new List<int>();

                var metrics = new ProcessMetrics
                {
                    ProcessId = _processId,
                    ProcessName = _processName,
                    CpuUsage = Math.Round(cpuUsage, 2),
                    MemoryUsageMB = Math.Round(memoryUsageMB, 2),
                    DiskReadBytesPerSec = Math.Round(diskReadBytesPerSec, 2),
                    DiskWriteBytesPerSec = Math.Round(diskWriteBytesPerSec, 2),
                    ThreadCount = _targetProcess.Threads.Count,
                    HandleCount = _targetProcess.HandleCount,
                    Timestamp = DateTime.Now,
                    IsSuspended = isSuspended,
                    ChildProcessCount = childIds.Count,
                    ChildProcessIds = childIds
                };

                CurrentMetrics = metrics;
                MetricsUpdated?.Invoke(this, metrics);

                return metrics;
            }
            catch (Exception ex)
            {
                _logService.LogWarning($"Error getting metrics: {ex.Message}", _processId, _processName);
                return new ProcessMetrics
                {
                    ProcessId = _processId,
                    ProcessName = _processName,
                    Timestamp = DateTime.Now
                };
            }
        }
    }

    private void ReinitializeCounters()
    {
        try
        {
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
            InitializeCounters();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reinitializing counters: {ex.Message}");
        }
    }

    public bool IsProcessRunning()
    {
        return _targetProcess != null && !_targetProcess.HasExited;
    }

    public void SuspendProcess()
    {
        if (_targetProcess == null || _targetProcess.HasExited) return;

        try
        {
            ProcessHelper.SuspendProcess(_targetProcess);
            _childTracker?.SuspendAllChildProcesses();
            _logService.LogActionTaken("Process suspended", _processId, _processName);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to suspend process: {ex.Message}", _processId, _processName);
            throw;
        }
    }

    public void ResumeProcess()
    {
        if (_targetProcess == null || _targetProcess.HasExited) return;

        try
        {
            ProcessHelper.ResumeProcess(_targetProcess);
            _childTracker?.ResumeAllChildProcesses();
            _logService.LogActionTaken("Process resumed", _processId, _processName);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to resume process: {ex.Message}", _processId, _processName);
            throw;
        }
    }

    public void LowerPriority()
    {
        if (_targetProcess == null || _targetProcess.HasExited) return;

        try
        {
            ProcessHelper.LowerProcessPriority(_targetProcess);
            _logService.LogActionTaken("Process priority lowered", _processId, _processName,
                $"New priority: {_targetProcess.PriorityClass}");
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to lower process priority: {ex.Message}", _processId, _processName);
            throw;
        }
    }

    public void TerminateProcess()
    {
        if (_targetProcess == null || _targetProcess.HasExited) return;

        try
        {
            _childTracker?.TerminateAllChildProcesses();
            ProcessHelper.TerminateProcess(_targetProcess);
            _logService.LogActionTaken("Process terminated", _processId, _processName);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to terminate process: {ex.Message}", _processId, _processName);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
            _childTracker?.Dispose();
            _targetProcess?.Dispose();
        }
        catch
        {
        }

        _logService.LogProcessEvent("Stopped monitoring process", _processId, _processName);
    }
}
