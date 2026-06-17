using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using VPMonitor.Models;

namespace VPMonitor.Core;

public class Logger : IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly ConcurrentQueue<LogEntry> _logQueue = new();
    private readonly System.Threading.Timer _writeTimer;
    private readonly object _fileLock = new();
    private bool _disposed;

    public event EventHandler<LogEntry>? LogAdded;

    public Logger()
    {
        _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        Directory.CreateDirectory(_logDirectory);
        _logFilePath = Path.Combine(_logDirectory, $"vpmonitor_{DateTime.Now:yyyyMMdd}.log");
        _writeTimer = new System.Threading.Timer(WriteLogs, null, 1000, 1000);
    }

    public void LogInfo(string message, string? details = null)
    {
        AddLog(new LogEntry { Level = "INFO", Message = message, Details = details });
    }

    public void LogWarning(string message, string? details = null)
    {
        AddLog(new LogEntry { Level = "WARN", Message = message, Details = details });
    }

    public void LogError(string message, string? details = null)
    {
        AddLog(new LogEntry { Level = "ERROR", Message = message, Details = details });
    }

    public void LogThresholdViolation(string message, string? details = null)
    {
        AddLog(new LogEntry { Level = "ALERT", Message = message, Details = details });
    }

    private void AddLog(LogEntry entry)
    {
        _logQueue.Enqueue(entry);
        LogAdded?.Invoke(this, entry);
    }

    private void WriteLogs(object? state)
    {
        if (_disposed) return;

        var sb = new StringBuilder();
        while (_logQueue.TryDequeue(out var entry))
        {
            sb.AppendLine(entry.ToString());
        }

        if (sb.Length > 0)
        {
            lock (_fileLock)
            {
                File.AppendAllText(_logFilePath, sb.ToString());
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writeTimer.Dispose();
        WriteLogs(null);
    }

    public string GetLogFilePath() => _logFilePath;
}
