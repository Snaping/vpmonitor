using System;
using System.Collections.Generic;

namespace VPMonitor.Models;

[Serializable]
public class IpcMessage
{
    public string Command { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
}

[Serializable]
public class IpcResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string> Data { get; set; } = new();
    public string RequestId { get; set; } = string.Empty;
}

public static class IpcCommands
{
    public const string Ping = "PING";
    public const string GetStatus = "GET_STATUS";
    public const string StartHighCpu = "START_HIGH_CPU";
    public const string StartHighMemory = "START_HIGH_MEMORY";
    public const string StartDiskActivity = "START_DISK_ACTIVITY";
    public const string StopAll = "STOP_ALL";
    public const string CreateChildProcess = "CREATE_CHILD";
    public const string GetMetrics = "GET_METRICS";
    public const string Crash = "CRASH";
    public const string Hang = "HANG";
    public const string Exit = "EXIT";
}

public class TestProcessStatus
{
    public bool IsHighCpuRunning { get; set; }
    public bool IsHighMemoryRunning { get; set; }
    public bool IsDiskActivityRunning { get; set; }
    public int CpuLoadLevel { get; set; }
    public long MemoryAllocatedBytes { get; set; }
    public int ChildProcessCount { get; set; }
    public int ThreadCount { get; set; }
}
