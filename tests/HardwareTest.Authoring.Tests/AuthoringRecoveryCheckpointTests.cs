using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringRecoveryCheckpointTests
{
    [Fact]
    public async Task DebounceCapturesLatestIsolatedContentAndNeverMarksSessionSaved()
    {
        using var workspace = new TemporaryWorkspace();
        var session = new AuthoringDocumentSession(Draft());
        session.ApplyEdit("rename", draft => draft with { Sidecar = new ProgramSidecar { DisplayName = "edited" } });
        var result = Completion();
        using var recovery = new AuthoringRecoveryCheckpointService(action => action(),
            value => result.TrySetResult(value), TimeSpan.FromMilliseconds(30));
        recovery.Schedule(workspace.Root, AuthoringDocumentDto.FromDraft(session.Draft, 1));
        var latest = AuthoringDocumentDto.FromDraft(session.Draft, 2);
        latest.State.IncompleteNumericText["pending"] = "1e-";
        recovery.Schedule(workspace.Root, latest);
        latest.Sidecar.DisplayName = "mutated after schedule";
        latest.State.IncompleteNumericText["pending"] = "changed";
        latest.Revision = 99;

        var completed = await result.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(completed.IsSuccess);
        Assert.Equal(2, completed.Revision);
        var store = new AuthoringDocumentStore(workspace.Root);
        var saved = store.LoadAtPath(store.GetRecoveryPath("test")).Document!;
        Assert.Equal("edited", saved.Sidecar.DisplayName);
        Assert.Equal("1e-", saved.State.IncompleteNumericText["pending"]);
        Assert.Equal(2, saved.Revision);
        Assert.True(session.IsDirty);
        Assert.False(File.Exists(store.GetDocumentPath("test")));
    }

    [Fact]
    public async Task DifferentProgramsKeepIndependentPendingCheckpoints()
    {
        using var workspace = new TemporaryWorkspace();
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        using var recovery = new AuthoringRecoveryCheckpointService(action => action(), _ =>
        {
            if (Interlocked.Increment(ref completed) == 2) both.TrySetResult();
        }, TimeSpan.FromMilliseconds(30));
        recovery.Schedule(workspace.Root, AuthoringDocumentDto.FromDraft(Draft(), 1));
        recovery.Schedule(workspace.Root, AuthoringDocumentDto.FromDraft(Draft() with { PlanId = "other" }, 4));
        await both.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var store = new AuthoringDocumentStore(workspace.Root);
        Assert.Equal(1, store.LoadAtPath(store.GetRecoveryPath("test")).Document!.Revision);
        Assert.Equal(4, store.LoadAtPath(store.GetRecoveryPath("other")).Document!.Revision);
    }

    [Fact]
    public async Task CompletionRunsOnlyThroughDispatchAndCanceledSessionSuppressesQueuedCallback()
    {
        using var workspace = new TemporaryWorkspace();
        var queued = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = 0;
        using var recovery = new AuthoringRecoveryCheckpointService(action => queued.TrySetResult(action),
            _ => callbacks++, TimeSpan.Zero);
        recovery.Schedule(workspace.Root, AuthoringDocumentDto.FromDraft(Draft(), 1));
        var notification = await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, callbacks);
        recovery.Cancel(workspace.Root, "test");
        notification();
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task FailedAtomicReplacementKeepsPreviousCheckpointAndPendingDraftContent()
    {
        using var workspace = new TemporaryWorkspace();
        var store = new AuthoringDocumentStore(workspace.Root);
        var path = store.GetRecoveryPath("test");
        store.SaveAtPath(path, AuthoringDocumentDto.FromDraft(Draft(), 1));
        var original = File.ReadAllBytes(path);
        var expectedError = new IOException("replacement failed");
        var result = Completion();
        var failingStore = new AuthoringDocumentStore(workspace.Root,
            new AuthoringAtomicWriter((_, _) => throw expectedError));
        using var recovery = new AuthoringRecoveryCheckpointService(action => action(),
            value => result.TrySetResult(value), TimeSpan.Zero,
            (destination, document) => failingStore.SaveAtPath(destination, document));
        var pending = AuthoringDocumentDto.FromDraft(Draft(), 2);
        pending.State.IncompleteNumericText["pending"] = "unfinished";
        recovery.Schedule(workspace.Root, pending);
        var completed = await result.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(completed.IsSuccess);
        Assert.Same(expectedError, completed.Error);
        Assert.Equal(path, completed.Path);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
        Assert.Equal("unfinished", pending.State.IncompleteNumericText["pending"]);
    }

    [Fact]
    public async Task DisposeCancelsPendingWritesAndRejectsFurtherSchedules()
    {
        using var workspace = new TemporaryWorkspace();
        var completed = 0;
        var recovery = new AuthoringRecoveryCheckpointService(action => action(), _ => completed++,
            TimeSpan.FromMilliseconds(100));
        var document = AuthoringDocumentDto.FromDraft(Draft());
        recovery.Schedule(workspace.Root, document);
        recovery.Dispose();
        await Task.Delay(150);
        Assert.Equal(0, completed);
        Assert.False(File.Exists(new AuthoringDocumentStore(workspace.Root).GetRecoveryPath("test")));
        Assert.Throws<ObjectDisposedException>(() => recovery.Schedule(workspace.Root, document));
    }

    [Fact]
    public void FutureSchemaNeverSchedulesARecoveryOverwrite()
    {
        using var workspace = new TemporaryWorkspace();
        using var recovery = new AuthoringRecoveryCheckpointService(action => action(), _ => { });
        var document = AuthoringDocumentDto.FromDraft(Draft());
        document.SchemaVersion++;
        Assert.Throws<InvalidDataException>(() => recovery.Schedule(workspace.Root, document));
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".authoring")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Slow_candidate_write_does_not_block_cancel_or_dispose_and_never_commits_late(bool dispose)
    {
        using var workspace = new TemporaryWorkspace();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var callbacks = 0;
        using var recovery = new AuthoringRecoveryCheckpointService(action => action(), _ => callbacks++, TimeSpan.Zero,
            (path, document) =>
            {
                started.SetResult();
                release.Wait();
                new AuthoringDocumentStore(workspace.Root).SaveAtPath(path, document);
                finished.SetResult();
            });
        recovery.Schedule(workspace.Root, AuthoringDocumentDto.FromDraft(Draft(), 1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (dispose) recovery.Dispose(); else recovery.Cancel(workspace.Root, "test");
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(200));
        release.Set();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        Assert.Equal(0, callbacks);
        Assert.False(File.Exists(new AuthoringDocumentStore(workspace.Root).GetRecoveryPath("test")));
    }

    [Fact]
    public async Task Replacement_checkpoint_commits_without_waiting_for_obsolete_slow_write()
    {
        using var workspace = new TemporaryWorkspace();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = Completion();
        using var release = new ManualResetEventSlim();
        using var recovery = new AuthoringRecoveryCheckpointService(action => action(), value => latest.TrySetResult(value), TimeSpan.Zero,
            (path, document) =>
            {
                if (document.Revision == 1) { started.SetResult(); release.Wait(); }
                new AuthoringDocumentStore(workspace.Root).SaveAtPath(path, document);
            });
        recovery.Schedule(workspace.Root, AuthoringDocumentDto.FromDraft(Draft(), 1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        recovery.Cancel(workspace.Root, "test");
        recovery.Schedule(workspace.Root, AuthoringDocumentDto.FromDraft(Draft(), 2));
        Assert.Equal(2, (await latest.Task.WaitAsync(TimeSpan.FromSeconds(5))).Revision);
        release.Set();
        await Task.Delay(50);
        var store = new AuthoringDocumentStore(workspace.Root);
        Assert.Equal(2, store.LoadAtPath(store.GetRecoveryPath("test")).Document!.Revision);
    }

    private static TaskCompletionSource<AuthoringRecoveryCheckpointResult> Completion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ProgramDraft Draft() => new("test", new ProgramSidecar(), [], [], [], new CleanupPolicy(false, []));

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "authoring-recovery-" + Guid.NewGuid().ToString("N"));
        public TemporaryWorkspace() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
