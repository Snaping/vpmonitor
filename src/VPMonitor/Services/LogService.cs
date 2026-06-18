using System.Diagnostics;
using VPMonitor.Models;

namespace VPMonitor.Services;

public class LogService : IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly object _lockObject = new();
    private StreamWriter? _streamWriter;
    private bool _disposed;

    public event EventHandler<LogEntry>? LogAdded;

    public LogService()
    {
        _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        Directory.CreateDirectory(_logDirectory);
        _logFilePath = Path.Combine(_logDirectory, $"monitor_{DateTime.Now:yyyyMMdd}.log");
        InitializeLogFile();
    }

    private void InitializeLogFile()
    {
        lock (_lockObject)
        {
            _streamWriter = new StreamWriter(_logFilePath, append: true)
            {
                AutoFlush = true
            };
        }
    }

    public void Log(LogEntry entry)
    {
        if (_disposed) return;

        lock (_lockObject)
        {
            try
            {
                _streamWriter?.WriteLine(entry.ToString());
                LogAdded?.Invoke(this, entry);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Log error: {ex.Message}");
            }
        }
    }

    public void LogInfo(string message, int? processId = null, string? processName = null, string? details = null)
    {
        Log(new LogEntry
        {
            EventType = LogEventType.Info,
            Message = message,
            ProcessId = processId,
            ProcessName = processName,
            Details = details
        });
    }

    public void LogWarning(string message, int? processId = null, string? processName = null, string? details = null)
    {
        Log(new LogEntry
        {
            EventType = LogEventType.Warning,
            Message = message,
            ProcessId = processId,
            ProcessName = processName,
            Details = details
        });
    }

    public void LogError(string message, int? processId = null, string? processName = null, string? details = null)
    {
        Log(new LogEntry
        {
            EventType = LogEventType.Error,
            Message = message,
            ProcessId = processId,
            ProcessName = processName,
            Details = details
        });
    }

    public void LogThresholdExceeded(string message, int? processId = null, string? processName = null, string? details = null)
    {
        Log(new LogEntry
        {
            EventType = LogEventType.ThresholdExceeded,
            Message = message,
            ProcessId = processId,
            ProcessName = processName,
            Details = details
        });
    }

    public void LogActionTaken(string message, int? processId = null, string? processName = null, string? details = null)
    {
        Log(new LogEntry
        {
            EventType = LogEventType.ActionTaken,
            Message = message,
            ProcessId = processId,
            ProcessName = processName,
            Details = details
        });
    }

    public void LogProcessEvent(string message, int? processId = null, string? processName = null, string? details = null)
    {
        Log(new LogEntry
        {
            EventType = LogEventType.ProcessEvent,
            Message = message,
            ProcessId = processId,
            ProcessName = processName,
            Details = details
        });
    }

    public List<LogEntry> GetRecentLogs(int count = 100)
    {
        var logs = new List<LogEntry>();
        if (!File.Exists(_logFilePath)) return logs;

        lock (_lockObject)
        {
            try
            {
                var lines = File.ReadAllLines(_logFilePath);
                var recentLines = lines.Skip(Math.Max(0, lines.Length - count));
                foreach (var line in recentLines)
                {
                    logs.Add(new LogEntry { Message = line });
                }
            }
            catch
            {
            }
        }
        return logs;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lockObject)
        {
            _streamWriter?.Dispose();
            _streamWriter = null;
        }
    }
}
