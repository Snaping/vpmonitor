using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using VPMonitor.TestProcess.Models;

namespace VPMonitor.TestProcess.Services;

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
