using VPMonitor.Models;

namespace VPMonitor.Services;

public class ThresholdManager
{
    private readonly ProcessMonitorService _monitorService;
    private readonly LogService _logService;
    private readonly Queue<DateTime> _cpuExceedTimes = new();
    private readonly Queue<DateTime> _memoryExceedTimes = new();
    private readonly object _lockObject = new();
    private bool _cpuActionExecuted = false;
    private bool _memoryActionExecuted = false;

    public ThresholdConfig Config { get; set; } = new();

    public event EventHandler<string>? ThresholdViolation;
    public event EventHandler<ThresholdAction>? ActionExecuted;

    public ThresholdManager(ProcessMonitorService monitorService, LogService logService)
    {
        _monitorService = monitorService;
        _logService = logService;
    }

    public void CheckThresholds(ProcessMetrics metrics)
    {
        if (!Config.Enabled) return;

        lock (_lockObject)
        {
            CheckCpuThreshold(metrics);
            CheckMemoryThreshold(metrics);
        }
    }

    private void CheckCpuThreshold(ProcessMetrics metrics)
    {
        var now = DateTime.Now;

        if (metrics.CpuUsage >= Config.CpuThreshold)
        {
            _cpuExceedTimes.Enqueue(now);

            while (_cpuExceedTimes.Count > 0 &&
                   (now - _cpuExceedTimes.Peek()).TotalSeconds > Config.CpuDurationSeconds)
            {
                _cpuExceedTimes.Dequeue();
            }

            var exceedDuration = _cpuExceedTimes.Count > 0
                ? (now - _cpuExceedTimes.Peek()).TotalSeconds
                : 0;

            if (exceedDuration >= Config.CpuDurationSeconds && !_cpuActionExecuted)
            {
                var message = $"CPU threshold exceeded: {metrics.CpuUsage:F1}% >= {Config.CpuThreshold}% for {exceedDuration:F0}s";
                _logService.LogThresholdExceeded(message, metrics.ProcessId, metrics.ProcessName);
                ThresholdViolation?.Invoke(this, message);
                ExecuteAction(Config.CpuAction, "CPU", metrics);
                _cpuActionExecuted = true;
            }
        }
        else
        {
            if (_cpuExceedTimes.Count > 0)
            {
                _cpuExceedTimes.Clear();
                if (_cpuActionExecuted)
                {
                    _cpuActionExecuted = false;
                    _logService.LogInfo("CPU usage returned to normal, action reset", metrics.ProcessId, metrics.ProcessName);
                }
            }
        }
    }

    private void CheckMemoryThreshold(ProcessMetrics metrics)
    {
        var now = DateTime.Now;

        if (metrics.MemoryUsageMB >= Config.MemoryThresholdMB)
        {
            _memoryExceedTimes.Enqueue(now);

            while (_memoryExceedTimes.Count > 0 &&
                   (now - _memoryExceedTimes.Peek()).TotalSeconds > Config.MemoryDurationSeconds)
            {
                _memoryExceedTimes.Dequeue();
            }

            var exceedDuration = _memoryExceedTimes.Count > 0
                ? (now - _memoryExceedTimes.Peek()).TotalSeconds
                : 0;

            if (exceedDuration >= Config.MemoryDurationSeconds && !_memoryActionExecuted)
            {
                var message = $"Memory threshold exceeded: {metrics.MemoryUsageMB:F1}MB >= {Config.MemoryThresholdMB}MB for {exceedDuration:F0}s";
                _logService.LogThresholdExceeded(message, metrics.ProcessId, metrics.ProcessName);
                ThresholdViolation?.Invoke(this, message);
                ExecuteAction(Config.MemoryAction, "Memory", metrics);
                _memoryActionExecuted = true;
            }
        }
        else
        {
            if (_memoryExceedTimes.Count > 0)
            {
                _memoryExceedTimes.Clear();
                if (_memoryActionExecuted)
                {
                    _memoryActionExecuted = false;
                    _logService.LogInfo("Memory usage returned to normal, action reset", metrics.ProcessId, metrics.ProcessName);
                }
            }
        }
    }

    private void ExecuteAction(ThresholdAction action, string thresholdType, ProcessMetrics metrics)
    {
        if (action == ThresholdAction.None) return;

        try
        {
            switch (action)
            {
                case ThresholdAction.LowerPriority:
                    _monitorService.LowerPriority();
                    _logService.LogActionTaken(
                        $"Auto-lowered process priority due to {thresholdType} threshold violation",
                        metrics.ProcessId, metrics.ProcessName);
                    break;

                case ThresholdAction.Suspend:
                    _monitorService.SuspendProcess();
                    _logService.LogActionTaken(
                        $"Auto-suspended process due to {thresholdType} threshold violation",
                        metrics.ProcessId, metrics.ProcessName);
                    break;

                case ThresholdAction.Terminate:
                    _monitorService.TerminateProcess();
                    _logService.LogActionTaken(
                        $"Auto-terminated process due to {thresholdType} threshold violation",
                        metrics.ProcessId, metrics.ProcessName);
                    break;
            }

            ActionExecuted?.Invoke(this, action);
        }
        catch (Exception ex)
        {
            _logService.LogError(
                $"Failed to execute {action} action: {ex.Message}",
                metrics.ProcessId, metrics.ProcessName);
        }
    }

    public void Reset()
    {
        lock (_lockObject)
        {
            _cpuExceedTimes.Clear();
            _memoryExceedTimes.Clear();
            _cpuActionExecuted = false;
            _memoryActionExecuted = false;
        }
    }
}
