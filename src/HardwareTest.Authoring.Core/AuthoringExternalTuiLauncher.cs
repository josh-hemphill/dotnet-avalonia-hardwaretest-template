using System.Diagnostics;

namespace HardwareTest.Authoring;

/// Starts the installed interactive CLI as a real external process. It never executes a test plan.
public sealed class AuthoringExternalTuiLauncher
{
    public static string? Prerequisite(string home, string? plan)
    {
        if (plan is null || !File.Exists(plan)) return "Save and compile the selected plan first.";
        if (!File.Exists(Path.Combine(home, "tap.dll"))) return "The selected OpenTAP home has no tap.dll CLI. Prepare an installed TUI home.";
        if (!Directory.EnumerateFiles(home, "*Tui*.dll", SearchOption.AllDirectories).Any()) return "Install the OpenTAP TUI package into the selected home first.";
        if (OperatingSystem.IsLinux() && !File.Exists("/usr/bin/x-terminal-emulator")) return "Install an X terminal emulator to open the interactive TUI.";
        return null;
    }

    public async Task<int> LaunchAsync(string home, string plan, CancellationToken cancellationToken = default)
    {
        if (Prerequisite(home, plan) is { } reason) throw new AuthoringWorkspaceException(reason);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root
            ? Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet") : "dotnet";
        var start = new ProcessStartInfo { WorkingDirectory = home, UseShellExecute = false };
        if (OperatingSystem.IsLinux())
        {
            start.FileName = "/usr/bin/x-terminal-emulator";
            start.ArgumentList.Add("-e"); start.ArgumentList.Add(dotnet);
        }
        else if (OperatingSystem.IsWindows())
        {
            start.FileName = "cmd.exe";
            start.ArgumentList.Add("/c"); start.ArgumentList.Add("start"); start.ArgumentList.Add("/wait");
            start.ArgumentList.Add("HardwareTest TUI"); start.ArgumentList.Add(dotnet);
        }
        else throw new PlatformNotSupportedException("Launch the installed TUI from a terminal on this platform, then refresh external changes.");
        start.ArgumentList.Add("--roll-forward"); start.ArgumentList.Add("Major");
        start.ArgumentList.Add(Path.Combine(home, "tap.dll")); start.ArgumentList.Add("tui"); start.ArgumentList.Add(plan);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the external TUI terminal.");
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().ConfigureAwait(false); } throw; }
        return process.ExitCode;
    }
}
