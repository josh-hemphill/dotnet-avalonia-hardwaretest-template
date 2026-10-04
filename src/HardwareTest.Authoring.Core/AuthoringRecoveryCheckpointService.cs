using System.Text.Json;

namespace HardwareTest.Authoring;

public sealed record AuthoringRecoveryCheckpointResult(
    string WorkspaceRoot, string PlanId, long Revision, string? Path, Exception? Error)
{
    public bool IsSuccess => Error is null;
}

/// Saves isolated editing snapshots without changing a document session's saved baseline.
public sealed class AuthoringRecoveryCheckpointService : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<(string Root, string PlanId), PendingCheckpoint> pending = [];
    private readonly Action<Action> dispatch;
    private readonly Action<AuthoringRecoveryCheckpointResult> completed;
    private readonly Action<string, AuthoringDocumentDto>? write;
    private readonly TimeSpan debounce;
    private bool disposed;

    public AuthoringRecoveryCheckpointService(Action<Action> dispatch,
        Action<AuthoringRecoveryCheckpointResult> completed, TimeSpan? debounce = null)
        : this(dispatch, completed, debounce, null) { }

    internal AuthoringRecoveryCheckpointService(Action<Action> dispatch,
        Action<AuthoringRecoveryCheckpointResult> completed, TimeSpan? debounce,
        Action<string, AuthoringDocumentDto>? write)
    {
        this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        this.completed = completed ?? throw new ArgumentNullException(nameof(completed));
        this.debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        if (this.debounce < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounce));
        this.write = write;
    }

    public void Schedule(string workspaceRoot, AuthoringDocumentDto document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != AuthoringDocumentDto.CurrentSchemaVersion)
            throw new InvalidDataException("Read-only authoring schemas cannot create recovery checkpoints.");
        // DTOs are mutable: capture every nested value before the delayed work starts.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, AuthoringDocumentJsonContext.Default.AuthoringDocumentDto);
        var snapshot = JsonSerializer.Deserialize(bytes, AuthoringDocumentJsonContext.Default.AuthoringDocumentDto)
            ?? throw new InvalidDataException("The recovery snapshot is empty.");
        var key = (Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot)), snapshot.PlanId);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (pending.Remove(key, out var previous)) previous.Cancel();
            var checkpoint = new PendingCheckpoint(snapshot);
            pending.Add(key, checkpoint);
            _ = Task.Run(() => SaveAfterDelayAsync(key, checkpoint));
        }
    }

    public void Cancel(string workspaceRoot, string planId)
    {
        lock (gate)
            if (pending.Remove((Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot)), planId), out var checkpoint))
                checkpoint.Cancel();
    }

    public void CancelAll()
    {
        lock (gate) CancelAllCore();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            CancelAllCore();
        }
    }

    private void CancelAllCore()
    {
        foreach (var checkpoint in pending.Values) checkpoint.Cancel();
        pending.Clear();
    }

    private async Task SaveAfterDelayAsync((string Root, string PlanId) key, PendingCheckpoint checkpoint)
    {
        try
        {
            await Task.Delay(debounce, checkpoint.Cancellation.Token).ConfigureAwait(false);
            AuthoringRecoveryCheckpointResult result;
            lock (gate)
            {
                if (!IsCurrent(key, checkpoint)) return;
                string? path = null;
                try
                {
                    var store = new AuthoringDocumentStore(key.Root);
                    path = store.GetRecoveryPath(key.PlanId);
                    if (write is null) store.SaveAtPath(path, checkpoint.Document);
                    else write(path, checkpoint.Document);
                    result = new(key.Root, key.PlanId, checkpoint.Document.Revision, path, null);
                }
                catch (Exception error)
                {
                    result = new(key.Root, key.PlanId, checkpoint.Document.Revision, path, error);
                }
            }
            dispatch(() =>
            {
                lock (gate)
                {
                    if (!IsCurrent(key, checkpoint)) return;
                    pending.Remove(key);
                    completed(result);
                }
            });
        }
        catch (OperationCanceledException) when (checkpoint.Cancellation.IsCancellationRequested) { }
        finally
        {
            lock (gate) checkpoint.DisposeCancellation();
        }
    }

    private bool IsCurrent((string Root, string PlanId) key, PendingCheckpoint checkpoint)
        => !disposed && pending.TryGetValue(key, out var current) && ReferenceEquals(current, checkpoint);

    private sealed class PendingCheckpoint(AuthoringDocumentDto document)
    {
        public AuthoringDocumentDto Document { get; } = document;
        public CancellationTokenSource Cancellation { get; } = new();
        private bool cancellationDisposed;
        public void Cancel()
        {
            if (!cancellationDisposed) Cancellation.Cancel();
        }
        public void DisposeCancellation()
        {
            cancellationDisposed = true;
            Cancellation.Dispose();
        }
    }
}
