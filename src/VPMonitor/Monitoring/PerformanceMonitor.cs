using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using VPMonitor.Core;
using VPMonitor.Models;

namespace VPMonitor.Monitoring;

public class PerformanceMonitor : IDisposable
{
    private readonly ProcessManager _processManager;
    private readonly Logger _logger;
    private readonly System.Threading.Timer _monitorTimer;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memoryCounter;
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;
    private readonly Dictionary<int, PerformanceCounter> _childCpuCounters = new();
    private readonly Dictionary<int, PerformanceCounter> _childMemoryCounters = new();
    private bool _disposed;

    public ProcessMetrics CurrentMetrics { get; private set; } = new();
    public MetricsHistory History { get; } = new();
    public int UpdateIntervalMs { get; set; } = 1000;

    public event EventHandler<ProcessMetrics>? MetricsUpdated;

    public PerformanceMonitor(ProcessManager processManager, Logger logger)
    {
        _processManager = processManager;
        _logger = logger;
        _monitorTimer = new System.Threading.Timer(MonitorCallback, null, Timeout.Infinite, Timeout.Infinite);

        _processManager.ChildProcessAdded += OnChildProcessAdded;
        _processManager.ChildProcessRemoved += OnChildProcessRemoved;
    }

    public void Start()
    {
        if (_processManager.TargetProcess == null) return;

        try
        {
            var process = _processManager.TargetProcess;
            var instanceName = GetProcessInstanceName(process.Id);

            _cpuCounter = new PerformanceCounter("Process", "% Processor Time", instanceName, true);
            _memoryCounter = new PerformanceCounter("Process", "Private Bytes", instanceName, true);
            _diskReadCounter = new PerformanceCounter("Process", "IO Read Bytes/sec", instanceName, true);
            _diskWriteCounter = new PerformanceCounter("Process", "IO Write Bytes/sec", instanceName, true);

            _cpuCounter.NextValue();
            _memoryCounter.NextValue();
            _diskReadCounter.NextValue();
            _diskWriteCounter.NextValue();

            RefreshChildCounters();

            _monitorTimer.Change(0, UpdateIntervalMs);
            _logger.LogInfo("Performance monitoring started", $"PID: {process.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to start performance monitoring", ex.Message);
        }
    }

    public void Stop()
    {
        _monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
        DisposeCounters();
        _logger.LogInfo("Performance monitoring stopped");
    }

    private void MonitorCallback(object? state)
    {
        if (_disposed || _processManager.TargetProcess == null || _processManager.TargetProcess.HasExited) return;
        if (_cpuCounter == null) return;

        try
        {
            var process = _processManager.TargetProcess;
            process.Refresh();

            var cpuUsage = _cpuCounter.NextValue() / Environment.ProcessorCount;
            var memoryBytes = _memoryCounter!.NextValue();
            var memoryMb = memoryBytes / (1024 * 1024);

            var diskReadRate = _diskReadCounter!.NextValue() / 1024.0;
            var diskWriteRate = _diskWriteCounter!.NextValue() / 1024.0;

            foreach (var kvp in _childCpuCounters)
            {
                try
                {
                    cpuUsage += kvp.Value.NextValue() / Environment.ProcessorCount;
                    memoryBytes += _childMemoryCounters[kvp.Key].NextValue();
                }
                catch { }
            }

            cpuUsage = Math.Min(100, cpuUsage);
            memoryMb = memoryBytes / (1024 * 1024);

            var metrics = new ProcessMetrics
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                CpuUsage = Math.Round(cpuUsage, 1),
                MemoryUsageMb = Math.Round(memoryMb, 1),
                MemoryUsageBytes = (long)memoryBytes,
                DiskReadRate = Math.Round(diskReadRate, 2),
                DiskWriteRate = Math.Round(diskWriteRate, 2),
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount,
                IsSuspended = _processManager.IsSuspended,
                Timestamp = DateTime.Now
            };

            metrics.ChildProcesses = new List<ProcessMetrics>();
            foreach (var child in _processManager.ChildProcesses)
            {
                try
                {
                    var childProcess = child.Value;
                    childProcess.Refresh();
                    metrics.ChildProcesses.Add(new ProcessMetrics
                    {
                        ProcessId = child.Key,
                        ProcessName = childProcess.ProcessName,
                        ThreadCount = childProcess.Threads.Count,
                        HandleCount = childProcess.HandleCount,
                        MemoryUsageBytes = childProcess.PrivateMemorySize64
                    });
                }
                catch { }
            }

            History.AddCpu(metrics.CpuUsage);
            History.AddMemory(metrics.MemoryUsageMb);
            History.AddDiskRead(metrics.DiskReadRate);
            History.AddDiskWrite(metrics.DiskWriteRate);

            CurrentMetrics = metrics;
            MetricsUpdated?.Invoke(this, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error collecting metrics", ex.Message);
        }
    }

    private void OnChildProcessAdded(object? sender, int childId)
    {
        try
        {
            var instanceName = GetProcessInstanceName(childId);
            _childCpuCounters[childId] = new PerformanceCounter("Process", "% Processor Time", instanceName, true);
            _childMemoryCounters[childId] = new PerformanceCounter("Process", "Private Bytes", instanceName, true);
            _childCpuCounters[childId].NextValue();
            _childMemoryCounters[childId].NextValue();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to create child process counters", $"PID: {childId}, Error: {ex.Message}");
        }
    }

    private void OnChildProcessRemoved(object? sender, int childId)
    {
        if (_childCpuCounters.TryGetValue(childId, out var cpuCounter))
        {
            cpuCounter.Dispose();
            _childCpuCounters.Remove(childId);
        }
        if (_childMemoryCounters.TryGetValue(childId, out var memCounter))
        {
            memCounter.Dispose();
            _childMemoryCounters.Remove(childId);
        }
    }

    private void RefreshChildCounters()
    {
        foreach (var counter in _childCpuCounters.Values) counter.Dispose();
        foreach (var counter in _childMemoryCounters.Values) counter.Dispose();
        _childCpuCounters.Clear();
        _childMemoryCounters.Clear();

        foreach (var child in _processManager.ChildProcesses)
        {
            OnChildProcessAdded(this, child.Key);
        }
    }

    private static string GetProcessInstanceName(int pid)
    {
        var category = new PerformanceCounterCategory("Process");
        var instances = category.GetInstanceNames();

        foreach (var instance in instances)
        {
            if (instance.StartsWith("ProcessName")) continue;
            try
            {
                using var counter = new PerformanceCounter("Process", "ID Process", instance, true);
                if ((int)counter.RawValue == pid)
                {
                    return instance;
                }
            }
            catch { }
        }

        return "Unknown_" + pid;
    }

    private void DisposeCounters()
    {
        _cpuCounter?.Dispose();
        _memoryCounter?.Dispose();
        _diskReadCounter?.Dispose();
        _diskWriteCounter?.Dispose();
        _cpuCounter = null;
        _memoryCounter = null;
        _diskReadCounter = null;
        _diskWriteCounter = null;

        foreach (var counter in _childCpuCounters.Values) counter.Dispose();
        foreach (var counter in _childMemoryCounters.Values) counter.Dispose();
        _childCpuCounters.Clear();
        _childMemoryCounters.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitorTimer.Dispose();
        DisposeCounters();
    }
}
