using System.Diagnostics;
using System.Text.RegularExpressions;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringExternalTuiProcessTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();
    [Fact]
    public async Task Installed_external_TUI_loads_safe_saved_plan_in_real_PTY_without_executing_it()
    {
        var external = Environment.GetEnvironmentVariable("AUTHORING_EXTERNAL_TUI_HOME")
            ?? "/workspace/scratch/opentap-external-integration";
        if (!OperatingSystem.IsLinux() || !File.Exists(Path.Combine(external, "tap.dll"))
            || !File.Exists("/usr/bin/script"))
            Assert.Skip("External TUI integration unavailable: install a real OpenTAP+TUI home and script PTY utility; set AUTHORING_EXTERNAL_TUI_HOME.");
        var root = AuthoringBuildSnapshotTests.Temp();
        var home = Path.Combine(root, "home");
        foreach (var file in Directory.EnumerateFiles(external, "*", SearchOption.AllDirectories))
        {
            if (file.Contains("SessionLogs", StringComparison.Ordinal) || file.Contains(".PackageCache", StringComparison.Ordinal)) continue;
            var destination = Path.Combine(home, Path.GetRelativePath(external, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        if (!Directory.GetFiles(home, "*Tui*.dll", SearchOption.AllDirectories).Any())
            Assert.Skip("External TUI integration unavailable: selected home has no real installed TUI payload.");
        var plan = Path.Combine(root, "safe.TapPlan");
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace());
        var plugins = AuthoringBuildSnapshotTests.Home(workspace);
        foreach (var package in new[] { "HardwareTest Basic", "HardwareTest Mixins" })
            foreach (var file in Directory.GetFiles(Path.Combine(plugins.Root, "Packages", package)))
            {
                var destination = Path.Combine(home, "Packages", package, Path.GetFileName(file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, true);
            }
        var compiler = new PlanCompiler(selectedHome: new OpenTapHome(home));
        var imported = compiler.Load(Path.Combine(workspace.Root, "sample.TapPlan"));
        var saved = imported with
        {
            PlanId = "safe",
            Setup = [new OperatorPromptSetup("Snapshot sentinel 09", "Saved authoring source process integration")],
            Cleanup = new CleanupPolicy(false, Array.Empty<string>())
        };
        saved.Sidecar.DisplayName = "Snapshot sentinel 09";
        var documents = new AuthoringDocumentStore(root);
        documents.Save(AuthoringDocumentDto.FromDraft(saved, revision: 9));
        compiler.Save(documents.Load("safe").Document!.ToDraft(), plan);
        var log = Path.Combine(root, "external-tui.log");
        var start = new ProcessStartInfo("/usr/bin/script")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = home,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-q"); start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"stty rows 24 cols 100; dotnet --roll-forward Major {Quote(Path.Combine(home, "tap.dll"))} tui --log {Quote(log)} {Quote(plan)}");
        start.ArgumentList.Add("/dev/null");
        start.Environment["TERM"] = "xterm";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        // Interactive TUI intentionally remains open. Bound the probe and kill only our cloned-home process tree.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        var screen = Regex.Replace(await stdout, "\\x1b\\[[0-9;?]*[A-Za-z]", "");
        Assert.Contains("Snapshot sentinel 09", screen);
        Assert.DoesNotContain("Errors occured while loading", screen);
        var evidence = await File.ReadAllTextAsync(log);
        Assert.Contains("Executing CLI action: tui", evidence);
        Assert.DoesNotContain("Unable to read element", evidence);
        Assert.DoesNotContain("Unable to resolve type", evidence);
        Assert.DoesNotContain("TestPlan started", evidence);
        Assert.DoesNotContain("Unhandled", await stderr);
        // This is actual external process plan-load evidence, not an external save roundtrip or hardware execution.
    }
    private static string Quote(string text) => "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
