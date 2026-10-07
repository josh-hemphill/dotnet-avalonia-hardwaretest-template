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
        documents.Save(AuthoringDocumentDto.FromDraft(saved, revision: 9,
            compiledPlanHash: AuthoringDocumentStore.ComputeHash(plan),
            compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(plan))));
        AuthoringWorkspaceLoader.SaveManifest(root, new AuthoringManifest { PlansDirectory = "." });
        var editor = new AuthoringWorkspaceViewModel();
        using var recoveryOwner = new RecoveryOwner(editor);
        editor.Open(root); editor.SelectProgram("safe");
        editor.DisplayName = "Unsaved source survives actual TUI return";
        var draftRevision = editor.SelectedDocument!.Revision;
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
        // A separate external writer changes the compiled plan while the actual installed TUI is open.
        // This is return/reconciliation evidence, not a claim that these bytes were written by TUI Save.
        compiler.Save(saved with { Setup = [new OperatorPromptSetup("Snapshot sentinel 09", "External saved-change sentinel 22")] }, plan);
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
        editor.RefreshExternalCompiledChanges();
        Assert.Contains("safe", editor.CompiledConflictProgramIds);
        Assert.Equal("Unsaved source survives actual TUI return", editor.DisplayName);
        Assert.Equal(draftRevision, editor.SelectedDocument!.Revision);
        Assert.True(editor.CanUndo); Assert.True(editor.HasUnsavedChanges);
        editor.ReconcileCompiled("safe", false);
        Assert.Empty(editor.CompiledConflictProgramIds);
        Assert.Equal("Unsaved source survives actual TUI return", editor.DisplayName);
        Assert.True(editor.HasUncompiledSources);
        await editor.StopRecoveryAsync();
        // Actual installed TUI launch/render and return reconciliation; no hardware execution or TUI Save claim.
    }
    [Fact]
    public async Task Production_private_terminal_waits_for_actual_installed_TUI_child_to_exit()
    {
        var external = Environment.GetEnvironmentVariable("AUTHORING_EXTERNAL_TUI_HOME") ?? "/workspace/scratch/opentap-external-integration";
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/xfce4-terminal") || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
            || !File.Exists(Path.Combine(external, "tap.dll")))
            Assert.Skip("Production terminal integration requires Linux, a native display, xfce4-terminal and an installed OpenTAP TUI home.");
        var root = AuthoringBuildSnapshotTests.Temp(); var home = Path.Combine(root, "home");
        foreach (var file in Directory.EnumerateFiles(external, "*", SearchOption.AllDirectories))
        {
            if (file.Contains("SessionLogs", StringComparison.Ordinal) || file.Contains(".PackageCache", StringComparison.Ordinal)) continue;
            var target = Path.Combine(home, Path.GetRelativePath(external, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace());
        var plugins = AuthoringBuildSnapshotTests.Home(workspace);
        foreach (var package in new[] { "HardwareTest Basic", "HardwareTest Mixins" })
            foreach (var file in Directory.GetFiles(Path.Combine(plugins.Root, "Packages", package)))
            {
                var target = Path.Combine(home, "Packages", package, Path.GetFileName(file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
            }
        var compiler = new PlanCompiler(selectedHome: new OpenTapHome(home));
        var draft = compiler.Load(Path.Combine(workspace.Root, "sample.TapPlan"));
        var plan = Path.Combine(root, "production-lifetime.TapPlan");
        compiler.Save(draft with { PlanId = "production-lifetime", Setup = [new OperatorPromptSetup("Production terminal lifetime sentinel", "Never execute this probe")] }, plan);
        Process? terminal = null;
        var launcher = new AuthoringExternalTuiLauncher { TerminalStarted = process => terminal = process };
        Assert.Null(AuthoringExternalTuiLauncher.Prerequisite(home, plan));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var launch = launcher.LaunchAsync(home, plan, timeout.Token);
        Process? child = null;
        try
        {
            while (child is null)
            {
                Assert.False(launch.IsCompleted, "Production launcher returned before its interactive CLI child was ready.");
                child = FindOwnedCli(terminal!.Id, plan);
                if (child is null) await Task.Delay(20, timeout.Token);
            }
            var logs = Path.Combine(home, "SessionLogs");
            string evidence = "";
            while (!evidence.Contains("Executing CLI action: tui", StringComparison.Ordinal))
            {
                timeout.Token.ThrowIfCancellationRequested(); Assert.False(launch.IsCompleted);
                if (Directory.Exists(logs))
                    evidence = string.Join("\n", Directory.GetFiles(logs, "*.txt").Select(File.ReadAllText));
                if (!evidence.Contains("Executing CLI action: tui", StringComparison.Ordinal)) await Task.Delay(20, timeout.Token);
            }
            Assert.False(child.HasExited); Assert.False(launch.IsCompleted);
            if (Environment.GetEnvironmentVariable("AUTHORING_EXTERNAL_TUI_EVIDENCE") is { Length: > 0 } evidencePath)
            {
                using var stream = File.Create(evidencePath);
                using var writer = new System.Text.Json.Utf8JsonWriter(stream);
                writer.WriteStartObject();
                writer.WriteString("terminal", terminal!.StartInfo.FileName);
                writer.WriteString("arguments", string.Join(" ", terminal.StartInfo.ArgumentList));
                writer.WriteNumber("terminalPid", terminal.Id); writer.WriteNumber("cliPid", child.Id);
                writer.WriteString("cliCommand", ReadProc($"/proc/{child.Id}/cmdline"));
                writer.WriteString("cliLog", evidence);
                writer.WriteBoolean("launcherPendingWhileCliAlive", !launch.IsCompleted);
                writer.WriteEndObject();
            }
            Assert.DoesNotContain("TestPlan started", evidence);
            // End only the captured original CLI process from our private terminal, then observe terminal completion.
            var terminalExit = terminal!.WaitForExitAsync();
            child.Kill(entireProcessTree: true);
            await launch.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(terminalExit.IsCompletedSuccessfully);
            var state = $"/proc/{child.Id}/stat";
            Assert.True(!File.Exists(state) || ReadProc(state)[(ReadProc(state).LastIndexOf(')') + 1)..].TrimStart().StartsWith('Z'),
                "The captured CLI must have exited when its private terminal completes.");
        }
        finally
        {
            timeout.Cancel();
            try { await launch; } catch (OperationCanceledException) { }
            child?.Dispose();
        }
    }

    private static string ReadProc(string path)
    {
        // Procfs reports zero file length; read the stream until EOF instead of using length-based helpers.
        using var reader = new StreamReader(path);
        return reader.ReadToEnd();
    }

    private static Process? FindOwnedCli(int parent, string plan)
    {
        // This kernel does not expose task/children; verify the direct parent from standard proc stat.
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid)) continue;
            try
            {
                var stat = ReadProc(Path.Combine(directory, "stat"));
                var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture) != parent) continue;
                if (ReadProc(Path.Combine(directory, "cmdline")).Contains(plan, StringComparison.Ordinal)) return Process.GetProcessById(pid);
            }
            catch (IOException) { } // Another unrelated process may exit during enumeration.
        }
        return null;
    }

    private sealed class RecoveryOwner(AuthoringWorkspaceViewModel editor) : IDisposable
    {
        public void Dispose() => editor.StopRecoveryAsync().GetAwaiter().GetResult();
    }
    private static string Quote(string text) => "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
