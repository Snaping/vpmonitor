namespace VPMonitor.Models;

public enum ThresholdAction
{
    None,
    LowerPriority,
    Suspend,
    Terminate
}

public class ThresholdConfig
{
    public double CpuThreshold { get; set; } = 70.0;
    public int CpuDurationSeconds { get; set; } = 10;
    public ThresholdAction CpuAction { get; set; } = ThresholdAction.LowerPriority;
    public double MemoryThresholdMB { get; set; } = 1024.0;
    public int MemoryDurationSeconds { get; set; } = 10;
    public ThresholdAction MemoryAction { get; set; } = ThresholdAction.LowerPriority;
    public bool Enabled { get; set; } = true;
}
