using System;
using System.Collections.Generic;
using VPMonitor.Core;
using VPMonitor.Models;

namespace VPMonitor.Monitoring;

public class ThresholdMonitor : IDisposable
{
    private readonly ProcessManager _processManager;
    private readonly PerformanceMonitor _performanceMonitor;
    private readonly Logger _logger;
    private readonly System.Timers.Timer _checkTimer;
    private bool _disposed;

    public CpuThresholdConfig CpuConfig { get; set; } = new();
    public MemoryThresholdConfig MemoryConfig { get; set; } = new();

    private DateTime _cpuViolationStartTime;
    private DateTime _memoryViolationStartTime;
    private bool _cpuViolationActive;
    private bool _memoryViolationActive;
    private bool _cpuActionTaken;
    private bool _memoryActionTaken;

    public event EventHandler<ThresholdViolation>? ThresholdTriggered;

    public ThresholdMonitor(ProcessManager processManager, PerformanceMonitor performanceMonitor, Logger logger)
    {
        _processManager = processManager;
        _performanceMonitor = performanceMonitor;
        _logger = logger;
        _checkTimer = new System.Timers.Timer(1000);
        _checkTimer.Elapsed += CheckTimer_Elapsed;

        _performanceMonitor.MetricsUpdated += OnMetricsUpdated;
    }

    public void Start()
    {
        ResetViolationStates();
        _checkTimer.Start();
        _logger.LogInfo("Threshold monitoring started");
    }

    public void Stop()
    {
        _checkTimer.Stop();
        _logger.LogInfo("Threshold monitoring stopped");
    }

    private void OnMetricsUpdated(object? sender, ProcessMetrics metrics)
    {
    }

    private void CheckTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_disposed || !_processManager.IsProcessRunning) return;

        var metrics = _performanceMonitor.CurrentMetrics;

        if (CpuConfig.Enabled)
        {
            CheckCpuThreshold(metrics);
        }

        if (MemoryConfig.Enabled)
        {
            CheckMemoryThreshold(metrics);
        }
    }

    private void CheckCpuThreshold(ProcessMetrics metrics)
    {
        var isViolating = metrics.CpuUsage >= CpuConfig.CpuPercentage;

        if (isViolating)
        {
            if (!_cpuViolationActive)
            {
                _cpuViolationStartTime = DateTime.Now;
                _cpuViolationActive = true;
                _logger.LogWarning($"CPU threshold exceeded: {metrics.CpuUsage}% >= {CpuConfig.CpuPercentage}%");
            }

            var violationDuration = DateTime.Now - _cpuViolationStartTime;
            var violationSeconds = (int)violationDuration.TotalSeconds;

            if (violationDuration.TotalSeconds >= CpuConfig.DurationSeconds && !_cpuActionTaken)
            {
                ExecuteCpuAction(metrics, violationSeconds);
            }
            else
            {
                ThresholdTriggered?.Invoke(this, new ThresholdViolation
                {
                    Type = "CPU",
                    Value = metrics.CpuUsage,
                    Threshold = CpuConfig.CpuPercentage,
                    ViolationCount = violationSeconds,
                    RequiredDuration = CpuConfig.DurationSeconds,
                    FirstViolationTime = _cpuViolationStartTime
                });
            }
        }
        else
        {
            if (_cpuViolationActive)
            {
                _logger.LogInfo($"CPU back to normal: {metrics.CpuUsage}%");
                _cpuViolationActive = false;
                _cpuActionTaken = false;
            }
        }
    }

    private void CheckMemoryThreshold(ProcessMetrics metrics)
    {
        var isViolating = metrics.MemoryUsageMb >= MemoryConfig.MemoryMb;

        if (isViolating)
        {
            if (!_memoryViolationActive)
            {
                _memoryViolationStartTime = DateTime.Now;
                _memoryViolationActive = true;
                _logger.LogWarning($"Memory threshold exceeded: {metrics.MemoryUsageMb}MB >= {MemoryConfig.MemoryMb}MB");
            }

            var violationDuration = DateTime.Now - _memoryViolationStartTime;
            var violationSeconds = (int)violationDuration.TotalSeconds;

            if (violationDuration.TotalSeconds >= MemoryConfig.DurationSeconds && !_memoryActionTaken)
            {
                ExecuteMemoryAction(metrics, violationSeconds);
            }
            else
            {
                ThresholdTriggered?.Invoke(this, new ThresholdViolation
                {
                    Type = "Memory",
                    Value = metrics.MemoryUsageMb,
                    Threshold = MemoryConfig.MemoryMb,
                    ViolationCount = violationSeconds,
                    RequiredDuration = MemoryConfig.DurationSeconds,
                    FirstViolationTime = _memoryViolationStartTime
                });
            }
        }
        else
        {
            if (_memoryViolationActive)
            {
                _logger.LogInfo($"Memory back to normal: {metrics.MemoryUsageMb}MB");
                _memoryViolationActive = false;
                _memoryActionTaken = false;
            }
        }
    }

    private void ExecuteCpuAction(ProcessMetrics metrics, int durationSeconds)
    {
        _cpuActionTaken = true;
        var action = CpuConfig.Action;
        var actionDesc = GetActionDescription(action);

        _logger.LogThresholdViolation(
            $"CPU threshold sustained for {durationSeconds}s. Executing action: {actionDesc}",
            $"CPU: {metrics.CpuUsage}%, Threshold: {CpuConfig.CpuPercentage}%");

        ExecuteAction(action, "CPU");

        ThresholdTriggered?.Invoke(this, new ThresholdViolation
        {
            Type = "CPU",
            Value = metrics.CpuUsage,
            Threshold = CpuConfig.CpuPercentage,
            ViolationCount = durationSeconds,
            RequiredDuration = CpuConfig.DurationSeconds,
            FirstViolationTime = _cpuViolationStartTime,
            ActionTaken = true,
            ActionDescription = actionDesc
        });
    }

    private void ExecuteMemoryAction(ProcessMetrics metrics, int durationSeconds)
    {
        _memoryActionTaken = true;
        var action = MemoryConfig.Action;
        var actionDesc = GetActionDescription(action);

        _logger.LogThresholdViolation(
            $"Memory threshold sustained for {durationSeconds}s. Executing action: {actionDesc}",
            $"Memory: {metrics.MemoryUsageMb}MB, Threshold: {MemoryConfig.MemoryMb}MB");

        ExecuteAction(action, "Memory");

        ThresholdTriggered?.Invoke(this, new ThresholdViolation
        {
            Type = "Memory",
            Value = metrics.MemoryUsageMb,
            Threshold = MemoryConfig.MemoryMb,
            ViolationCount = durationSeconds,
            RequiredDuration = MemoryConfig.DurationSeconds,
            FirstViolationTime = _memoryViolationStartTime,
            ActionTaken = true,
            ActionDescription = actionDesc
        });
    }

    private void ExecuteAction(ThresholdAction action, string source)
    {
        switch (action)
        {
            case ThresholdAction.LowerPriority:
                _processManager.LowerPriority();
                break;
            case ThresholdAction.Suspend:
                _processManager.SuspendProcess();
                break;
            case ThresholdAction.Kill:
                _processManager.StopProcess();
                break;
            case ThresholdAction.LogOnly:
            default:
                break;
        }
    }

    private static string GetActionDescription(ThresholdAction action)
    {
        return action switch
        {
            ThresholdAction.LowerPriority => "Lower Priority",
            ThresholdAction.Suspend => "Suspend Process",
            ThresholdAction.Kill => "Terminate Process",
            ThresholdAction.LogOnly => "Log Only",
            _ => "Unknown"
        };
    }

    public void ResetViolationStates()
    {
        _cpuViolationActive = false;
        _memoryViolationActive = false;
        _cpuActionTaken = false;
        _memoryActionTaken = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _checkTimer.Dispose();
    }
}
