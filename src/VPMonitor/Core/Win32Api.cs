using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VPMonitor.Core;

[Flags]
public enum ProcessAccessFlags : uint
{
    Terminate = 0x00000001,
    CreateThread = 0x00000002,
    VirtualMemoryOperation = 0x00000008,
    VirtualMemoryRead = 0x00000010,
    VirtualMemoryWrite = 0x00000020,
    DuplicateHandle = 0x00000040,
    CreateProcess = 0x00000080,
    SetQuota = 0x00000100,
    SetInformation = 0x00000200,
    QueryInformation = 0x00000400,
    QueryLimitedInformation = 0x00001000,
    Synchronize = 0x00100000,
    AllAccess = 0x001F0FFF
}

[Flags]
public enum ThreadAccessFlags : uint
{
    Terminate = 0x00000001,
    SuspendResume = 0x00000002,
    GetContext = 0x00000008,
    SetContext = 0x00000010,
    SetInformation = 0x00000020,
    QueryInformation = 0x00000040,
    SetThreadToken = 0x00000080,
    Impersonate = 0x00000100,
    DirectImpersonation = 0x00000200,
    AllAccess = 0x001F03FF
}

public static class Win32Api
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(ProcessAccessFlags processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenThread(ThreadAccessFlags dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    public const uint TH32CS_SNAPPROCESS = 0x00000002;
    public const uint TH32CS_SNAPTHREAD = 0x00000004;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
public struct PROCESSENTRY32
{
    public uint dwSize;
    public uint cntUsage;
    public uint th32ProcessID;
    public IntPtr th32DefaultHeapID;
    public uint th32ModuleID;
    public uint cntThreads;
    public uint th32ParentProcessID;
    public int pcPriClassBase;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szExeFile;
}

[StructLayout(LayoutKind.Sequential)]
public struct THREADENTRY32
{
    public uint dwSize;
    public uint cntUsage;
    public uint th32ThreadID;
    public uint th32OwnerProcessID;
    public int tpBasePri;
    public int tpDeltaPri;
    public uint dwFlags;
}
