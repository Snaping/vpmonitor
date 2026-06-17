using System;

namespace VPMonitor.Models;

public enum ThresholdAction
{
    LowerPriority,
    Suspend,
    Kill,
    LogOnly
}

public class CpuThresholdConfig
{
    public double CpuPercentage { get; set; } = 70;
    public int DurationSeconds { get; set; } = 10;
    public ThresholdAction Action { get; set; } = ThresholdAction.LogOnly;
    public bool Enabled { get; set; } = true;
}

public class MemoryThresholdConfig
{
    public double MemoryMb { get; set; } = 500;
    public int DurationSeconds { get; set; } = 10;
    public ThresholdAction Action { get; set; } = ThresholdAction.LogOnly;
    public bool Enabled { get; set; } = true;
}

public class ThresholdViolation
{
    public string Type { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Threshold { get; set; }
    public int ViolationCount { get; set; }
    public int RequiredDuration { get; set; }
    public DateTime FirstViolationTime { get; set; }
    public bool ActionTaken { get; set; }
    public string ActionDescription { get; set; } = string.Empty;
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }

    public override string ToString()
    {
        return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message}" + 
               (string.IsNullOrEmpty(Details) ? "" : $" - {Details}");
    }
}
