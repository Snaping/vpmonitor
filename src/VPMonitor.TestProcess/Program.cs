using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace VPMonitor.TestProcess;

class Program
{
    private static string _pipeName = "VPMonitor_TestProcess";
    private static volatile bool _cpuStressRunning;
    private static volatile bool _memoryStressRunning;
    private static volatile bool _diskActivityRunning;
    private static Thread? _cpuThread;
    private static Thread? _memoryThread;
    private static Thread? _diskThread;
    private static List<byte[]>? _allocatedMemory;
    private static int _cpuLoadLevel = 70;
    private static int _memorySizeMb = 200;
    private static readonly List<Process> _childProcesses = new();
    private static int _currentProcessId;
    private static readonly ManualResetEvent _exitEvent = new(false);

    static async Task Main(string[] args)
    {
        _currentProcessId = Process.GetCurrentProcess().Id;

        if (args.Length > 0)
        {
            _pipeName = args[0];
        }

        Console.Title = $"VPTestProcess (PID: {_currentProcessId})";
        Console.WriteLine($"[VPTestProcess] Started, PID: {_currentProcessId}");
        Console.WriteLine($"[VPTestProcess] Pipe: {_pipeName}");
        Console.WriteLine($"[VPTestProcess] Waiting for commands...");
        Console.WriteLine();

        var serverTask = RunIpcServer();

        await Task.WhenAny(serverTask, Task.Run(() => _exitEvent.WaitOne()));

        StopAllStress();
        foreach (var child in _childProcesses)
        {
            try { if (!child.HasExited) child.Kill(); }
            catch { }
        }

        Console.WriteLine("[VPTestProcess] Exited");
    }

    private static async Task RunIpcServer()
    {
        while (!_exitEvent.WaitOne(0))
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                Console.WriteLine("[IPC] Waiting for connection...");
                await pipeServer.WaitForConnectionAsync();
                Console.WriteLine("[IPC] Client connected");

                using var reader = new StreamReader(pipeServer, Encoding.UTF8);
                using var writer = new StreamWriter(pipeServer, Encoding.UTF8) { AutoFlush = true };

                while (pipeServer.IsConnected && !_exitEvent.WaitOne(0))
                {
                    try
                    {
                        var messageJson = await reader.ReadLineAsync();
                        if (messageJson == null) break;

                        var message = JsonConvert.DeserializeObject<IpcMessage>(messageJson);
                        if (message == null) continue;

                        var response = HandleCommand(message);
                        response.RequestId = message.RequestId;

                        var responseJson = JsonConvert.SerializeObject(response);
                        await writer.WriteLineAsync(responseJson);

                        if (message.Command == IpcCommands.Exit)
                        {
                            _exitEvent.Set();
                            break;
                        }
                    }
                    catch (IOException)
                    {
                        Console.WriteLine("[IPC] Client disconnected");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[IPC] Error: {ex.Message}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IPC] Server error: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    private static IpcResponse HandleCommand(IpcMessage message)
    {
        Console.WriteLine($"[CMD] Received: {message.Command}");

        switch (message.Command)
        {
            case IpcCommands.Ping:
                return new IpcResponse { Success = true, Message = "PONG" };

            case IpcCommands.GetStatus:
                return GetStatusResponse();

            case IpcCommands.StartHighCpu:
                if (message.Parameters.TryGetValue("LoadLevel", out var loadStr)
                    && int.TryParse(loadStr, out var load))
                {
                    _cpuLoadLevel = Math.Clamp(load, 10, 100);
                }
                StartCpuStress();
                return new IpcResponse { Success = true, Message = "CPU stress started" };

            case IpcCommands.StartHighMemory:
                if (message.Parameters.TryGetValue("SizeMb", out var sizeStr)
                    && int.TryParse(sizeStr, out var size))
                {
                    _memorySizeMb = Math.Clamp(size, 10, 2048);
                }
                StartMemoryStress();
                return new IpcResponse { Success = true, Message = "Memory stress started" };

            case IpcCommands.StartDiskActivity:
                StartDiskActivity();
                return new IpcResponse { Success = true, Message = "Disk activity started" };

            case IpcCommands.StopAll:
                StopAllStress();
                return new IpcResponse { Success = true, Message = "All stress stopped" };

            case IpcCommands.CreateChildProcess:
                CreateChildProcess();
                return new IpcResponse
                {
                    Success = true,
                    Message = "Child process created",
                    Data = new Dictionary<string, string>
                    {
                        ["ChildCount"] = _childProcesses.Count.ToString()
                    }
                };

            case IpcCommands.Crash:
                Console.WriteLine("[CRASH] Crashing on purpose...");
                Task.Run(() => throw new InvalidOperationException("Test crash"));
                return new IpcResponse { Success = true, Message = "Crash initiated" };

            case IpcCommands.Hang:
                Console.WriteLine("[HANG] Hanging on purpose...");
                Thread.Sleep(Timeout.Infinite);
                return new IpcResponse { Success = true, Message = "Hang initiated" };

            case IpcCommands.Exit:
                Console.WriteLine("[EXIT] Exit requested");
                return new IpcResponse { Success = true, Message = "Exiting" };

            default:
                return new IpcResponse { Success = false, Message = "Unknown command" };
        }
    }

    private static IpcResponse GetStatusResponse()
    {
        var process = Process.GetCurrentProcess();
        _childProcesses.RemoveAll(p => p.HasExited);

        var status = new TestProcessStatus
        {
            IsHighCpuRunning = _cpuStressRunning,
            IsHighMemoryRunning = _memoryStressRunning,
            IsDiskActivityRunning = _diskActivityRunning,
            CpuLoadLevel = _cpuStressRunning ? _cpuLoadLevel : 0,
            MemoryAllocatedBytes = _allocatedMemory?.Count * 10 * 1024 * 1024L ?? 0,
            ChildProcessCount = _childProcesses.Count,
            ThreadCount = process.Threads.Count
        };

        return new IpcResponse
        {
            Success = true,
            Message = "OK",
            Data = new Dictionary<string, string>
            {
                ["IsHighCpuRunning"] = status.IsHighCpuRunning.ToString(),
                ["IsHighMemoryRunning"] = status.IsHighMemoryRunning.ToString(),
                ["IsDiskActivityRunning"] = status.IsDiskActivityRunning.ToString(),
                ["CpuLoadLevel"] = status.CpuLoadLevel.ToString(),
                ["MemoryAllocatedBytes"] = status.MemoryAllocatedBytes.ToString(),
                ["ChildProcessCount"] = status.ChildProcessCount.ToString(),
                ["ThreadCount"] = status.ThreadCount.ToString(),
                ["HandleCount"] = process.HandleCount.ToString(),
                ["WorkingSetMb"] = (process.WorkingSet64 / (1024 * 1024)).ToString()
            }
        };
    }

    private static void StartCpuStress()
    {
        if (_cpuStressRunning) return;

        _cpuStressRunning = true;
        _cpuThread = new Thread(CpuStressLoop)
        {
            Priority = ThreadPriority.Normal,
            IsBackground = true
        };
        _cpuThread.Start();
        Console.WriteLine($"[STRESS] CPU stress started (target: {_cpuLoadLevel}%)");
    }

    private static void CpuStressLoop()
    {
        while (_cpuStressRunning)
        {
            var load = _cpuLoadLevel;
            var busyTime = load;
            var idleTime = 100 - load;

            var endTime = DateTime.Now.AddMilliseconds(busyTime);
            while (DateTime.Now < endTime && _cpuStressRunning)
            {
                var a = 0;
                for (var i = 0; i < 10000; i++)
                {
                    a += i;
                }
            }

            if (idleTime > 0)
            {
                Thread.Sleep(idleTime);
            }
        }
    }

    private static void StartMemoryStress()
    {
        if (_memoryStressRunning) return;

        _memoryStressRunning = true;
        _allocatedMemory = new List<byte[]>();
        _memoryThread = new Thread(MemoryStressLoop)
        {
            IsBackground = true
        };
        _memoryThread.Start();
        Console.WriteLine($"[STRESS] Memory stress started (target: {_memorySizeMb}MB)");
    }

    private static void MemoryStressLoop()
    {
        try
        {
            const int blockSize = 10 * 1024 * 1024;
            int totalBlocks = _memorySizeMb / 10;
            int allocatedBlocks = 0;

            while (_memoryStressRunning && allocatedBlocks < totalBlocks)
            {
                try
                {
                    var block = new byte[blockSize];
                    for (int i = 0; i < blockSize; i += 4096)
                    {
                        block[i] = 0xAA;
                    }
                    _allocatedMemory?.Add(block);
                    allocatedBlocks++;
                    Thread.Sleep(50);
                }
                catch (OutOfMemoryException)
                {
                    Console.WriteLine("[STRESS] Out of memory!");
                    break;
                }
            }

            while (_memoryStressRunning)
            {
                if (_allocatedMemory != null)
                {
                    foreach (var block in _allocatedMemory)
                    {
                        for (int i = 0; i < block.Length; i += 4096)
                        {
                            block[i] = (byte)(block[i] + 1);
                        }
                    }
                }
                Thread.Sleep(100);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STRESS] Memory error: {ex.Message}");
        }
    }

    private static void StartDiskActivity()
    {
        if (_diskActivityRunning) return;

        _diskActivityRunning = true;
        _diskThread = new Thread(DiskActivityLoop)
        {
            IsBackground = true
        };
        _diskThread.Start();
        Console.WriteLine("[STRESS] Disk activity started");
    }

    private static void DiskActivityLoop()
    {
        var tempFile = Path.GetTempFileName();
        var buffer = new byte[4096];
        var random = new Random();

        try
        {
            while (_diskActivityRunning)
            {
                random.NextBytes(buffer);
                using (var fs = new FileStream(tempFile, FileMode.Append, FileAccess.Write, FileShare.None, 4096))
                {
                    fs.Write(buffer, 0, buffer.Length);
                }
                Thread.Sleep(10);

                if (File.Exists(tempFile) && new FileInfo(tempFile).Length > 50 * 1024 * 1024)
                {
                    File.Delete(tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STRESS] Disk error: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); }
                catch { }
            }
        }
    }

    private static void CreateChildProcess()
    {
        try
        {
            var childPipeName = $"{_pipeName}_Child_{Guid.NewGuid():N}";
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName
                ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

            var startInfo = new ProcessStartInfo
            {
                FileName = currentExe,
                Arguments = childPipeName,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            var child = Process.Start(startInfo);
            if (child != null)
            {
                _childProcesses.Add(child);
                Console.WriteLine($"[CHILD] Created child process: {child.Id}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CHILD] Error: {ex.Message}");
        }
    }

    private static void StopAllStress()
    {
        _cpuStressRunning = false;
        _memoryStressRunning = false;
        _diskActivityRunning = false;

        if (_cpuThread != null && _cpuThread.IsAlive)
        {
            _cpuThread.Join(2000);
        }
        if (_memoryThread != null && _memoryThread.IsAlive)
        {
            _memoryThread.Join(2000);
        }
        if (_diskThread != null && _diskThread.IsAlive)
        {
            _diskThread.Join(2000);
        }

        _allocatedMemory?.Clear();
        _allocatedMemory = null;
        GC.Collect();

        Console.WriteLine("[STRESS] All stress tests stopped");
    }
}
