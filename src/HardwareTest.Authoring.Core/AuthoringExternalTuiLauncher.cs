using System.Diagnostics;

namespace HardwareTest.Authoring;

/// Starts the installed interactive CLI as a real external process. It never executes a test plan.
public sealed class AuthoringExternalTuiLauncher
{
    internal Action<Process>? TerminalStarted { get; init; }
    public static string? Prerequisite(string home, string? plan, bool requiresInstrumentLibrary)
    {
        try
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) return "Launch the installed TUI from a terminal on this platform, then reopen or reconcile external changes.";
            if (plan is null || !File.Exists(plan)) return "Save and compile the selected plan first.";
            if (!File.Exists(Path.Combine(home, "tap.dll"))) return "The selected OpenTAP home has no tap.dll CLI. Prepare an installed TUI home.";
            if (StandaloneVisaReadiness.ExecutionPrerequisite(new(home), requiresInstrumentLibrary) is { } unavailable) return unavailable;
            if (!Directory.EnumerateFiles(home, "*Tui*.dll", SearchOption.AllDirectories).Any()) return "Install the OpenTAP TUI package into the selected home first.";
            if (OperatingSystem.IsLinux() && LinuxTerminal() is null) return "Install xfce4-terminal or xterm to open the interactive TUI with child-lifetime waiting.";
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { return $"Cannot inspect the selected TUI home: {error.Message}"; }
    }

    private static (string Executable, string[] Arguments)? LinuxTerminal()
    {
        // These modes retain a private terminal process until its interactive child exits.
        // Generic x-terminal-emulator alternatives may hand off to an existing server.
        if (File.Exists("/usr/bin/xfce4-terminal")) return ("/usr/bin/xfce4-terminal", ["--disable-server", "--title=HardwareTest TUI", "--execute"]);
        if (File.Exists("/usr/bin/xterm")) return ("/usr/bin/xterm", ["-T", "HardwareTest TUI", "-e"]);
        return null;
    }

    internal static ProcessStartInfo WindowsStartInfo(string executable, string home, string plan, bool requiresInstrumentLibrary)
    {
        // ShellExecute the console executable itself: argv uses Windows process quoting,
        // with no cmd parser to expand percent names or interpret ampersands in paths.
        var start = new ProcessStartInfo(executable) { WorkingDirectory = home, UseShellExecute = true };
        AddCliArguments(start, home, plan, requiresInstrumentLibrary);
        return start;
    }

    private static void AddCliArguments(ProcessStartInfo start, string home, string plan, bool requiresInstrumentLibrary)
    {
        start.ArgumentList.Add("--roll-forward"); start.ArgumentList.Add("Major");
        start.ArgumentList.Add(Path.Combine(home, requiresInstrumentLibrary || StandaloneVisaReadiness.IsLibraryHome(new(home)) ? HardwareTest.OpenTap.Host.StandaloneVisaPackage.WrapperFileName : "tap.dll")); start.ArgumentList.Add("tui"); start.ArgumentList.Add(plan);
    }

    public async Task<int> LaunchAsync(string home, string plan, bool requiresInstrumentLibrary, CancellationToken cancellationToken = default)
    {
        if (Prerequisite(home, plan, requiresInstrumentLibrary) is { } reason) throw new AuthoringWorkspaceException(reason);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root
            ? Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet") : "dotnet";
        var start = new ProcessStartInfo { WorkingDirectory = home, UseShellExecute = false };
        if (OperatingSystem.IsLinux())
        {
            var terminal = LinuxTerminal() ?? throw new AuthoringWorkspaceException("Install xfce4-terminal or xterm first.");
            start.FileName = terminal.Executable;
            foreach (var argument in terminal.Arguments) start.ArgumentList.Add(argument);
            start.ArgumentList.Add(dotnet);
        }
        else if (OperatingSystem.IsWindows())
        {
            start = WindowsStartInfo(dotnet, home, plan, requiresInstrumentLibrary);
        }
        else throw new PlatformNotSupportedException("Launch the installed TUI from a terminal on this platform, then refresh external changes.");
        if (!OperatingSystem.IsWindows()) AddCliArguments(start, home, plan, requiresInstrumentLibrary);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the external TUI terminal.");
        try
        {
            TerminalStarted?.Invoke(process);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            throw;
        }
        return process.ExitCode;
    }
}
