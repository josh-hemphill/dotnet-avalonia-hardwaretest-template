using System.Diagnostics;
using System.Text.Json;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringWindowsTuiLauncherTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Fact]
    public void Windows_launch_policy_passes_literal_paths_without_a_command_interpreter()
    {
        const string dotnet = @"C:\Program Files\R&D\%TUI_PATH%\dotnet.exe";
        const string home = @"C:\Installed homes\R&D\%TUI_PATH%";
        const string plan = @"C:\Plans\R&D\%TUI_PATH%\selected plan.TapPlan";
        var start = AuthoringExternalTuiLauncher.WindowsStartInfo(dotnet, home, plan);
        Assert.Equal(dotnet, start.FileName);
        Assert.True(start.UseShellExecute);
        Assert.Equal(home, start.WorkingDirectory);
        Assert.Equal(["--roll-forward", "Major", Path.Combine(home, "tap.dll"), "tui", plan], start.ArgumentList);
        Assert.False(start.CreateNoWindow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_actual_child_receives_literal_home_and_plan_and_owns_completion(bool cancel)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requires Windows ShellExecute and console process semantics.");
        var root = AuthoringBuildSnapshotTests.Temp();
        var home = Path.Combine(root, "Installed homes R&D %PATH%");
        Directory.CreateDirectory(home);
        // A real executable protocol fixture substitutes for tap.dll and records its exact argv.
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
            File.Copy(file, Path.Combine(home, Path.GetFileName(file)));
        var fixture = Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture");
        File.Copy(fixture + ".dll", Path.Combine(home, "tap.dll"));
        File.Copy(fixture + ".deps.json", Path.Combine(home, "tap.deps.json"));
        File.Copy(fixture + ".runtimeconfig.json", Path.Combine(home, "tap.runtimeconfig.json"));
        File.WriteAllText(Path.Combine(home, "FixtureTui.dll"), "Prerequisite fixture only");
        var plan = Path.Combine(home, "Literal plan R&D %PATH%.TapPlan");
        File.WriteAllText(plan, "Argument capture fixture; not an executable test plan");
        Assert.Null(AuthoringExternalTuiLauncher.Prerequisite(home, plan));
        Process? owned = null;
        var launcher = new AuthoringExternalTuiLauncher { TerminalStarted = child => owned = child };
        using var cancellation = new CancellationTokenSource();
        var launching = launcher.LaunchAsync(home, plan, cancellation.Token);
        try
        {
            var marker = plan + ".argv.json";
            var elapsed = Stopwatch.StartNew();
            while (!File.Exists(marker))
            {
                if (launching.IsCompleted) await launching;
                Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), "Literal-argument child did not publish readiness.");
                await Task.Delay(20);
            }
            using var captured = JsonDocument.Parse(await File.ReadAllTextAsync(marker));
            Assert.Equal(Path.Combine(home, "tap.dll"), captured.RootElement.GetProperty("assembly").GetString());
            Assert.Equal(home, captured.RootElement.GetProperty("workingDirectory").GetString());
            Assert.Equal(["tui", plan], captured.RootElement.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()));
            Assert.NotNull(owned); Assert.False(owned.HasExited); Assert.False(launching.IsCompleted);
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launching.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            else
            {
                File.WriteAllText(plan + ".release", "");
                Assert.Equal(0, await launching.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            try
            {
                using var exited = Process.GetProcessById(captured.RootElement.GetProperty("pid").GetInt32());
                Assert.True(exited.HasExited);
            }
            catch (ArgumentException) { /* The owned child has already been reaped. */ }
        }
        finally
        {
            cancellation.Cancel();
            try { await launching.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }
}
