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
    private int unixGroup;
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
        // Capture while the anchor is retained, before the host-start gate permits descendants.
        // Only the host signals its own group; this identifier is used for observation, never kill.
        if (job is null) unixGroup = anchor.Id;
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
    public async Task WaitForExitAsync(Process anchor)
    {
        var elapsed = Stopwatch.StartNew();
        await anchor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        // Native scope termination is asynchronous; the anchor can exit before its descendants.
        while (true)
        {
            if (job is null)
            {
                if (!await HasLiveUnixMembersAsync(TimeSpan.FromSeconds(5) - elapsed.Elapsed).ConfigureAwait(false)) return;
            }
            else
            {
                if (!QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<JobAccounting>(), IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (accounting.ActiveProcesses == 0) return;
            }
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(5))
                throw new TimeoutException("Authoring operation processes did not exit.");
            await Task.Delay(20).ConfigureAwait(false);
        }
    }
    private async Task<bool> HasLiveUnixMembersAsync(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) throw new TimeoutException("Authoring operation processes did not exit.");
        if (OperatingSystem.IsLinux())
        {
            var observing = Stopwatch.StartNew();
            foreach (var entry in Directory.EnumerateDirectories("/proc"))
            {
                if (observing.Elapsed >= remaining) throw new TimeoutException("Authoring operation processes did not exit.");
                if (!int.TryParse(Path.GetFileName(entry), out _)) continue;
                string stat;
                try { stat = File.ReadAllText(Path.Combine(entry, "stat")); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                catch (IOException) when (!Directory.Exists(entry)) { continue; }
                // comm can contain spaces and ')'. Fields after the final ')' begin at state.
                var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 4) throw new InvalidDataException("Invalid Unix process status.");
                if (int.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture) == unixGroup
                    && int.Parse(fields[3], System.Globalization.CultureInfo.InvariantCulture) == unixGroup
                    && fields[0] is not ("Z" or "X" or "x")) return true;
            }
            return false;
        }
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Unix authoring process observation requires Linux or macOS.");
        // macOS has no /proc. Read the native process table; never signal a captured/recycled PID.
        using var status = new Process
        {
            StartInfo = new ProcessStartInfo("/bin/ps")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "-axo", "pgid=,stat=" }
            }
        };
        status.Start();
        var output = status.StandardOutput.ReadToEndAsync();
        var error = status.StandardError.ReadToEndAsync();
        try { await status.WaitForExitAsync().WaitAsync(remaining).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            status.Kill();
            await status.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        if (status.ExitCode != 0) throw new IOException("Could not observe Unix operation processes: " + await error.ConfigureAwait(false));
        foreach (var row in (await output.ConfigureAwait(false)).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = row.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2) throw new InvalidDataException("Invalid Unix process status.");
            if (int.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture) == unixGroup
                && fields[1][0] is not ('Z' or 'X')) return true;
        }
        return false;
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
    [StructLayout(LayoutKind.Sequential)]
    private struct JobAccounting
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodUserTime, ThisPeriodKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
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
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out JobAccounting information, uint length, IntPtr returnLength);
}
