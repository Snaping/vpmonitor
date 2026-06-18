using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VPMonitor.Services;

public static class ProcessHelper
{
    [Flags]
    private enum ThreadAccess : int
    {
        SUSPEND_RESUME = 0x0002,
        QUERY_INFORMATION = 0x0040,
        QUERY_LIMITED_INFORMATION = 0x1000
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(IntPtr hProcess);

    private const uint NORMAL_PRIORITY_CLASS = 0x0020;
    private const uint IDLE_PRIORITY_CLASS = 0x0040;
    private const uint HIGH_PRIORITY_CLASS = 0x0080;
    private const uint REALTIME_PRIORITY_CLASS = 0x0100;
    private const uint BELOW_NORMAL_PRIORITY_CLASS = 0x4000;
    private const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x8000;

    public static void SuspendProcess(Process process)
    {
        if (process == null || process.HasExited)
            throw new ArgumentException("Process is not valid");

        foreach (ProcessThread thread in process.Threads)
        {
            IntPtr hThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);
            if (hThread == IntPtr.Zero)
                continue;

            try
            {
                SuspendThread(hThread);
            }
            finally
            {
                CloseHandle(hThread);
            }
        }
    }

    public static void ResumeProcess(Process process)
    {
        if (process == null || process.HasExited)
            throw new ArgumentException("Process is not valid");

        foreach (ProcessThread thread in process.Threads)
        {
            IntPtr hThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);
            if (hThread == IntPtr.Zero)
                continue;

            try
            {
                uint suspendCount;
                do
                {
                    suspendCount = ResumeThread(hThread);
                } while (suspendCount > 0);
            }
            finally
            {
                CloseHandle(hThread);
            }
        }
    }

    public static bool IsProcessSuspended(Process process)
    {
        if (process == null || process.HasExited)
            return false;

        foreach (ProcessThread thread in process.Threads)
        {
            if (thread.ThreadState == System.Diagnostics.ThreadState.Wait && thread.WaitReason == ThreadWaitReason.Suspended)
            {
                return true;
            }
        }
        return false;
    }

    public static void LowerProcessPriority(Process process)
    {
        if (process == null || process.HasExited)
            throw new ArgumentException("Process is not valid");

        uint currentPriority = GetPriorityClass(process.Handle);

        uint newPriority = currentPriority switch
        {
            REALTIME_PRIORITY_CLASS => HIGH_PRIORITY_CLASS,
            HIGH_PRIORITY_CLASS => ABOVE_NORMAL_PRIORITY_CLASS,
            ABOVE_NORMAL_PRIORITY_CLASS => NORMAL_PRIORITY_CLASS,
            NORMAL_PRIORITY_CLASS => BELOW_NORMAL_PRIORITY_CLASS,
            BELOW_NORMAL_PRIORITY_CLASS => IDLE_PRIORITY_CLASS,
            _ => IDLE_PRIORITY_CLASS
        };

        SetPriorityClass(process.Handle, newPriority);
        process.PriorityClass = MapPriorityClass(newPriority);
    }

    public static void SetProcessPriority(Process process, ProcessPriorityClass priorityClass)
    {
        if (process == null || process.HasExited)
            throw new ArgumentException("Process is not valid");

        process.PriorityClass = priorityClass;
    }

    private static ProcessPriorityClass MapPriorityClass(uint priorityClass)
    {
        return priorityClass switch
        {
            IDLE_PRIORITY_CLASS => ProcessPriorityClass.Idle,
            BELOW_NORMAL_PRIORITY_CLASS => ProcessPriorityClass.BelowNormal,
            NORMAL_PRIORITY_CLASS => ProcessPriorityClass.Normal,
            ABOVE_NORMAL_PRIORITY_CLASS => ProcessPriorityClass.AboveNormal,
            HIGH_PRIORITY_CLASS => ProcessPriorityClass.High,
            REALTIME_PRIORITY_CLASS => ProcessPriorityClass.RealTime,
            _ => ProcessPriorityClass.Normal
        };
    }

    public static void TerminateProcess(Process process)
    {
        if (process == null || process.HasExited)
            return;

        process.Kill();
    }
}
