using System.Diagnostics;

namespace VPMonitor.Services;

public class StressTestService : IDisposable
{
    private Thread? _cpuStressThread;
    private volatile bool _cpuStressRunning = false;
    private int _stressLevel = 100;
    private readonly List<byte[]> _allocatedMemory = new();
    private readonly object _lockObject = new();
    private readonly LogService _logService;

    public bool IsCpuStressRunning => _cpuStressRunning;
    public int StressLevel => _stressLevel;
    public long AllocatedMemoryMB { get; private set; }

    public StressTestService(LogService logService)
    {
        _logService = logService;
    }

    public void StartCpuStress(int level = 100)
    {
        if (_cpuStressRunning) return;

        _stressLevel = Math.Clamp(level, 10, 100);
        _cpuStressRunning = true;
        _cpuStressThread = new Thread(CpuStressLoop)
        {
            Priority = ThreadPriority.Highest,
            IsBackground = true
        };
        _cpuStressThread.Start();
        _logService.LogInfo($"CPU stress test started with level {_stressLevel}%", Process.GetCurrentProcess().Id);
    }

    public void StopCpuStress()
    {
        _cpuStressRunning = false;
        _cpuStressThread?.Join(1000);
        _cpuStressThread = null;
        _logService.LogInfo("CPU stress test stopped", Process.GetCurrentProcess().Id);
    }

    private void CpuStressLoop()
    {
        var cpuCount = Environment.ProcessorCount;
        var threads = new Thread[cpuCount];

        for (int i = 0; i < cpuCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                var random = new Random();
                while (_cpuStressRunning)
                {
                    var workTime = (int)(_stressLevel * 10);
                    var sleepTime = 1000 - workTime;

                    var endTime = DateTime.Now.AddMilliseconds(workTime);
                    while (DateTime.Now < endTime && _cpuStressRunning)
                    {
                        var a = random.NextDouble();
                        var b = random.NextDouble();
                        var c = Math.Sqrt(a * a + b * b);
                    }

                    if (sleepTime > 0 && _cpuStressRunning)
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

    public void AllocateMemory(int sizeMB)
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
                AllocatedMemoryMB += sizeMB;
                _logService.LogInfo($"Allocated {sizeMB}MB memory for stress test",
                    Process.GetCurrentProcess().Id, details: $"Total allocated: {AllocatedMemoryMB}MB");
            }
        }
        catch (OutOfMemoryException)
        {
            _logService.LogWarning("Out of memory during memory allocation", Process.GetCurrentProcess().Id);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to allocate memory: {ex.Message}", Process.GetCurrentProcess().Id);
        }
    }

    public void FreeMemory()
    {
        lock (_lockObject)
        {
            var freed = AllocatedMemoryMB;
            _allocatedMemory.Clear();
            AllocatedMemoryMB = 0;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _logService.LogInfo($"Freed {freed}MB memory from stress test", Process.GetCurrentProcess().Id);
        }
    }

    public void CreateThreads(int count)
    {
        if (count <= 0) return;

        for (int i = 0; i < count; i++)
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(1000);
                }
            })
            {
                IsBackground = true
            };
            thread.Start();
        }
        _logService.LogInfo($"Created {count} additional threads for stress test", Process.GetCurrentProcess().Id);
    }

    public void StopAll()
    {
        StopCpuStress();
        FreeMemory();
    }

    public void Dispose()
    {
        StopAll();
    }
}
