using System;
using System.Collections.Generic;

namespace VPMonitor.Models;

public class ProcessMetrics
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public double MemoryUsageMb { get; set; }
    public long MemoryUsageBytes { get; set; }
    public double DiskReadRate { get; set; }
    public double DiskWriteRate { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsSuspended { get; set; }
    public List<ProcessMetrics>? ChildProcesses { get; set; }
}

public class MetricsHistory
{
    public Queue<double> CpuHistory { get; } = new Queue<double>(60);
    public Queue<double> MemoryHistory { get; } = new Queue<double>(60);
    public Queue<double> DiskReadHistory { get; } = new Queue<double>(60);
    public Queue<double> DiskWriteHistory { get; } = new Queue<double>(60);

    public void AddCpu(double value)
    {
        if (CpuHistory.Count >= 60) CpuHistory.Dequeue();
        CpuHistory.Enqueue(value);
    }

    public void AddMemory(double value)
    {
        if (MemoryHistory.Count >= 60) MemoryHistory.Dequeue();
        MemoryHistory.Enqueue(value);
    }

    public void AddDiskRead(double value)
    {
        if (DiskReadHistory.Count >= 60) DiskReadHistory.Dequeue();
        DiskReadHistory.Enqueue(value);
    }

    public void AddDiskWrite(double value)
    {
        if (DiskWriteHistory.Count >= 60) DiskWriteHistory.Dequeue();
        DiskWriteHistory.Enqueue(value);
    }
}
