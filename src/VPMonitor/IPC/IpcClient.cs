using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VPMonitor.Core;
using VPMonitor.Models;

namespace VPMonitor.IPC;

public class IpcClient : IDisposable
{
    private readonly Logger _logger;
    private NamedPipeClientStream? _pipeClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _disposed;
    private readonly string _pipeName;

    public bool IsConnected => _pipeClient?.IsConnected ?? false;

    public IpcClient(string pipeName, Logger logger)
    {
        _pipeName = pipeName;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(int timeoutMs = 5000)
    {
        try
        {
            _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipeClient.ConnectAsync(timeoutMs);

            _reader = new StreamReader(_pipeClient, Encoding.UTF8);
            _writer = new StreamWriter(_pipeClient, Encoding.UTF8) { AutoFlush = true };

            _logger.LogInfo("IPC client connected", $"Pipe: {_pipeName}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to connect to IPC server", ex.Message);
            Dispose();
            return false;
        }
    }

    public async Task<IpcResponse?> SendCommandAsync(string command, Dictionary<string, string>? parameters = null, int timeoutMs = 5000)
    {
        if (!IsConnected || _writer == null || _reader == null)
        {
            _logger.LogWarning("IPC client not connected");
            return null;
        }

        try
        {
            var message = new IpcMessage
            {
                Command = command,
                Parameters = parameters ?? new Dictionary<string, string>()
            };

            var json = JsonConvert.SerializeObject(message);
            await _writer.WriteLineAsync(json);

            using var cts = new CancellationTokenSource(timeoutMs);
            var responseJson = await _reader.ReadLineAsync(cts.Token);

            if (responseJson == null)
            {
                _logger.LogWarning("No response from IPC server");
                return null;
            }

            var response = JsonConvert.DeserializeObject<IpcResponse>(responseJson);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error sending IPC command", ex.Message);
            return null;
        }
    }

    public async Task<bool> PingAsync()
    {
        var response = await SendCommandAsync(IpcCommands.Ping);
        return response?.Success ?? false;
    }

    public async Task<TestProcessStatus?> GetStatusAsync()
    {
        var response = await SendCommandAsync(IpcCommands.GetStatus);
        if (response?.Success == true && response.Data != null)
        {
            return new TestProcessStatus
            {
                IsHighCpuRunning = bool.Parse(response.Data.GetValueOrDefault("IsHighCpuRunning", "false")),
                IsHighMemoryRunning = bool.Parse(response.Data.GetValueOrDefault("IsHighMemoryRunning", "false")),
                IsDiskActivityRunning = bool.Parse(response.Data.GetValueOrDefault("IsDiskActivityRunning", "false")),
                CpuLoadLevel = int.Parse(response.Data.GetValueOrDefault("CpuLoadLevel", "0")),
                MemoryAllocatedBytes = long.Parse(response.Data.GetValueOrDefault("MemoryAllocatedBytes", "0")),
                ChildProcessCount = int.Parse(response.Data.GetValueOrDefault("ChildProcessCount", "0")),
                ThreadCount = int.Parse(response.Data.GetValueOrDefault("ThreadCount", "0"))
            };
        }
        return null;
    }

    public async Task<bool> StartHighCpuAsync(int loadLevel = 70)
    {
        var parameters = new Dictionary<string, string>
        {
            ["LoadLevel"] = loadLevel.ToString()
        };
        var response = await SendCommandAsync(IpcCommands.StartHighCpu, parameters);
        return response?.Success ?? false;
    }

    public async Task<bool> StartHighMemoryAsync(int sizeMb = 200)
    {
        var parameters = new Dictionary<string, string>
        {
            ["SizeMb"] = sizeMb.ToString()
        };
        var response = await SendCommandAsync(IpcCommands.StartHighMemory, parameters);
        return response?.Success ?? false;
    }

    public async Task<bool> StartDiskActivityAsync()
    {
        var response = await SendCommandAsync(IpcCommands.StartDiskActivity);
        return response?.Success ?? false;
    }

    public async Task<bool> StopAllAsync()
    {
        var response = await SendCommandAsync(IpcCommands.StopAll);
        return response?.Success ?? false;
    }

    public async Task<bool> CreateChildProcessAsync()
    {
        var response = await SendCommandAsync(IpcCommands.CreateChildProcess);
        return response?.Success ?? false;
    }

    public async Task<bool> CrashProcessAsync()
    {
        var response = await SendCommandAsync(IpcCommands.Crash);
        return response?.Success ?? false;
    }

    public async Task<bool> HangProcessAsync()
    {
        var response = await SendCommandAsync(IpcCommands.Hang);
        return response?.Success ?? false;
    }

    public async Task<bool> ExitProcessAsync()
    {
        var response = await SendCommandAsync(IpcCommands.Exit);
        Dispose();
        return response?.Success ?? false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _reader?.Dispose();
        _writer?.Dispose();
        _pipeClient?.Dispose();
        _reader = null;
        _writer = null;
        _pipeClient = null;
    }
}
