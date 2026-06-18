namespace VPMonitor.Models;

public enum LogEventType
{
    Info,
    Warning,
    Error,
    ThresholdExceeded,
    ActionTaken,
    ProcessEvent
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogEventType EventType { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? ProcessId { get; set; }
    public string? ProcessName { get; set; }
    public string? Details { get; set; }

    public override string ToString()
    {
        return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{EventType}] {Message}" +
               (ProcessId.HasValue ? $" [PID:{ProcessId}]" : "") +
               (!string.IsNullOrEmpty(ProcessName) ? $" [{ProcessName}]" : "") +
               (!string.IsNullOrEmpty(Details) ? $" - {Details}" : "");
    }
}
