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
    private readonly HashSet<Task> writers = [];

    public Task StopAsync() => DrainStoppedWritersAsync().WaitAsync(TimeSpan.FromSeconds(5));

    internal Task DrainStoppedWritersAsync()
    {
        Task[] owned;
        lock (gate) { disposed = true; CancelAllCore(); owned = writers.ToArray(); }
        return Task.WhenAll(owned);
    }

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
            var writer = Task.Run(() => SaveAfterDelayAsync(key, checkpoint));
            writers.Add(writer);
            _ = writer.ContinueWith(completedWriter => { lock (gate) writers.Remove(completedWriter); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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
            string? path = null;
            string? stagingDirectory = null;
            var candidateWritten = false;
            try
            {
                var store = new AuthoringDocumentStore(key.Root);
                path = store.GetRecoveryPath(key.PlanId);
                lock (gate) if (!IsCurrent(key, checkpoint)) return;
                // Slow durable writes use a private candidate. Only the final rename holds the session gate.
                stagingDirectory = Path.Combine(Path.GetDirectoryName(path)!, ".checkpoint-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingDirectory);
                var candidate = Path.Combine(stagingDirectory, Path.GetFileName(path));
                if (write is null) store.SaveAtPath(candidate, checkpoint.Document);
                else write(candidate, checkpoint.Document);
                candidateWritten = true;
                // Reads and schema/path checks remain outside the session gate. The gate protects
                // service generations; as with durable source saves, it cannot lock out external editors.
                store.ValidatePath(path);
                var backupPath = store.ValidatePath(path + ".bak");
                var existing = store.LoadAtPath(path);
                if (existing.IsReadOnly) throw new InvalidOperationException(existing.Error);
                var hadPrevious = File.Exists(path);
                lock (gate)
                {
                    if (!IsCurrent(key, checkpoint)) return;
                    if (hadPrevious) File.Move(path, backupPath, true);
                    try { File.Move(candidate, path, true); }
                    catch
                    {
                        if (hadPrevious) File.Move(backupPath, path, true);
                        throw;
                    }
                }
                result = new(key.Root, key.PlanId, checkpoint.Document.Revision, path, null);
            }
            catch (Exception error)
            {
                // A failed durable candidate retains a backup of the current previous checkpoint.
                // Prepare that backup outside the gate too; cancellation can discard it at any point.
                if (!candidateWritten && stagingDirectory is not null && path is not null)
                {
                    try
                    {
                        var store = new AuthoringDocumentStore(key.Root);
                        var backupPath = store.ValidatePath(path + ".bak");
                        if (!store.LoadAtPath(path).IsReadOnly && File.Exists(path))
                        {
                            var previous = Path.Combine(stagingDirectory, "previous");
                            File.Copy(store.ValidatePath(path), previous);
                            lock (gate)
                                if (IsCurrent(key, checkpoint)) File.Move(previous, backupPath, true);
                        }
                    }
                    catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or InvalidOperationException) { }
                }
                result = new(key.Root, key.PlanId, checkpoint.Document.Revision, path, error);
            }
            finally
            {
                if (stagingDirectory is not null) AuthoringBuildService.CleanupStaging(stagingDirectory);
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
