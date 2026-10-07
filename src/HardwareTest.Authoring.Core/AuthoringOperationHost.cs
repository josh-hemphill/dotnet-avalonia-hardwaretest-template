using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace HardwareTest.Authoring;

/// Keeps the ownership anchor alive after an operation root exits, until its owner reaps the scope.
public static class AuthoringOperationHost
{
    public const string Switch = "--authoring-operation-host";
    public static int Run(string requestPath)
    {
        if (!OperatingSystem.IsWindows()) AuthoringProcessOwnership.CreateUnixSession();
        try { return RunOwned(requestPath); }
        finally
        {
            // Issued by the still-living group member itself, never by a recycled PID.
            if (!OperatingSystem.IsWindows()) AuthoringProcessOwnership.TerminateUnixSession();
        }
    }

    private static int RunOwned(string requestPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(requestPath))!;
        var readiness = Path.Combine(directory, "host-ready");
        File.WriteAllText(readiness + ".tmp", Environment.ProcessId.ToString());
        File.Move(readiness + ".tmp", readiness);
        // The parent assigns the Windows job or observes the Unix session before releasing this gate.
        // EOF also drains the scope when the owning GUI process disappears without normal disposal.
        var stopping = Task.WhenAny(WaitFor("host-stop"), Task.Run(() => Console.In.ReadToEnd()));
        var starting = WaitFor("host-start");
        if (Task.WhenAny(starting, stopping).GetAwaiter().GetResult() == stopping)
            return 1;
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = directory };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        start.ArgumentList.Add(AuthoringOperationChild.Switch);
        start.ArgumentList.Add(requestPath);
        using var child = Process.Start(start) ?? throw new IOException("Could not start the operation child.");
        var exited = child.WaitForExitAsync();
        if (Task.WhenAny(exited, stopping).GetAwaiter().GetResult() == exited)
        {
            exited.GetAwaiter().GetResult();
            var path = Path.Combine(directory, "host-exit.json");
            File.WriteAllBytes(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(new AuthoringChildExit(child.ExitCode),
                AuthoringOperationJsonContext.Default.AuthoringChildExit));
            File.Move(path + ".tmp", path);
            stopping.GetAwaiter().GetResult();
        }
        return 1; // Windows is terminated by the retained parent-owned Job handle.

        async Task WaitFor(string name)
        {
            var path = Path.Combine(directory, name);
            while (!File.Exists(path)) await Task.Delay(20).ConfigureAwait(false);
        }
    }
}

internal sealed record AuthoringChildExit(int ExitCode);
