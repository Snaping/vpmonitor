using System;
using System.Collections.Generic;
using System.Threading;
using VPMonitor.Core;

namespace VPMonitor.Monitoring;

public class StressTester : IDisposable
{
    private readonly Logger _logger;
    private Thread? _cpuStressThread;
    private Thread? _memoryStressThread;
    private volatile bool _cpuStressRunning;
    private volatile bool _memoryStressRunning;
    private bool _disposed;
    private List<byte[]>? _allocatedMemory;
    private int _cpuLoadLevel = 50;
    private int _memorySizeMb = 200;

    public bool IsCpuStressRunning => _cpuStressRunning;
    public bool IsMemoryStressRunning => _memoryStressRunning;

    public int CpuLoadLevel
    {
        get => _cpuLoadLevel;
        set => _cpuLoadLevel = Math.Clamp(value, 10, 100);
    }

    public int MemorySizeMb
    {
        get => _memorySizeMb;
        set => _memorySizeMb = Math.Clamp(value, 10, 2048);
    }

    public StressTester(Logger logger)
    {
        _logger = logger;
    }

    public void StartCpuStress()
    {
        if (_cpuStressRunning) return;

        _cpuStressRunning = true;
        _cpuStressThread = new Thread(CpuStressLoop)
        {
            Priority = ThreadPriority.BelowNormal,
            IsBackground = true
        };
        _cpuStressThread.Start();
        _logger.LogInfo("CPU stress test started", $"Target load: {_cpuLoadLevel}%");
    }

    public void StopCpuStress()
    {
        _cpuStressRunning = false;
        _logger.LogInfo("CPU stress test stopped");
    }

    public void StartMemoryStress()
    {
        if (_memoryStressRunning) return;

        _memoryStressRunning = true;
        _allocatedMemory = new List<byte[]>();
        _memoryStressThread = new Thread(MemoryStressLoop)
        {
            IsBackground = true
        };
        _memoryStressThread.Start();
        _logger.LogInfo("Memory stress test started", $"Target size: {_memorySizeMb}MB");
    }

    public void StopMemoryStress()
    {
        _memoryStressRunning = false;
        _allocatedMemory?.Clear();
        _allocatedMemory = null;
        GC.Collect();
        _logger.LogInfo("Memory stress test stopped");
    }

    public void StopAll()
    {
        StopCpuStress();
        StopMemoryStress();
    }

    private void CpuStressLoop()
    {
        var load = _cpuLoadLevel;
        var busyTime = load;
        var idleTime = 100 - load;

        while (_cpuStressRunning)
        {
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

            load = _cpuLoadLevel;
            busyTime = load;
            idleTime = 100 - load;
        }
    }

    private void MemoryStressLoop()
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
                    _logger.LogWarning("Memory allocation failed - out of memory");
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
            _logger.LogError("Memory stress test error", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAll();

        if (_cpuStressThread != null && _cpuStressThread.IsAlive)
        {
            _cpuStressThread.Join(2000);
        }
        if (_memoryStressThread != null && _memoryStressThread.IsAlive)
        {
            _memoryStressThread.Join(2000);
        }

        _allocatedMemory?.Clear();
        _allocatedMemory = null;
        GC.Collect();
    }
}
