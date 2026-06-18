using System.Diagnostics;
using Newtonsoft.Json;
using VPMonitor.TestProcess.Models;
using VPMonitor.TestProcess.Services;

namespace VPMonitor.TestProcess;

class Program
{
    private static IpcClient? _ipcClient;
    private static volatile bool _highLoadRunning = false;
    private static Thread? _highLoadThread;
    private static int _loadLevel = 50;
    private static readonly List<byte[]> _allocatedMemory = new();
    private static readonly List<Thread> _workerThreads = new();
    private static readonly object _lockObject = new();
    private static int _processId;
    private static string _pipeName = "VPMonitor_IPC_Pipe";
    private static readonly CancellationTokenSource _cts = new();

    static async Task Main(string[] args)
    {
        _processId = Process.GetCurrentProcess().Id;

        if (args.Length > 0)
        {
            _pipeName = args[0];
        }

        Console.WriteLine($"Test Process started. PID: {_processId}");
        Console.WriteLine($"Waiting for IPC connection on pipe: {_pipeName}");

        _ipcClient = new IpcClient(_pipeName);
        _ipcClient.MessageReceived += OnMessageReceived;

        var connected = false;
        for (int i = 0; i < 5 && !connected; i++)
        {
            connected = await _ipcClient.ConnectAsync(3000);
            if (!connected)
            {
                Console.WriteLine($"Connection attempt {i + 1} failed, retrying...");
                await Task.Delay(1000);
            }
        }

        if (!connected)
        {
            Console.WriteLine("Failed to connect to IPC server. Exiting...");
            return;
        }

        Console.WriteLine("Connected to monitor. Waiting for commands...");

        await SendStatus();

        while (!_cts.IsCancellationRequested && _ipcClient.IsConnected)
        {
            await Task.Delay(1000);
        }

        Cleanup();
        Console.WriteLine("Test Process exiting.");
    }

    private static async void OnMessageReceived(object? sender, IpcMessage e)
    {
        Console.WriteLine($"Received command: {e.Command}");

        try
        {
            switch (e.Command)
            {
                case IpcCommandType.Ping:
                    await SendMessage(new IpcMessage
                    {
                        Command = IpcCommandType.StatusResponse,
                        Payload = "Pong"
                    });
                    break;

                case IpcCommandType.GetStatus:
                    await SendStatus();
                    break;

                case IpcCommandType.StartHighLoad:
                    StartHighLoad();
                    await SendStatus();
                    break;

                case IpcCommandType.StopHighLoad:
                    StopHighLoad();
                    await SendStatus();
                    break;

                case IpcCommandType.SetLoadLevel:
                    if (int.TryParse(e.Payload, out var level))
                    {
                        _loadLevel = Math.Clamp(level, 10, 100);
                    }
                    await SendStatus();
                    break;

                case IpcCommandType.AllocateMemory:
                    if (int.TryParse(e.Payload, out var sizeMB))
                    {
                        AllocateMemory(sizeMB);
                    }
                    await SendStatus();
                    break;

                case IpcCommandType.FreeMemory:
                    FreeMemory();
                    await SendStatus();
                    break;

                case IpcCommandType.CreateThreads:
                    if (int.TryParse(e.Payload, out var threadCount))
                    {
                        CreateWorkerThreads(threadCount);
                    }
                    await SendStatus();
                    break;

                case IpcCommandType.CriticalStateTest:
                    RunCriticalStateTests();
                    await SendStatus();
                    break;

                case IpcCommandType.ExitProcess:
                    await SendMessage(new IpcMessage
                    {
                        Command = IpcCommandType.StatusResponse,
                        Payload = "Exiting"
                    });
                    _cts.Cancel();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing command: {ex.Message}");
            await SendMessage(new IpcMessage
            {
                Command = IpcCommandType.Error,
                Payload = ex.Message
            });
        }
    }

    private static void StartHighLoad()
    {
        if (_highLoadRunning) return;

        _highLoadRunning = true;
        _highLoadThread = new Thread(HighLoadLoop)
        {
            Priority = ThreadPriority.Highest,
            IsBackground = true
        };
        _highLoadThread.Start();
        Console.WriteLine("High load started");
    }

    private static void StopHighLoad()
    {
        _highLoadRunning = false;
        _highLoadThread?.Join(1000);
        _highLoadThread = null;
        Console.WriteLine("High load stopped");
    }

    private static void HighLoadLoop()
    {
        var cpuCount = Environment.ProcessorCount;
        var threads = new Thread[cpuCount];

        for (int i = 0; i < cpuCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                var random = new Random();
                while (_highLoadRunning)
                {
                    var workTime = (int)(_loadLevel * 10);
                    var sleepTime = 1000 - workTime;

                    var endTime = DateTime.Now.AddMilliseconds(workTime);
                    while (DateTime.Now < endTime && _highLoadRunning)
                    {
                        var a = random.NextDouble();
                        var b = random.NextDouble();
                        var c = Math.Sqrt(a * a + b * b);
                    }

                    if (sleepTime > 0 && _highLoadRunning)
                    {
                        Thread.Sleep(sleepTime);
                    }
                }
            })
            {
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }
    }

    private static void AllocateMemory(int sizeMB)
    {
        if (sizeMB <= 0) return;

        try
        {
            lock (_lockObject)
            {
                var bytes = new byte[sizeMB * 1024 * 1024];
                for (int i = 0; i < bytes.Length; i += 4096)
                {
                    bytes[i] = 0xAA;
                }
                _allocatedMemory.Add(bytes);
                Console.WriteLine($"Allocated {sizeMB}MB memory");
            }
        }
        catch (OutOfMemoryException)
        {
            Console.WriteLine("Out of memory!");
        }
    }

    private static void FreeMemory()
    {
        lock (_lockObject)
        {
            _allocatedMemory.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Console.WriteLine("Memory freed");
        }
    }

    private static void CreateWorkerThreads(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var thread = new Thread(() =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    Thread.Sleep(1000);
                }
            })
            {
                IsBackground = true
            };
            thread.Start();
            _workerThreads.Add(thread);
        }
        Console.WriteLine($"Created {count} worker threads");
    }

    private static void RunCriticalStateTests()
    {
        Console.WriteLine("Running critical state tests...");

        var tests = new List<string>();

        try
        {
            tests.Add("Test 1: High thread count creation");
            CreateWorkerThreads(50);
        }
        catch (Exception ex)
        {
            tests.Add($"Test 1 Failed: {ex.Message}");
        }

        try
        {
            tests.Add("Test 2: Large memory allocation attempt");
            try
            {
                AllocateMemory(512);
                tests.Add("Test 2 Passed: Allocated 512MB");
            }
            catch (OutOfMemoryException)
            {
                tests.Add("Test 2 Passed: Expected OutOfMemoryException (boundary reached)");
            }
        }
        catch (Exception ex)
        {
            tests.Add($"Test 2 Failed: {ex.Message}");
        }

        try
        {
            tests.Add("Test 3: CPU stress spike");
            StartHighLoad();
            Thread.Sleep(3000);
            StopHighLoad();
            tests.Add("Test 3 Passed: CPU stress test completed");
        }
        catch (Exception ex)
        {
            tests.Add($"Test 3 Failed: {ex.Message}");
        }

        try
        {
            tests.Add("Test 4: Rapid memory allocate/free cycles");
            for (int i = 0; i < 10; i++)
            {
                AllocateMemory(10);
                Thread.Sleep(100);
                FreeMemory();
                Thread.Sleep(100);
            }
            tests.Add("Test 4 Passed: Rapid memory cycles completed");
        }
        catch (Exception ex)
        {
            tests.Add($"Test 4 Failed: {ex.Message}");
        }

        Console.WriteLine("Critical state tests completed");
        foreach (var test in tests)
        {
            Console.WriteLine($"  {test}");
        }
    }

    private static async Task SendStatus()
    {
        var status = new TestProcessStatus
        {
            ProcessId = _processId,
            MemoryUsedMB = GetAllocatedMemoryMB(),
            ThreadCount = Process.GetCurrentProcess().Threads.Count,
            IsHighLoadRunning = _highLoadRunning,
            LoadLevel = _loadLevel,
            Status = _cts.IsCancellationRequested ? "Exiting" : "Running"
        };

        await SendMessage(new IpcMessage
        {
            Command = IpcCommandType.StatusResponse,
            Payload = JsonConvert.SerializeObject(status)
        });
    }

    private static double GetAllocatedMemoryMB()
    {
        lock (_lockObject)
        {
            return _allocatedMemory.Sum(a => a.Length) / (1024.0 * 1024.0);
        }
    }

    private static async Task SendMessage(IpcMessage message)
    {
        if (_ipcClient != null)
        {
            await _ipcClient.SendMessageAsync(message);
        }
    }

    private static void Cleanup()
    {
        try
        {
            StopHighLoad();
            FreeMemory();
            _ipcClient?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cleanup error: {ex.Message}");
        }
    }
}
