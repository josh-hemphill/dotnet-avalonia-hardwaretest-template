using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringOperationTests : IDisposable
{
    private readonly List<string> roots = [];
    [Fact]
    public async Task Partial_fixture_marker_is_not_visible_until_complete_atomic_publication()
    {
        var root = Temp();
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture"))
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("--partial-marker"); start.ArgumentList.Add(root);
        using var child = Process.Start(start)!;
        try
        {
            Assert.Equal("partial", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(File.Exists(Path.Combine(root, "partial-marker")));
            Assert.Single(Directory.GetFiles(root, "partial-marker.*.tmp"));
            await child.StandardInput.WriteLineAsync("release"); await child.StandardInput.FlushAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, child.ExitCode);
            Assert.Equal(["complete", "owned-root"], File.ReadAllLines(Path.Combine(root, "partial-marker")));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task Query_failure_retains_original_scope_after_staging_removal_until_verified_retry()
    {
        var root = Workspace();
        File.WriteAllText(Path.Combine(root, "fixture-wait"), "");
        File.WriteAllText(Path.Combine(root, "fixture-spawn"), "");
        var blocked = true;
        var observations = 0;
        var executable = Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture");
        var unrelatedStart = new ProcessStartInfo(executable) { UseShellExecute = false };
        unrelatedStart.ArgumentList.Add("--descendant");
        using var unrelated = Process.Start(unrelatedStart)!;
        var runner = new AuthoringChildProcessRunner(executable)
        {
            BeforeExitVerification = () => { observations++; if (blocked) throw new IOException("Injected original scope query failure"); }
        };
        using var coordinator = new AuthoringOperationCoordinator(runner);
        try
        {
            var running = coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root);
            await WaitFor(root, "fixture-descendant", operation: running);
            var child = File.ReadAllLines(Path.Combine(root, "fixture-child.json"));
            var owned = child[1]; roots.Add(owned);
            var descendant = int.Parse(File.ReadAllText(Path.Combine(root, "fixture-descendant")));
            coordinator.Cancel();
            await Assert.ThrowsAsync<IOException>(() => running);
            Assert.False(Directory.Exists(owned));
            Assert.False(coordinator.IsBusy); Assert.True(coordinator.HasPendingCleanup); Assert.True(runner.HasPendingReap);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await Assert.ThrowsAsync<IOException>(() => coordinator.StopAsync());
                Assert.True(coordinator.HasPendingCleanup); Assert.False(unrelated.HasExited);
            }
            blocked = false;
            await coordinator.StopAsync();
            Assert.False(coordinator.HasPendingCleanup); Assert.False(runner.HasPendingReap);
            Assert.False(IsAlive(int.Parse(child[0]))); Assert.False(IsAlive(descendant)); Assert.False(unrelated.HasExited);
            Assert.True(observations >= 4);
            await coordinator.StopAsync();
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(); await unrelated.WaitForExitAsync(); blocked = false; await coordinator.StopAsync(); }
    }

    [Fact]
    public async Task Unix_scope_reaping_waits_for_live_root_and_leaf_after_anchor_exits()
    {
        if (OperatingSystem.IsWindows()) return;
        var owned = Temp();
        var fixture = Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture");
        var start = new ProcessStartInfo(fixture) { UseShellExecute = false };
        start.ArgumentList.Add("--scope-anchor");
        start.ArgumentList.Add(owned);
        using var anchor = Process.Start(start)!;
        using var ownership = new AuthoringProcessOwnership(owned);
        ownership.Attach(anchor);
        Task? reaping = null;
        try
        {
            await WaitFor(owned, "leaf.pid");
            await WaitFor(owned, "root.pid");
            var root = int.Parse(File.ReadAllText(Path.Combine(owned, "root.pid")));
            var leaf = int.Parse(File.ReadAllText(Path.Combine(owned, "leaf.pid")));
            File.WriteAllText(Path.Combine(owned, "anchor.release"), "");
            await anchor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            reaping = ownership.WaitForExitAsync(anchor);
            await Task.Delay(100);
            Assert.False(reaping.IsCompleted);
            Assert.True(IsAlive(root));
            Assert.True(IsAlive(leaf));
            File.WriteAllText(Path.Combine(owned, "root.release"), "");
            // This deliberately orphaned root can remain a zombie until the machine's init reaps it.
            // Observe its live state for the intermediate gate; final completion assertions stay immediate.
            var exiting = Stopwatch.StartNew();
            while (IsAlive(root))
            {
                if (exiting.Elapsed > TimeSpan.FromSeconds(2)) throw new TimeoutException("Fixture root did not exit.");
                await Task.Delay(20);
            }
            await Task.Delay(100);
            Assert.False(reaping.IsCompleted);
            Assert.True(IsAlive(leaf));
            File.WriteAllText(Path.Combine(owned, "leaf.release"), "");
            await reaping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(IsAlive(root));
            Assert.False(IsAlive(leaf));
        }
        finally
        {
            foreach (var role in new[] { "anchor", "root", "leaf" }) File.WriteAllText(Path.Combine(owned, role + ".release"), "");
            await anchor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (reaping is not null) await reaping;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_retains_failed_owned_cleanup_after_busy_clears_and_retries_until_removed(bool stopWhileBusy)
    {
        var root = Workspace();
        File.WriteAllText(Path.Combine(root, "fixture-wait"), "");
        var blocked = 1;
        using var coordinator = new AuthoringOperationCoordinator(AuthoringChildProcessRunner.ForExecutable(
            Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")))
        {
            CancelledCleanup = owned => AuthoringOperationCoordinator.CleanupCancelledOperationAsync(owned, path =>
            {
                if (Volatile.Read(ref blocked) != 0) throw new IOException("Persistent owned-tree lock");
                Directory.Delete(path, true);
            }, TimeSpan.FromMilliseconds(40))
        };
        var running = coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root);
        await WaitFor(root, "fixture-child.json", operation: running);
        var owned = File.ReadAllLines(Path.Combine(root, "fixture-child.json"))[1];
        roots.Add(owned);
        if (stopWhileBusy) await Assert.ThrowsAsync<IOException>(() => coordinator.StopAsync());
        else coordinator.Cancel();
        await Assert.ThrowsAsync<IOException>(() => running);
        Assert.False(coordinator.IsBusy);
        Assert.True(coordinator.HasPendingCleanup);
        if (!stopWhileBusy)
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<IOException>(() => coordinator.StopAsync());
            Assert.True(coordinator.HasPendingCleanup);
            Assert.True(Directory.Exists(owned));
        }
        Volatile.Write(ref blocked, 0);
        await coordinator.StopAsync();
        Assert.False(coordinator.HasPendingCleanup);
        Assert.False(Directory.Exists(owned));
        await coordinator.StopAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_operation_cleanup_retries_transient_file_release_failures(bool unauthorized)
    {
        var owned = Temp();
        File.WriteAllText(Path.Combine(owned, "held"), "owned data");
        var attempts = 0;
        var cleanup = AuthoringOperationCoordinator.CleanupCancelledOperationAsync(owned, path =>
        {
            if (++attempts <= 2)
            {
                if (unauthorized) throw new UnauthorizedAccessException("File release pending");
                throw new IOException("File release pending");
            }
            Directory.Delete(path, true);
        });
        Assert.False(cleanup.IsCompleted);
        await cleanup.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(3, attempts);
        Assert.False(Directory.Exists(owned));
    }

    [Fact]
    public async Task Cancelled_operation_cleanup_surfaces_persistent_failure_with_a_bound()
    {
        var owned = Temp();
        var failure = new IOException("Persistent owned-tree lock");
        var attempts = 0;
        var cleanup = AuthoringOperationCoordinator.CleanupCancelledOperationAsync(owned,
            _ => { attempts++; throw failure; }, TimeSpan.FromMilliseconds(40));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => cleanup.WaitAsync(TimeSpan.FromSeconds(2))));
        Assert.True(attempts > 1);
        Assert.True(Directory.Exists(owned));
    }

    [Fact]
    public async Task Disappearing_owner_terminates_native_scope_and_descendants()
    {
        var root = Workspace();
        File.WriteAllText(Path.Combine(root, "fixture-spawn"), "");
        File.WriteAllText(Path.Combine(root, "fixture-wait"), "");
        var fixture = Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var start = new ProcessStartInfo(fixture) { UseShellExecute = false };
        start.ArgumentList.Add("--operation-owner"); start.ArgumentList.Add(root);
        using var owner = Process.Start(start)!;
        try
        {
            await WaitFor(root, "fixture-descendant", operation: owner.WaitForExitAsync());
            var child = File.ReadAllLines(Path.Combine(root, "fixture-child.json"));
            roots.Add(child[1]); // Abrupt owner death cannot run its owned-directory finally cleanup.
            var anchor = int.Parse(File.ReadAllText(Path.Combine(child[1], "host-ready")));
            var descendant = int.Parse(File.ReadAllText(Path.Combine(root, "fixture-descendant")));
            owner.Kill(); await owner.WaitForExitAsync();
            var clock = Stopwatch.StartNew();
            while (IsAlive(anchor) || IsAlive(int.Parse(child[0])) || IsAlive(descendant))
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Native ownership scope outlived its owner.");
                await Task.Delay(20);
            }
        }
        finally { if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); } }
    }

    [Fact]
    public void Failed_publication_restores_read_only_executable_preimage_and_mode()
    {
        if (OperatingSystem.IsWindows()) return;
        var source = Temp();
        var output = Temp();
        var helper = Path.Combine(output, "a-helper");
        File.WriteAllText(helper, "previous helper");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserExecute;
        File.SetUnixFileMode(helper, mode);
        var incoming = Path.Combine(source, "a-helper");
        File.WriteAllText(incoming, "replacement helper");
        File.SetUnixFileMode(incoming, mode);
        File.WriteAllText(Path.Combine(source, "z-fail"), "fail second move");
        Assert.Throws<IOException>(() => AuthoringBuildService.Publish(source, output, default, move: (from, to) =>
        {
            if (Path.GetFileName(from) == "z-fail") throw new IOException("Injected publication failure");
            File.Move(from, to, true);
        }));
        Assert.Equal("previous helper", File.ReadAllText(helper));
        Assert.Equal(mode, File.GetUnixFileMode(helper));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exited_operation_root_with_inherited_pipe_descendant_is_reaped_without_killing_unrelated_process(bool cancelAtDrain)
    {
        var root = Workspace();
        var owned = Temp();
        File.WriteAllText(Path.Combine(root, "fixture-spawn"), "");
        File.WriteAllText(Path.Combine(root, "fixture-root-exit"), "");
        var fixture = Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        using var unrelated = Process.Start(new ProcessStartInfo(fixture, "--descendant") { UseShellExecute = false })!;
        try
        {
            var path = Path.Combine(owned, "request.json");
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new AuthoringChildRequest(1, Guid.NewGuid().ToString("N"),
                AuthoringOperationKind.Bootstrap, root, owned, null, true), AuthoringOperationJsonContext.Default.AuthoringChildRequest));
            using var cancellation = new CancellationTokenSource();
            var running = AuthoringChildProcessRunner.ForExecutable(fixture).RunAsync(path, _ => { }, stage =>
            {
                if (stage.Stage == "Reaping operation processes" && cancelAtDrain) cancellation.Cancel();
            }, cancellation.Token);
            if (cancelAtDrain) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(15)));
            else Assert.Equal(7, await running.WaitAsync(TimeSpan.FromSeconds(15)));
            var child = File.ReadAllLines(Path.Combine(root, "fixture-child.json"));
            Assert.False(IsAlive(int.Parse(child[0])));
            Assert.False(IsAlive(int.Parse(File.ReadAllText(Path.Combine(root, "fixture-descendant")))));
            Assert.False(unrelated.HasExited);
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(); await unrelated.WaitForExitAsync(); }
    }

    [Theory]
    [InlineData(AuthoringOperationKind.Bootstrap)]
    [InlineData(AuthoringOperationKind.Pack)]
    public async Task Executable_selected_home_helper_keeps_mode_through_clone_preparation_and_publication(AuthoringOperationKind kind)
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Workspace(isolatedPlans: true);
        var home = Temp();
        var helper = Path.Combine(home, "unrelated-helper");
        File.WriteAllText(helper, "#!/bin/sh\nprintf 'permission-preserved'");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        File.SetUnixFileMode(helper, mode);
        File.WriteAllText(Path.Combine(root, "fixture-result-wait"), "");
        using var coordinator = Coordinator();
        var running = coordinator.RunAsync(kind, root, kind == AuthoringOperationKind.Pack ? Temp() : null, home);
        await WaitFor(root, "fixture-prepared", TimeSpan.FromSeconds(60), running);
        var owned = File.ReadAllLines(Path.Combine(root, "fixture-child.json"))[1];
        Assert.Equal(mode, File.GetUnixFileMode(Path.Combine(owned, "home", "unrelated-helper")));
        if (kind == AuthoringOperationKind.Pack)
            Assert.Equal(mode, File.GetUnixFileMode(Path.Combine(owned, "prepared", "home", "unrelated-helper")));
        File.WriteAllText(Path.Combine(owned, "fixture-release"), "");
        var result = await running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(mode, File.GetUnixFileMode(helper));
        if (result.Build is { } build)
            Assert.Equal((int)mode, Assert.Single(build.Receipt.Inputs, input => input.Path == helper).UnixMode);
        using var process = Process.Start(new ProcessStartInfo(helper) { UseShellExecute = false, RedirectStandardOutput = true })!;
        Assert.Equal("permission-preserved", await process.StandardOutput.ReadToEndAsync());
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task Permission_only_selected_home_change_rejects_publication()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Workspace();
        var home = Temp();
        var helper = Path.Combine(home, "unrelated-helper");
        File.WriteAllText(helper, "same bytes");
        File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var coordinator = Coordinator();
        var error = await Assert.ThrowsAsync<AuthoringWorkspaceException>(() => coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: home,
            progress: stage => { if (!OperatingSystem.IsWindows() && stage.Stage == "Publishing prepared home") File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite); }));
        Assert.Contains("BUILD_INPUT_CHANGED", error.Message);
        Assert.Equal("same bytes", File.ReadAllText(helper));
        Assert.Equal(["unrelated-helper"], Directory.GetFiles(home).Select(path => Path.GetFileName(path)!).ToArray());
    }

    [Fact]
    public async Task Slow_real_child_keeps_caller_responsive_blocks_duplicates_and_bounds_distinct_logs()
    {
        var root = Workspace();
        File.WriteAllText(Path.Combine(root, "fixture-wait"), "");
        File.WriteAllText(Path.Combine(root, "fixture-flood"), "");
        using var coordinator = Coordinator();
        var running = coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root);
        await WaitFor(root, "fixture-child.json");
        var heartbeat = 0;
        for (var index = 0; index < 10; index++) { await Task.Delay(20); heartbeat++; }
        Assert.Equal(10, heartbeat);
        Assert.False(running.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(AuthoringOperationKind.Validate, root));
        Assert.InRange(coordinator.Logs.Count, 1, 128);
        Assert.All(coordinator.Logs, log => Assert.InRange(log.Text.Length, 1, 1024));
        // Both pipes are independently drained; their bounded retained tails keep stream identity.
        Assert.Contains(coordinator.Logs, log => log.Stream == "stderr");
        File.WriteAllText(Path.Combine(root, "fixture-release"), "");
        var result = await running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(File.Exists(Path.Combine(result.Home!, "OpenTap.dll")));
        Assert.False(coordinator.IsBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_or_workspace_replacement_after_preparation_never_publishes_and_removes_owned_tree(bool replace)
    {
        var root = Workspace();
        var output = Temp();
        File.WriteAllText(Path.Combine(output, "existing.txt"), "previous");
        File.WriteAllText(Path.Combine(root, "fixture-result-wait"), "");
        File.WriteAllText(Path.Combine(root, "fixture-spawn"), "");
        using var coordinator = Coordinator();
        var running = coordinator.RunAsync(AuthoringOperationKind.Pack, root, output);
        await WaitFor(root, "fixture-prepared", TimeSpan.FromSeconds(60), running);
        var child = File.ReadAllLines(Path.Combine(root, "fixture-child.json"));
        var clock = Stopwatch.StartNew();
        if (replace) coordinator.ReplaceWorkspace(); else coordinator.Cancel();
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(output, "existing.txt")));
        Assert.False(File.Exists(Path.Combine(output, AuthoringBuildService.ReceiptFileName)));
        Assert.False(Directory.Exists(child[1]));
        Assert.False(IsAlive(int.Parse(child[0])));
        Assert.False(IsAlive(int.Parse(File.ReadAllText(Path.Combine(root, "fixture-descendant")))));
        Assert.False(Directory.Exists(Path.Combine(root, OpenTapHomeBootstrapper.DefaultHomeRelativePath)));
    }

    [Fact]
    public async Task Cancellation_at_parent_publication_boundary_preserves_previous_artifacts()
    {
        var root = Workspace();
        var output = Temp();
        File.WriteAllText(Path.Combine(output, "existing.txt"), "previous");
        using var coordinator = Coordinator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(AuthoringOperationKind.Pack, root, output,
            progress: update => { if (update.Stage == "Publishing checked artifacts") coordinator.Cancel(); }));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(output, "existing.txt")));
        Assert.False(File.Exists(Path.Combine(output, AuthoringBuildService.ReceiptFileName)));
    }

    [Fact]
    public async Task Cancelled_bootstrap_never_changes_selected_home()
    {
        var root = Workspace();
        var home = Temp();
        File.WriteAllText(Path.Combine(home, "existing.txt"), "previous");
        using var coordinator = Coordinator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: Path.GetRelativePath(Environment.CurrentDirectory, home),
            progress: update => { if (update.Stage == "Publishing prepared home") coordinator.Cancel(); }));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(home, "existing.txt")));
        Assert.Equal(["existing.txt"], Directory.GetFiles(home).Select(path => Path.GetFileName(path)!).ToArray());
    }

    [Fact]
    public async Task Each_workspace_uses_a_new_process_and_selected_home_clone()
    {
        var first = Workspace();
        var second = Workspace();
        var firstHome = Temp();
        var secondHome = Temp();
        var firstManifest = AuthoringWorkspaceLoader.Load(first).Manifest;
        firstManifest.Dependencies.Add(new AuthoringPackageDependency { Package = OpenTapHomeBootstrapper.VisaPackageName, Version = "0.1.0" });
        AuthoringWorkspaceLoader.SaveManifest(first, firstManifest);
        File.WriteAllText(Path.Combine(firstHome, "plugin-environment"), "one");
        File.WriteAllText(Path.Combine(secondHome, "plugin-environment"), "two");
        foreach (var (home, name) in new[] { (firstHome, "EnvironmentOne"), (secondHome, "EnvironmentTwo") })
        {
            var directory = Path.Combine(home, "Packages", name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "package.xml"), $"<Package Name=\"{name}\" Version=\"1.0.0\" />");
        }
        using var coordinator = Coordinator();
        await coordinator.RunAsync(AuthoringOperationKind.Bootstrap, first, home: firstHome);
        Assert.Contains(coordinator.Logs, log => log.Text.Contains("home-package:EnvironmentOne", StringComparison.Ordinal));
        Assert.DoesNotContain(coordinator.Logs, log => log.Text.Contains("home-package:EnvironmentTwo", StringComparison.Ordinal));
        await coordinator.RunAsync(AuthoringOperationKind.Bootstrap, second, home: secondHome);
        Assert.Contains(coordinator.Logs, log => log.Text.Contains("home-package:EnvironmentTwo", StringComparison.Ordinal));
        Assert.DoesNotContain(coordinator.Logs, log => log.Text.Contains("home-package:EnvironmentOne", StringComparison.Ordinal));
        Assert.NotEqual(File.ReadAllLines(Path.Combine(first, "fixture-child.json"))[0], File.ReadAllLines(Path.Combine(second, "fixture-child.json"))[0]);
        Assert.True(File.Exists(Path.Combine(firstHome, "Packages", OpenTapHomeBootstrapper.VisaPackageName, OpenTapHomeBootstrapper.VisaAssemblyFileName)));
        Assert.False(Directory.EnumerateFiles(secondHome, OpenTapHomeBootstrapper.VisaAssemblyFileName, SearchOption.AllDirectories).Any());
        Assert.Equal("one", File.ReadAllText(Path.Combine(firstHome, "plugin-environment")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(secondHome, "plugin-environment")));
        Assert.All(new[] { first, second }, root => Assert.False(Directory.Exists(File.ReadAllLines(Path.Combine(root, "fixture-child.json"))[1])));
    }

    [Fact]
    public async Task Production_child_result_roundtrip_publishes_checked_build_and_actionable_failures()
    {
        var root = Workspace();
        var output = Temp();
        using var coordinator = Coordinator();
        var result = await coordinator.RunAsync(AuthoringOperationKind.Pack, root, output).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.NotNull(result.Build);
        Assert.Contains(result.Build.Receipt.RequiredChecks, finding => finding.Code == "BUILD_COMPATIBILITY_PASS");
        Assert.True(File.Exists(Path.Combine(output, AuthoringBuildService.ReceiptFileName)));
        var manifest = AuthoringWorkspaceLoader.Load(root).Manifest;
        manifest.PluginProjects.Add("missing.TapPackage");
        AuthoringWorkspaceLoader.SaveManifest(root, manifest);
        var error = await Assert.ThrowsAsync<AuthoringWorkspaceException>(() => coordinator.RunAsync(AuthoringOperationKind.Pack, root, output));
        Assert.Contains("missing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(coordinator.Logs, log => log.Stream == "stderr" && log.Text.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_uses_fresh_child_and_keeps_selected_home_unmodified()
    {
        var root = Workspace();
        var home = Temp();
        File.WriteAllText(Path.Combine(home, "existing.txt"), "previous");
        using var coordinator = Coordinator();
        var result = await coordinator.RunAsync(AuthoringOperationKind.Validate, root, home: home);
        Assert.NotNull(result.Validation);
        Assert.Equal(Path.Combine(root, "sample.TapPlan"), Assert.Single(result.Validation.Plans).TargetPath);
        Assert.Equal("previous", File.ReadAllText(Path.Combine(home, "existing.txt")));
        Assert.Equal(["existing.txt"], Directory.GetFiles(home).Select(path => Path.GetFileName(path)!).ToArray());
        Assert.Null(result.Build);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsupported_child_result_version_is_rejected_before_home_publication(bool directExecutable)
    {
        var root = Workspace();
        var home = Temp();
        File.WriteAllText(Path.Combine(root, "fixture-corrupt-version"), "");
        using var coordinator = Coordinator(directExecutable);
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: home));
        Assert.Empty(Directory.EnumerateFiles(home));
    }

    private static AuthoringOperationCoordinator Coordinator(bool directExecutable = false)
        => new(AuthoringChildProcessRunner.ForExecutable(Path.Combine(AppContext.BaseDirectory,
            "HardwareTest.Authoring.ProcessFixture" + (directExecutable ? OperatingSystem.IsWindows() ? ".exe" : "" : ".dll"))));
    private string Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "operation-test-" + Guid.NewGuid().ToString("N"));
        roots.Add(root); Directory.CreateDirectory(root); return root;
    }
    private string Workspace(bool isolatedPlans = false)
    {
        var root = Temp();
        var plans = isolatedPlans ? Path.Combine(root, "plans") : root;
        Directory.CreateDirectory(plans);
        var repository = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repository, "dirs.proj"))) repository = Path.GetDirectoryName(repository)!;
        File.Copy(Path.Combine(repository, "plans/opentap/sample.TapPlan"), Path.Combine(plans, "sample.TapPlan"));
        File.Copy(Path.Combine(repository, "plans/opentap/sample.program.json"), Path.Combine(plans, "sample.program.json"));
        AuthoringWorkspaceLoader.SaveManifest(root, new AuthoringManifest
        {
            PlansDirectory = isolatedPlans ? "plans" : ".",
            Package = new AuthoringPackageSpec { Name = "Operation fixture", Version = "0.1.0" },
            Dependencies = [new AuthoringPackageDependency { Package = "HardwareTest Basic", Version = "0.2.0" }, new AuthoringPackageDependency { Package = "HardwareTest Mixins", Version = "0.1.0" }]
        });
        return root;
    }
    private static async Task WaitFor(string root, string file, TimeSpan? timeout = null, Task? operation = null)
    {
        var clock = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(root, file)))
        {
            if (operation?.IsCompleted == true) { await operation; throw new InvalidOperationException("Child exited before the requested barrier."); }
            if (clock.Elapsed > (timeout ?? TimeSpan.FromSeconds(10))) throw new TimeoutException(file);
            await Task.Delay(20);
        }
    }
    private static bool IsAlive(int id)
    {
        try
        {
            if (OperatingSystem.IsLinux() && File.Exists($"/proc/{id}/stat") && File.ReadAllText($"/proc/{id}/stat").Split(')')[1].TrimStart().StartsWith('Z')) return false;
            using var process = Process.GetProcessById(id); return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    public void Dispose() { foreach (var root in roots) if (Directory.Exists(root)) Directory.Delete(root, true); }
}
