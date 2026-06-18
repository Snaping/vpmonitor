namespace VPMonitor.Models;

public enum IpcCommandType
{
    Ping,
    StartHighLoad,
    StopHighLoad,
    SetLoadLevel,
    AllocateMemory,
    FreeMemory,
    CreateThreads,
    ExitProcess,
    GetStatus,
    StatusResponse,
    CriticalStateTest,
    Error
}

public class IpcMessage
{
    public IpcCommandType Command { get; set; }
    public string? Payload { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
}

public class TestProcessStatus
{
    public int ProcessId { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryUsedMB { get; set; }
    public int ThreadCount { get; set; }
    public bool IsHighLoadRunning { get; set; }
    public int LoadLevel { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<string> CriticalTests { get; set; } = new();
}
