using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringOperationWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_close_retries_failed_cleanup_after_operation_stops_and_stays_open_until_removed(bool closeWhileBusy)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-wait"), "");
        var blocked = 1;
        using var coordinator = new AuthoringOperationCoordinator(AuthoringChildProcessRunner.ForExecutable(
            Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")))
        {
            CancelledCleanup = owned => AuthoringOperationCoordinator.CleanupCancelledOperationAsync(owned, path =>
            {
                Assert.False(Dispatcher.UIThread.CheckAccess());
                if (Volatile.Read(ref blocked) != 0) throw new IOException("Persistent owned-tree lock");
                Directory.Delete(path, true);
            }, TimeSpan.FromMilliseconds(40))
        };
        fixture.ViewModel.ConfigureOperations(coordinator, action => Dispatcher.UIThread.Post(action));
        try
        {
            var running = fixture.ViewModel.RunOperationAsync(AuthoringOperationKind.Bootstrap);
            await Until(() => File.Exists(Path.Combine(fixture.WorkspaceRoot, "fixture-child.json")));
            var owned = File.ReadAllLines(Path.Combine(fixture.WorkspaceRoot, "fixture-child.json"))[1];
            if (!closeWhileBusy)
            {
                fixture.ViewModel.CancelOperation();
                await Until(() => running.IsCompleted);
                await Assert.ThrowsAsync<IOException>(() => running);
            }
            fixture.ViewModel.ReportError("Awaiting first close");
            fixture.Window!.Close();
            await Until(() => fixture.ViewModel.Error == "Persistent owned-tree lock" && !fixture.ViewModel.OperationBusy);
            Assert.True(fixture.Window.IsVisible);
            Assert.True(fixture.ViewModel.OperationCleanupPending);
            Assert.Contains("Persistent owned-tree lock", fixture.ViewModel.Error);
            Assert.True(Directory.Exists(owned));
            fixture.ViewModel.ReportError("Awaiting repeated close");
            fixture.Window.Close();
            await Until(() => fixture.ViewModel.Error == "Persistent owned-tree lock");
            Assert.True(fixture.Window.IsVisible);
            Assert.True(fixture.ViewModel.OperationCleanupPending);
            Volatile.Write(ref blocked, 0);
            fixture.Window.Close();
            await Until(() => !fixture.Window.IsVisible);
            Assert.False(fixture.ViewModel.OperationCleanupPending);
            Assert.False(Directory.Exists(owned));
            await Assert.ThrowsAsync<IOException>(() => running);
        }
        finally { Volatile.Write(ref blocked, 0); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_close_reaps_real_child_and_descendant_before_owner_closes(bool cancelDirtyDialogFirst)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(
            Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")),
            action => Dispatcher.UIThread.Post(action));
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-wait"), "");
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-spawn"), "");
        AuthoringUiFixture.Click(fixture.Control<Button>("Prepare authoring environment"));
        await Until(() => File.Exists(Path.Combine(fixture.WorkspaceRoot, "fixture-descendant")));
        var child = File.ReadAllLines(Path.Combine(fixture.WorkspaceRoot, "fixture-child.json"));
        var childId = int.Parse(child[0]);
        var descendantId = int.Parse(File.ReadAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-descendant")));
        if (cancelDirtyDialogFirst)
        {
            fixture.ViewModel.DisplayName = "Unsaved close test";
            fixture.Window!.Close();
            AuthoringUiFixture.Drain();
            Assert.True(fixture.Window.IsVisible);
            Assert.True(fixture.ViewModel.OperationBusy);
            Assert.True(IsAlive(childId));
            fixture.Interaction.Choice = UnsavedChangesChoice.Discard;
        }
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        fixture.Window!.Close();
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
        Assert.True(fixture.Window.IsVisible);
        var heartbeat = false;
        Dispatcher.UIThread.Post(() => heartbeat = true);
        await Until(() => !fixture.Window.IsVisible);
        Assert.True(heartbeat);
        Assert.False(IsAlive(childId));
        Assert.False(IsAlive(descendantId));
        Assert.False(Directory.Exists(child[1]));
        Assert.False(Directory.Exists(Path.Combine(fixture.WorkspaceRoot, OpenTapHomeBootstrapper.DefaultHomeRelativePath)));
    }

    private static bool IsAlive(int id)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(id);
            if (OperatingSystem.IsLinux() && File.ReadAllText($"/proc/{id}/stat").Split(')')[1].TrimStart().StartsWith('Z')) return false;
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (FileNotFoundException) { return false; }
    }

    [AvaloniaFact]
    public async Task Bootstrap_handler_runs_real_child_off_UI_and_cancel_leaves_workspace_usable()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show();
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(
            Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")),
            action => Dispatcher.UIThread.Post(action));
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-wait"), "");
        var updatesOnUI = true;
        fixture.ViewModel.PropertyChanged += (_, _) => updatesOnUI &= Dispatcher.UIThread.CheckAccess();
        AuthoringUiFixture.Click(fixture.Control<Button>("Prepare authoring environment"));
        Assert.True(fixture.ViewModel.OperationBusy);
        await Until(() => File.Exists(Path.Combine(fixture.WorkspaceRoot, "fixture-child.json")));
        var heartbeat = false;
        Dispatcher.UIThread.Post(() => heartbeat = true);
        AuthoringUiFixture.Drain();
        Assert.True(heartbeat);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel authoring operation"));
        await Until(() => !fixture.ViewModel.OperationBusy);
        Assert.True(updatesOnUI);
        Assert.Null(fixture.ViewModel.Error);
        Assert.Contains("cancelled", fixture.ViewModel.Status);
        Assert.True(fixture.ViewModel.HasWorkspace);
        Assert.False(Directory.Exists(Path.Combine(fixture.WorkspaceRoot, OpenTapHomeBootstrapper.DefaultHomeRelativePath)));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workspace_replacement_suppresses_old_result_and_progress(bool waitForChild)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(
            Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")),
            action => Dispatcher.UIThread.Post(action));
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-wait"), "");
        var running = fixture.ViewModel.RunOperationAsync(AuthoringOperationKind.Bootstrap);
        if (waitForChild) await Until(() => File.Exists(Path.Combine(fixture.WorkspaceRoot, "fixture-child.json")));
        Assert.True(await fixture.Window!.ReopenWorkspaceAsync());
        var replacementStatus = fixture.ViewModel.Status;
        await Until(() => running.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(replacementStatus, fixture.ViewModel.Status);
        Assert.Null(fixture.ViewModel.Error);
        Assert.Null(fixture.ViewModel.OperationStage);
    }

    private static async Task Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("UI operation did not settle.");
            await Task.Delay(20);
            AuthoringUiFixture.Drain();
        }
        AuthoringUiFixture.Drain();
    }
}
