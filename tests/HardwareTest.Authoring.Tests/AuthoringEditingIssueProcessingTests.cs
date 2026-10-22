using System.Collections.Concurrent;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringEditingIssueProcessingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-editing-issues-" + Guid.NewGuid().ToString("N"));
    private readonly AuthoringWorkspaceViewModel _vm = new();

    public AuthoringEditingIssueProcessingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "plans"));
        File.WriteAllText(Path.Combine(_root, "authoring.json"), "{\"schemaVersion\":2,\"displayName\":\"Issues\"}");
        _vm.Open(_root);
        _vm.CreateDemoProgram("issues");
    }

    public void Dispose()
    {
        _vm.StopEditingIssueProcessing();
        _vm.StopRecovery();
        Directory.Delete(_root, true);
    }

    [Fact]
    public void Worker_coalesces_edits_while_owner_remains_responsive_and_never_publishes_old_snapshot()
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
            if (change.PropertyName == nameof(_vm.EditingIssues)) published.AddRange(_vm.EditingIssues.Select(issue => issue.Message));
        };
        _vm.ConfigureEditingIssueProcessing(queue.Post, () => true, input =>
        {
            Assert.NotEqual(ownerThread, Environment.CurrentManagedThreadId);
            var title = input.Documents.Single().Snapshot.Restore().Sidecar.DisplayName!;
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
                Assert.Equal("original", input.Documents.Single().Snapshot.Restore().Sidecar.DisplayName);
            }
            return [new("TEST_CHECK", title, "issues", Guid.Empty)];
        });
        try
        {
            queue.Complete(entered.Task);
            Assert.True(_vm.EditingIssuesBusy);
            for (var i = 0; i < 20; i++) Assert.Empty(_vm.EditingIssues);
            Assert.Equal(1, calls);
            var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Post(() => heartbeat.SetResult());
            queue.Complete(heartbeat.Task);
            _vm.DisplayName = "intermediate";
            _vm.DisplayName = "latest";
            var finished = _vm.WaitForEditingIssuesAsync();
            release.Set();
            queue.Complete(finished);
            Assert.Equal("latest", Assert.Single(_vm.EditingIssues).Message);
            Assert.DoesNotContain("original", published);
            Assert.DoesNotContain("intermediate", published);
            Assert.Equal(2, calls);
            Assert.False(_vm.EditingIssuesBusy);
            for (var i = 0; i < 20; i++) Assert.Single(_vm.EditingIssues);
            Assert.Equal(2, calls);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void Workspace_transition_and_home_change_discard_inflight_results()
    {
        using var queue = new OwnerQueue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new List<AuthoringEditingIssueInput>();
        var calls = 0;
        var published = new List<string>();
        _vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(_vm.EditingIssues)) published.AddRange(_vm.EditingIssues.Select(issue => issue.Message));
        };
        _vm.ConfigureEditingIssueProcessing(queue.Post, () => true, input =>
        {
            captured.Add(input);
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
                return [new("TEST_CHECK", "obsolete", "issues", Guid.Empty)];
            }
            return [new("TEST_CHECK", "current", input.Documents.Single(document => document.PlanId == "replacement").PlanId, Guid.Empty)];
        });
        try
        {
            queue.Complete(entered.Task);
            _vm.CommitOpen(_vm.PrepareOpen(_root), discardUnsavedChanges: true);
            _vm.CreateDemoProgram("replacement");
            _vm.OpenTapHomeOverride = Path.Combine(_root, "different-home");
            var finished = _vm.WaitForEditingIssuesAsync();
            release.Set();
            queue.Complete(finished);
            Assert.Equal("current", Assert.Single(_vm.EditingIssues).Message);
            Assert.DoesNotContain("obsolete", published);
            Assert.NotEqual(captured[0].Session, captured[^1].Session);
            Assert.NotEqual(captured[0].HomeOverride, captured[^1].HomeOverride);
            Assert.Single(captured[^1].Documents, document => document.PlanId == "replacement");
            Assert.Equal("replacement", Assert.Single(_vm.EditingIssues).PlanId);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void Owner_close_prevents_publication_and_releases_waiters()
    {
        using var queue = new OwnerQueue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = true;
        _vm.ConfigureEditingIssueProcessing(queue.Post, () => owner, _ =>
        {
            entered.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
            return [new("TEST_CHECK", "late", "issues", Guid.Empty)];
        });
        try
        {
            queue.Complete(entered.Task);
            var waiting = _vm.WaitForEditingIssuesAsync();
            owner = false;
            _vm.StopEditingIssueProcessing();
            Assert.True(waiting.IsCompleted);
            release.Set();
            queue.RunOne();
            Assert.Empty(_vm.EditingIssues);
            Assert.False(_vm.EditingIssuesBusy);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void Reentrant_edit_during_publication_schedules_checks_for_the_new_snapshot()
    {
        using var queue = new OwnerQueue();
        var edited = false;
        _vm.DisplayName = "before publication";
        _vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(_vm.EditingIssues) || edited || _vm.EditingIssues.Count == 0) return;
            edited = true;
            _vm.DisplayName = "after publication";
        };
        _vm.ConfigureEditingIssueProcessing(queue.Post, () => true, input =>
            [new("TEST_CHECK", input.Documents.Single().Snapshot.Restore().Sidecar.DisplayName!, "issues", Guid.Empty)]);
        queue.Complete(_vm.WaitForEditingIssuesAsync());
        Assert.True(edited);
        Assert.Equal("after publication", Assert.Single(_vm.EditingIssues).Message);
        Assert.False(_vm.EditingIssuesBusy);
    }

    [Fact]
    public void Headless_issues_remain_synchronous_without_configuration()
    {
        _vm.ReplaceSelected(_vm.SelectedProgram! with { Measure = [] });
        Assert.Contains(_vm.EditingIssues, issue => issue.Code == "EMPTY_MEASURE");
        _vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        Assert.DoesNotContain(_vm.EditingIssues, issue => issue.Code == "EMPTY_MEASURE");
        Assert.True(_vm.WaitForEditingIssuesAsync().IsCompleted);
    }

    private sealed class OwnerQueue : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        public void Post(Action callback) => _queue.Add(callback);
        public void RunOne()
        {
            Assert.True(_queue.TryTake(out var callback, TimeSpan.FromSeconds(15)), "Worker did not dispatch completion.");
            callback();
        }
        public void Complete(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                if (_queue.TryTake(out var callback, 25)) callback();
            Assert.True(task.IsCompleted, "Editing checks did not finish on the owner context.");
            task.GetAwaiter().GetResult();
        }
        public void Dispose() => _queue.Dispose();
    }
}
