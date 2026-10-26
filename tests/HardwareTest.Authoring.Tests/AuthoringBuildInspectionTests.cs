using System.Collections.Concurrent;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringBuildInspectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-build-inspection-" + Guid.NewGuid().ToString("N"));
    private readonly AuthoringWorkspaceViewModel _vm = new();
    public AuthoringBuildInspectionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "plans"));
        File.WriteAllText(Path.Combine(_root, "authoring.json"), "{\"schemaVersion\":2,\"displayName\":\"Build\"}");
        _vm.Open(_root);
        _vm.CreateDemoProgram("build");
    }
    public void Dispose()
    {
        _vm.StopBuildInspection();
        _vm.StopRecovery();
        Directory.Delete(_root, true);
    }
    [Fact]
    public void Readiness_getters_do_not_run_inspection_and_edits_supersede_blocked_worker()
    {
        using var queue = new OwnerQueue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerThread = Environment.CurrentManagedThreadId;
        var calls = 0;
        var published = new List<string>();
        _vm.DisplayName = "original";
        _vm.PropertyChanged += (_, change) =>
        {
            Assert.Equal(ownerThread, Environment.CurrentManagedThreadId);
            if (change.PropertyName == nameof(_vm.BuildPrograms)) published.AddRange(_vm.BuildPrograms.Select(program => program.State));
        };
        _vm.ConfigureBuildInspection(queue.Post, () => true, input =>
        {
            Assert.NotEqual(ownerThread, Environment.CurrentManagedThreadId);
            var title = input.Documents.Single().Snapshot.Restore().Sidecar.DisplayName!;
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
                Assert.Equal("original", input.Documents.Single().Snapshot.Restore().Sidecar.DisplayName);
            }
            return new(WorkspacePackPlan.Empty, [new("build", true, 1, title, true)], false, true, title);
        });
        try
        {
            queue.Complete(entered.Task);
            Assert.True(_vm.BuildInspectionBusy);
            for (var i = 0; i < 20; i++)
            {
                Assert.Empty(_vm.BuildPrograms);
                Assert.Empty(_vm.ExcludedPackPlans);
                Assert.False(_vm.CanPack);
                _ = _vm.PackGuardText;
                _ = _vm.BuildReadinessText;
            }
            Assert.Equal(1, calls);
            _vm.DisplayName = "intermediate";
            _vm.DisplayName = "latest";
            var finished = _vm.WaitForBuildInspectionAsync();
            release.Set();
            queue.Complete(finished);
            Assert.Equal("latest", Assert.Single(_vm.BuildPrograms).State);
            Assert.DoesNotContain("original", published);
            Assert.DoesNotContain("intermediate", published);
            Assert.Equal(2, calls);
            Assert.False(_vm.BuildInspectionBusy);
        }
        finally { release.Set(); }
    }
    [Fact]
    public void Home_change_and_workspace_replacement_reject_old_inspection()
    {
        using var queue = new OwnerQueue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var captured = new List<AuthoringBuildInspectionInput>();
        var published = new List<string>();
        _vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(_vm.BuildPrograms)) published.AddRange(_vm.BuildPrograms.Select(program => program.State));
        };
        _vm.ConfigureBuildInspection(queue.Post, () => true, input =>
        {
            captured.Add(input);
            var state = "current";
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
                state = "obsolete";
            }
            return new(WorkspacePackPlan.Empty, [new("build", true, 1, state, true)], false, true, state);
        });
        try
        {
            queue.Complete(entered.Task);
            _vm.CommitOpen(_vm.PrepareOpen(_root), discardUnsavedChanges: true);
            _vm.OpenTapHomeOverride = Path.Combine(_root, "different-home");
            var finished = _vm.WaitForBuildInspectionAsync();
            release.Set();
            queue.Complete(finished);
            Assert.Equal("current", Assert.Single(_vm.BuildPrograms).State);
            Assert.DoesNotContain("obsolete", published);
            Assert.NotEqual(captured[0].Session, captured[^1].Session);
            Assert.NotEqual(captured[0].Home, captured[^1].Home);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void Failed_inspection_blocks_build_and_reports_failure_on_owner()
    {
        using var queue = new OwnerQueue();
        _vm.ConfigureBuildInspection(queue.Post, () => true, _ => throw new IOException("missing source"));
        queue.Complete(_vm.WaitForBuildInspectionAsync());
        Assert.False(_vm.BuildInspectionBusy);
        Assert.False(_vm.CanPack);
        Assert.Contains("missing source", _vm.Error);
    }
    [Fact]
    public void Reentrant_refresh_during_publication_runs_one_followup_worker()
    {
        using var queue = new OwnerQueue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var requested = false;
        _vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(_vm.BuildPrograms) || requested || _vm.BuildPrograms.Count == 0) return;
            requested = true;
            _vm.RequestBuildInspection();
        };
        _vm.ConfigureBuildInspection(queue.Post, () => true, _ =>
        {
            if (Interlocked.Increment(ref calls) > 1)
            {
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
            }
            return new(WorkspacePackPlan.Empty, [new("build", true, 1, "ready", true)], false, true, "identity");
        });
        try
        {
            queue.Complete(entered.Task);
            Assert.Equal(2, calls);
            Assert.True(_vm.BuildInspectionBusy);
            Assert.False(_vm.WaitForBuildInspectionAsync().IsCompleted);
            release.Set();
            queue.Complete(_vm.WaitForBuildInspectionAsync());
            Assert.Equal(2, calls);
            Assert.False(_vm.BuildInspectionBusy);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void Excluded_source_is_listed_without_following_its_unsafe_link()
    {
        if (OperatingSystem.IsWindows()) return;
        using var queue = new OwnerQueue();
        var outside = Path.GetTempFileName();
        var link = Path.Combine(_root, "authoring-drafts", "excluded.authoring.json");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            File.WriteAllText(outside, "unreadable deployment source");
            File.CreateSymbolicLink(link, outside);
            _vm.Workspace!.Manifest.ExcludedProgramIds.Add("excluded");
            _vm.ConfigureBuildInspection(queue.Post, () => true);
            queue.Complete(_vm.WaitForBuildInspectionAsync());
            Assert.Null(_vm.Error);
            var row = Assert.Single(_vm.BuildPrograms, program => program.PlanId == "excluded");
            Assert.False(row.Included);
            Assert.Equal("Excluded from build", row.State);
            Assert.Null(row.SavedRevision);
        }
        finally { File.Delete(link); File.Delete(outside); }
    }

    private sealed class OwnerQueue : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        public void Post(Action callback) => _queue.Add(callback);
        public void Complete(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                if (_queue.TryTake(out var callback, 25)) callback();
            Assert.True(task.IsCompleted, "Build inspection did not settle.");
            task.GetAwaiter().GetResult();
        }
        public void Dispose() => _queue.Dispose();
    }
}
