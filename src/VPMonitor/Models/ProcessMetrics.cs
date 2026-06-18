namespace VPMonitor.Models;

public class ProcessMetrics
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public double MemoryUsageMB { get; set; }
    public double DiskReadBytesPerSec { get; set; }
    public double DiskWriteBytesPerSec { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsSuspended { get; set; }
    public int ChildProcessCount { get; set; }
    public List<int> ChildProcessIds { get; set; } = new();
}
