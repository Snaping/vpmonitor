using System.Diagnostics;
using System.Management;
using VPMonitor.Models;

namespace VPMonitor.Services;

public class ChildProcessTracker : IDisposable
{
    private readonly int _parentProcessId;
    private readonly Dictionary<int, Process> _childProcesses = new();
    private readonly object _lockObject = new();
    private ManagementEventWatcher? _processStartWatcher;
    private ManagementEventWatcher? _processStopWatcher;
    private bool _disposed;
    private readonly LogService _logService;

    public event EventHandler<int>? ChildProcessStarted;
    public event EventHandler<int>? ChildProcessExited;

    public IReadOnlyDictionary<int, Process> ChildProcesses
    {
        get
        {
            lock (_lockObject)
            {
                return new Dictionary<int, Process>(_childProcesses);
            }
        }
    }

    public ChildProcessTracker(int parentProcessId, LogService logService)
    {
        _parentProcessId = parentProcessId;
        _logService = logService;
        InitializeWmiWatchers();
        EnumerateExistingChildProcesses();
    }

    private void InitializeWmiWatchers()
    {
        try
        {
            var startQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
            _processStartWatcher = new ManagementEventWatcher(startQuery);
            _processStartWatcher.EventArrived += ProcessStartWatcher_EventArrived;
            _processStartWatcher.Start();

            var stopQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace");
            _processStopWatcher = new ManagementEventWatcher(stopQuery);
            _processStopWatcher.EventArrived += ProcessStopWatcher_EventArrived;
            _processStopWatcher.Start();
        }
        catch (Exception ex)
        {
            _logService.LogWarning($"WMI watcher initialization failed: {ex.Message}", _parentProcessId);
        }
    }

    private void ProcessStartWatcher_EventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processId = Convert.ToInt32(e.NewEvent["ProcessID"]);
            var parentProcessId = Convert.ToInt32(e.NewEvent["ParentProcessID"]);

            if (IsChildOfParent(parentProcessId))
            {
                AddChildProcess(processId);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error handling process start: {ex.Message}");
        }
    }

    private void ProcessStopWatcher_EventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processId = Convert.ToInt32(e.NewEvent["ProcessID"]);
            RemoveChildProcess(processId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error handling process stop: {ex.Message}");
        }
    }

    private bool IsChildOfParent(int potentialParentId)
    {
        if (potentialParentId == _parentProcessId) return true;

        lock (_lockObject)
        {
            return _childProcesses.ContainsKey(potentialParentId);
        }
    }

    private void EnumerateExistingChildProcesses()
    {
        try
        {
            var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId, ParentProcessId FROM Win32_Process WHERE ParentProcessId = {_parentProcessId}");

            foreach (var obj in searcher.Get())
            {
                var processId = Convert.ToInt32(obj["ProcessId"]);
                AddChildProcess(processId);
                EnumerateChildProcessesRecursive(processId);
            }
        }
        catch (Exception ex)
        {
            _logService.LogWarning($"Failed to enumerate child processes: {ex.Message}", _parentProcessId);
        }
    }

    private void EnumerateChildProcessesRecursive(int parentId)
    {
        try
        {
            var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId, ParentProcessId FROM Win32_Process WHERE ParentProcessId = {parentId}");

            foreach (var obj in searcher.Get())
            {
                var processId = Convert.ToInt32(obj["ProcessId"]);
                AddChildProcess(processId);
                EnumerateChildProcessesRecursive(processId);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error enumerating recursive children: {ex.Message}");
        }
    }

    private void AddChildProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            lock (_lockObject)
            {
                if (!_childProcesses.ContainsKey(processId))
                {
                    _childProcesses[processId] = process;
                    _logService.LogProcessEvent("Child process started", processId, process.ProcessName);
                    ChildProcessStarted?.Invoke(this, processId);
                }
            }
        }
        catch (ArgumentException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding child process {processId}: {ex.Message}");
        }
    }

    private void RemoveChildProcess(int processId)
    {
        lock (_lockObject)
        {
            if (_childProcesses.TryGetValue(processId, out var process))
            {
                _childProcesses.Remove(processId);
                _logService.LogProcessEvent("Child process exited", processId, process.ProcessName);
                ChildProcessExited?.Invoke(this, processId);
            }
        }
    }

    public List<int> GetAllChildProcessIds()
    {
        lock (_lockObject)
        {
            return new List<int>(_childProcesses.Keys);
        }
    }

    public int GetChildProcessCount()
    {
        lock (_lockObject)
        {
            return _childProcesses.Count;
        }
    }

    public void SuspendAllChildProcesses()
    {
        lock (_lockObject)
        {
            foreach (var kvp in _childProcesses)
            {
                try
                {
                    ProcessHelper.SuspendProcess(kvp.Value);
                    _logService.LogActionTaken("Child process suspended", kvp.Key, kvp.Value.ProcessName);
                }
                catch (Exception ex)
                {
                    _logService.LogError($"Failed to suspend child process: {ex.Message}", kvp.Key, kvp.Value.ProcessName);
                }
            }
        }
    }

    public void ResumeAllChildProcesses()
    {
        lock (_lockObject)
        {
            foreach (var kvp in _childProcesses)
            {
                try
                {
                    ProcessHelper.ResumeProcess(kvp.Value);
                    _logService.LogActionTaken("Child process resumed", kvp.Key, kvp.Value.ProcessName);
                }
                catch (Exception ex)
                {
                    _logService.LogError($"Failed to resume child process: {ex.Message}", kvp.Key, kvp.Value.ProcessName);
                }
            }
        }
    }

    public void TerminateAllChildProcesses()
    {
        lock (_lockObject)
        {
            foreach (var kvp in _childProcesses.ToList())
            {
                try
                {
                    if (!kvp.Value.HasExited)
                    {
                        kvp.Value.Kill();
                        _logService.LogActionTaken("Child process terminated", kvp.Key, kvp.Value.ProcessName);
                    }
                }
                catch (Exception ex)
                {
                    _logService.LogError($"Failed to terminate child process: {ex.Message}", kvp.Key, kvp.Value.ProcessName);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _processStartWatcher?.Stop();
            _processStartWatcher?.Dispose();
            _processStopWatcher?.Stop();
            _processStopWatcher?.Dispose();
        }
        catch
        {
        }

        lock (_lockObject)
        {
            foreach (var process in _childProcesses.Values)
            {
                process.Dispose();
            }
            _childProcesses.Clear();
        }
    }
}
