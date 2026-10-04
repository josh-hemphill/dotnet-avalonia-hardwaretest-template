using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HardwareTest.Authoring;

internal sealed class AuthoringProcessOwnership : IDisposable
{
    private readonly string directory;
    private readonly SafeFileHandle? job;
    private readonly object terminationGate = new();
    private bool terminationRequested;
    public AuthoringProcessOwnership(string directory)
    {
        this.directory = directory;
        if (!OperatingSystem.IsWindows()) return;
        job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var information = new JobLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job, 9, ref information, (uint)Marshal.SizeOf<JobLimits>()))
        {
            var error = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw new Win32Exception(error);
        }
    }

    public void Attach(Process anchor)
    {
        if (job is not null && !AssignProcessToJobObject(job, anchor.SafeHandle))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    public void Terminate()
    {
        lock (terminationGate)
        {
            if (terminationRequested) return;
            if (job is not null)
            {
                if (!TerminateJobObject(job, 1)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            else File.WriteAllText(Path.Combine(directory, "host-stop"), "");
            terminationRequested = true;
        }
    }
    public void Dispose() { lock (terminationGate) job?.Dispose(); }
    internal static void CreateUnixSession()
    {
        if (setsid() < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    internal static void TerminateUnixSession()
    {
        if (kill(0, 9) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("libc", SetLastError = true)]
    private static extern int setsid();
    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref JobLimits information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint code);
}
