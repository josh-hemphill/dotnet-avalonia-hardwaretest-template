using System.Collections.Concurrent;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringEnvironmentInspectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-environment-snapshot-" + Guid.NewGuid().ToString("N"));
    private readonly AuthoringWorkspaceViewModel _vm = new();

    public AuthoringEnvironmentInspectionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "plans"));
        File.WriteAllText(Path.Combine(_root, "authoring.json"), "{\"schemaVersion\":2,\"displayName\":\"Snapshot\"}");
        _vm.Open(_root);
        _vm.CreateDemoProgram("snapshot");
    }

    public void Dispose()
    {
        _vm.StopRecovery();
        Directory.Delete(_root, true);
    }

    [Fact]
    public void Bound_getters_read_the_published_snapshot_without_inspecting_again()
    {
        var calls = 0;
        _vm.EnvironmentInspector = (_, _) =>
        {
            calls++;
            return AuthoringEnvironmentInspection.Empty("Published result");
        };
        _vm.RefreshEnvironmentInspection();
        for (var i = 0; i < 20; i++)
        {
            _ = _vm.EnvironmentPackages;
            _ = _vm.EnvironmentRuntimeFiles;
            _ = _vm.EnvironmentLibraryReadiness;
            _ = _vm.EnvironmentExecutionReadiness;
            _ = _vm.InstrumentTypeChoices;
            _ = _vm.HardwareRows;
            _ = _vm.HardwareEditTypeGuidance;
            _ = _vm.NewInstrumentAvailabilityText;
            _ = _vm.CanPack;
        }
        Assert.Equal(1, calls);
        Assert.Equal("Published result", _vm.LibraryEnvironmentReadinessText);
    }

    [Fact]
    public void Async_inspection_uses_detached_input_and_publishes_only_the_latest_revision_on_owner_context()
    {
        using var context = new OwnerContext();
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var ownerThread = Environment.CurrentManagedThreadId;
        var workerThread = ownerThread;
        var notifications = new List<int>();
        _vm.PropertyChanged += (_, _) => notifications.Add(Environment.CurrentManagedThreadId);
        _vm.Workspace!.Manifest.DisplayName = "first";
        var originalManifest = _vm.Workspace.Manifest;
        _vm.EnvironmentInspector = (workspace, _) =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            Assert.NotSame(originalManifest, workspace.Manifest);
            var name = workspace.Manifest.DisplayName;
            if (name == "first")
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                Assert.Equal("first", workspace.Manifest.DisplayName);
            }
            return AuthoringEnvironmentInspection.Empty(name) with { Error = name };
        };
        try
        {
            var first = _vm.RefreshEnvironmentInspectionAsync();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(_vm.EnvironmentInspectionPending);
            Assert.False(_vm.CanPack);
            _vm.Workspace.Manifest.DisplayName = "second";
            var second = _vm.RefreshEnvironmentInspectionAsync();
            release.Set();
            context.Complete(second);
            Assert.Equal("second", _vm.EnvironmentPathError);
            context.Complete(first);
            Assert.Equal("second", _vm.EnvironmentPathError);
            Assert.False(_vm.EnvironmentInspectionPending);
            Assert.NotEqual(ownerThread, workerThread);
            Assert.All(notifications, thread => Assert.Equal(ownerThread, thread));
        }
        finally
        {
            release.Set();
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }

    [Fact]
    public void Desktop_dispatch_discards_results_after_its_owner_closes()
    {
        using var context = new OwnerContext();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var returned = new ManualResetEventSlim();
        var ownerIsCurrent = true;
        var ownerThread = Environment.CurrentManagedThreadId;
        var publicationThreads = new List<int>();
        _vm.PropertyChanged += (_, _) => publicationThreads.Add(Environment.CurrentManagedThreadId);
        _vm.EnvironmentInspector = (_, _) =>
        {
            Assert.NotEqual(ownerThread, Environment.CurrentManagedThreadId);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            returned.Set();
            return AuthoringEnvironmentInspection.Empty("Obsolete result");
        };
        _vm.ConfigureEnvironmentInspection(action => context.Post(_ => action(), null), () => ownerIsCurrent);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(_vm.EnvironmentInspectionPending);
            ownerIsCurrent = false;
            _vm.StopEnvironmentInspection();
            Assert.True(_vm.WaitForEnvironmentInspectionAsync().IsCompleted);
            release.Set();
            Assert.True(returned.Wait(TimeSpan.FromSeconds(10)));
            context.Complete(_vm.WaitForEnvironmentInspectionAsync());
            Assert.DoesNotContain("Obsolete result", _vm.LibraryEnvironmentReadinessText);
            Assert.All(publicationThreads, thread => Assert.Equal(ownerThread, thread));
        }
        finally { release.Set(); }
    }

    [Fact]
    public void Unexpected_inspection_failures_publish_a_terminal_fail_closed_result()
    {
        using var context = new OwnerContext();
        _vm.EnvironmentInspector = (_, _) => throw new NotSupportedException("Injected unexpected inspection failure");
        _vm.ConfigureEnvironmentInspection(action => context.Post(_ => action(), null), () => true);
        context.Complete(_vm.WaitForEnvironmentInspectionAsync());
        Assert.False(_vm.EnvironmentInspectionPending);
        Assert.Equal("Injected unexpected inspection failure", _vm.EnvironmentPathError);
        Assert.False(_vm.CanPack);
    }

    [Fact]
    public void Reentrant_refresh_notifications_keep_the_latest_completion_task()
    {
        using var context = new OwnerContext();
        _vm.EnvironmentInspector = (_, _) => AuthoringEnvironmentInspection.Empty("Checked");
        _vm.ConfigureEnvironmentInspection(action => context.Post(_ => action(), null), () => true);
        context.Complete(_vm.WaitForEnvironmentInspectionAsync());
        Task? latest = null;
        var reentered = false;
        _vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(_vm.EnvironmentInspectionPending) || !_vm.EnvironmentInspectionPending || reentered) return;
            reentered = true;
            latest = _vm.RefreshEnvironmentInspectionAsync();
        };
        var original = _vm.RefreshEnvironmentInspectionAsync();
        Assert.NotNull(latest);
        Assert.Same(latest, _vm.WaitForEnvironmentInspectionAsync());
        context.Complete(latest!);
        context.Complete(original);
        Assert.False(_vm.EnvironmentInspectionPending);
    }

    [Fact]
    public void Pending_choices_are_retained_only_within_the_same_workspace_session()
    {
        using var context = new OwnerContext();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _vm.RefreshEnvironmentInspection();
        Assert.NotEmpty(_vm.InstrumentTypeChoices);
        var choices = _vm.InstrumentTypeChoices;
        _vm.EnvironmentInspector = (_, _) =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Inspection gate did not release.");
            return AuthoringEnvironmentInspection.Empty("Checked") with { Choices = choices };
        };
        _vm.ConfigureEnvironmentInspection(action => context.Post(_ => action(), null), () => true);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.Same(choices, _vm.InstrumentTypeChoices);
            _vm.CommitOpen(_vm.PrepareOpen(_root), discardUnsavedChanges: true);
            Assert.True(_vm.EnvironmentInspectionPending);
            Assert.Empty(_vm.InstrumentTypeChoices);
            release.Set();
            context.Complete(_vm.WaitForEnvironmentInspectionAsync());
        }
        finally { release.Set(); }
    }

    private sealed class OwnerContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        public void Complete(Task task)
        {
            var timeout = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < timeout)
                if (_queue.TryTake(out var work, 100)) work.Callback(work.State);
            Assert.True(task.IsCompleted, "Inspection did not return to the owning context.");
            task.GetAwaiter().GetResult();
        }
        public void Dispose() => _queue.Dispose();
    }
}
