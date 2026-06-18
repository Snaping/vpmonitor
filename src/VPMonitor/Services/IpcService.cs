using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using VPMonitor.Models;

namespace VPMonitor.Services;

public class IpcService : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;
    private readonly LogService _logService;
    private bool _isConnected;

    public event EventHandler<IpcMessage>? MessageReceived;
    public event EventHandler? ClientConnected;
    public event EventHandler? ClientDisconnected;

    public bool IsConnected => _isConnected;

    public IpcService(string pipeName, LogService logService)
    {
        _pipeName = pipeName;
        _logService = logService;
    }

    public void StartServer()
    {
        if (_disposed) return;

        _cancellationTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => WaitForConnectionAsync(_cancellationTokenSource.Token));
    }

    private async Task WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _pipeServer = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                _logService.LogInfo($"IPC server waiting for connection on pipe: {_pipeName}");

                await _pipeServer.WaitForConnectionAsync(cancellationToken);
                _isConnected = true;
                _logService.LogInfo("IPC client connected");
                ClientConnected?.Invoke(this, EventArgs.Empty);

                await HandleCommunicationAsync(_pipeServer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
            }
            catch (Exception ex)
            {
                _logService.LogError($"IPC server error: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                if (_pipeServer != null)
                {
                    if (_pipeServer.IsConnected)
                    {
                        try { _pipeServer.Disconnect(); } catch { }
                    }
                    _pipeServer.Dispose();
                    _pipeServer = null;
                }
                ClientDisconnected?.Invoke(this, EventArgs.Empty);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task HandleCommunicationAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var messageBuilder = new StringBuilder();

        while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var read = await pipe.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read == 0) break;

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, read));

                if (pipe.IsMessageComplete)
                {
                    var messageJson = messageBuilder.ToString();
                    messageBuilder.Clear();

                    try
                    {
                        var message = JsonConvert.DeserializeObject<IpcMessage>(messageJson);
                        if (message != null)
                        {
                            _logService.LogInfo($"Received IPC message: {message.Command}");
                            MessageReceived?.Invoke(this, message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError($"Failed to deserialize IPC message: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logService.LogError($"IPC read error: {ex.Message}");
                break;
            }
        }
    }

    public async Task<bool> SendMessageAsync(IpcMessage message)
    {
        if (_pipeServer == null || !_pipeServer.IsConnected || _disposed)
        {
            _logService.LogWarning("Cannot send IPC message: not connected");
            return false;
        }

        try
        {
            var json = JsonConvert.SerializeObject(message);
            var data = Encoding.UTF8.GetBytes(json);
            await _pipeServer.WriteAsync(data, 0, data.Length);
            await _pipeServer.FlushAsync();
            _logService.LogInfo($"Sent IPC message: {message.Command}");
            return true;
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to send IPC message: {ex.Message}");
            return false;
        }
    }

    public void StopServer()
    {
        _cancellationTokenSource?.Cancel();
        try
        {
            if (_pipeServer != null && _pipeServer.IsConnected)
            {
                _pipeServer.Disconnect();
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopServer();
        _cancellationTokenSource?.Dispose();
        _pipeServer?.Dispose();
    }
}

public class IpcClient : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipeClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;
    private bool _isConnected;

    public event EventHandler<IpcMessage>? MessageReceived;

    public bool IsConnected => _isConnected;

    public IpcClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<bool> ConnectAsync(int timeoutMs = 5000)
    {
        if (_disposed) return false;

        try
        {
            _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipeClient.ConnectAsync(timeoutMs);
            _isConnected = true;

            _cancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveMessagesAsync(_cancellationTokenSource.Token));

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to IPC server: {ex.Message}");
            return false;
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        if (_pipeClient == null) return;

        var buffer = new byte[4096];
        var messageBuilder = new StringBuilder();

        while (_pipeClient.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var read = await _pipeClient.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read == 0) break;

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, read));

                if (_pipeClient.IsMessageComplete)
                {
                    var messageJson = messageBuilder.ToString();
                    messageBuilder.Clear();

                    try
                    {
                        var message = JsonConvert.DeserializeObject<IpcMessage>(messageJson);
                        if (message != null)
                        {
                            MessageReceived?.Invoke(this, message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to deserialize IPC message: {ex.Message}");
                    }
                }
            }
            catch
            {
                break;
            }
        }

        _isConnected = false;
    }

    public async Task<bool> SendMessageAsync(IpcMessage message)
    {
        if (_pipeClient == null || !_pipeClient.IsConnected || _disposed) return false;

        try
        {
            var json = JsonConvert.SerializeObject(message);
            var data = Encoding.UTF8.GetBytes(json);
            await _pipeClient.WriteAsync(data, 0, data.Length);
            await _pipeClient.FlushAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Disconnect()
    {
        _cancellationTokenSource?.Cancel();
        try
        {
            _pipeClient?.Close();
        }
        catch
        {
        }
        _isConnected = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Disconnect();
        _cancellationTokenSource?.Dispose();
        _pipeClient?.Dispose();
    }
}
