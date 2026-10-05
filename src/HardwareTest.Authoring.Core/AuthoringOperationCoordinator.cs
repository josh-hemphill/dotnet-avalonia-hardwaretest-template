using System.Text.Json;

namespace HardwareTest.Authoring;

/// One process-owned OpenTAP lane with cancellation and workspace generation checks at publication.
public sealed class AuthoringOperationCoordinator(AuthoringChildProcessRunner runner) : IDisposable
{
    private static readonly SemaphoreSlim Lane = new(1, 1);
    private readonly object gate = new();
    private readonly Queue<AuthoringOperationLog> logs = new();
    private readonly SemaphoreSlim stopGate = new(1, 1);
    private CancellationTokenSource? active;
    private Task<AuthoringOperationResult>? running;
    private string? pendingCleanup;
    private long generation;
    private bool disposed;
    private long nextLogNotification;
    public event Action? LogsChanged;
    public bool IsBusy { get { lock (gate) return active is not null; } }
    public bool HasPendingCleanup { get { lock (gate) return pendingCleanup is not null; } }
    internal Func<string, Task> CancelledCleanup { get; init; } = owned => CleanupCancelledOperationAsync(owned);
    public IReadOnlyList<AuthoringOperationLog> Logs { get { lock (gate) return logs.ToArray(); } }
    public void Cancel() { lock (gate) active?.Cancel(); }
    public void ReplaceWorkspace() { lock (gate) { generation++; active?.Cancel(); logs.Clear(); } }
    public void Dispose() { lock (gate) { disposed = true; generation++; active?.Cancel(); } }
    public async Task StopAsync()
    {
        await stopGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Task<AuthoringOperationResult>? pending;
            lock (gate) { disposed = true; generation++; active?.Cancel(); pending = active is null ? null : running; }
            if (pending is not null)
            {
                try { await pending.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            string? owned;
            lock (gate) owned = pendingCleanup;
            if (owned is null) return;
            // A stopped operation can reach this path before any asynchronous await yields.
            // Directory removal must stay off the UI thread on repeated owner close too.
            await Task.Run(() => CancelledCleanup(owned)).ConfigureAwait(false);
            lock (gate) pendingCleanup = null;
        }
        finally { stopGate.Release(); }
    }

    public Task<AuthoringOperationResult> RunAsync(AuthoringOperationKind kind, string workspaceRoot,
        string? outputDirectory = null, string? home = null, bool offline = true,
        Action<AuthoringOperationProgress>? progress = null, CancellationToken cancellationToken = default, string? offlinePackagePath = null)
    {
        CancellationTokenSource operation;
        long capturedGeneration;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active is not null) throw new InvalidOperationException("An authoring operation is already running.");
            if (pendingCleanup is not null) throw new InvalidOperationException("Authoring operation cleanup must finish before starting another operation.");
            if (kind == AuthoringOperationKind.Pack) ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
            if (offlinePackagePath is not null && kind != AuthoringOperationKind.Bootstrap)
                throw new ArgumentException("Offline import requires environment preparation.", nameof(offlinePackagePath));
            active = operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            capturedGeneration = generation;
            logs.Clear();
            nextLogNotification = 0;
            // Reserve and retain the task together so accepted owner close can await all cleanup.
            running = Task.Run(() => RunOwnedAsync(kind, workspaceRoot, outputDirectory, home, offline, progress, operation, capturedGeneration, offlinePackagePath));
            return running;
        }
    }

    private async Task<AuthoringOperationResult> RunOwnedAsync(AuthoringOperationKind kind, string workspaceRoot,
        string? outputDirectory, string? home, bool offline, Action<AuthoringOperationProgress>? progress,
        CancellationTokenSource operation, long capturedGeneration, string? offlinePackagePath)
    {
        var owned = Path.Combine(Path.GetTempPath(), "authoring-operation-" + Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid().ToString("N");
        var token = operation.Token;
        var entered = false;
        try
        {
            Report(new("Waiting for authoring lane"));
            await Lane.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            var inheritedEnvironment = AuthoringBuildService.CaptureEnvironment();
            var request = new AuthoringChildRequest(1, id, kind, Path.GetFullPath(workspaceRoot), owned, string.IsNullOrWhiteSpace(home) ? null : Path.GetFullPath(home), offline);
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(owned);
            else Directory.CreateDirectory(owned, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (offlinePackagePath is not null)
            {
                if (!offlinePackagePath.EndsWith(".TapPackage", StringComparison.OrdinalIgnoreCase) && !offlinePackagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    throw new AuthoringWorkspaceException("Choose a .TapPackage or .zip offline package.");
                var bytes = await File.ReadAllBytesAsync(offlinePackagePath, token).ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(owned, "import.TapPackage"), bytes, token).ConfigureAwait(false);
                request = request with { OfflinePackageSha256 = AuthoringBuildService.Hash(bytes) };
            }
            var requestPath = Path.Combine(owned, "request.json");
            await File.WriteAllBytesAsync(requestPath, JsonSerializer.SerializeToUtf8Bytes(request,
                AuthoringOperationJsonContext.Default.AuthoringChildRequest), token).ConfigureAwait(false);
            Report(new("Starting child"));
            var exitCode = await runner.RunAsync(requestPath, log =>
            {
                lock (gate) if (!disposed && generation == capturedGeneration && !token.IsCancellationRequested) RetainLog(log);
            }, Report, token).ConfigureAwait(false);
            CheckCurrent();
            var resultPath = Path.Combine(owned, "result.json");
            var result = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(resultPath, token).ConfigureAwait(false),
                AuthoringOperationJsonContext.Default.AuthoringChildResult) ?? throw new InvalidDataException("Empty child result.");
            if (result.Version != 1 || result.Id != id || result.Kind != kind || result.OwnedRoot != owned)
                throw new InvalidDataException("Child result does not belong to this operation.");
            if (exitCode != 0 || result.Error is not null)
                throw new AuthoringWorkspaceException(result.Error ?? $"Authoring child exited with {exitCode}. See operation logs.");
            if (!AuthoringBuildService.EnvironmentIdentity(inheritedEnvironment).SequenceEqual(result.Environment))
                throw new AuthoringWorkspaceException("BUILD_ENVIRONMENT_CHANGED: Child and parent inherited environments differ.");
            var value = result.Result ?? throw new InvalidDataException("Child omitted operation result.");
            var selectedRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(home)
                ? Path.Combine(request.WorkspaceRoot, OpenTapHomeBootstrapper.DefaultHomeRelativePath) : home);
            if (value.Home != selectedRoot) throw new InvalidDataException("Child returned a different selected home.");
            if (kind == AuthoringOperationKind.Pack)
            {
                var snapshot = result.Snapshot ?? throw new InvalidDataException("Child omitted saved build identity.");
                if (snapshot.Root != request.WorkspaceRoot || snapshot.Home != Path.Combine(owned, "home")
                    || snapshot.TuiHome != snapshot.Home || value.Build is null || value.Build.Receipt.SchemaVersion != 1 || value.Validation is not null
                    || !snapshot.Trees.Any(tree => tree.StageRelativePath == "operation-home" && tree.Root == selectedRoot && !tree.Materialize && tree.Recursive))
                    throw new InvalidDataException("Child build identity does not match this workspace.");
                if (AuthoringBuildService.Hash(JsonSerializer.SerializeToUtf8Bytes(value.Build.Receipt,
                    AuthoringBuildJsonContext.Default.AuthoringBuildReceipt)) != result.ReceiptHash)
                    throw new InvalidDataException("Child result differs from its durable receipt.");
                var captured = snapshot.ToRequest();
                if (!captured.Inputs.SequenceEqual(value.Build.Receipt.Inputs) || !captured.Environment.SequenceEqual(value.Build.Receipt.Environment))
                    throw new InvalidDataException("Child snapshot identity differs from its durable receipt.");
                using var prepared = new AuthoringPreparedBuild(captured, Path.Combine(owned, "prepared"), value.Build);
                if (prepared.ReceiptSha256 != result.ReceiptHash) throw new InvalidDataException("Child receipt changed before publication.");
                Report(new("Publishing checked artifacts"));
                // Synchronous publication is off the UI thread. Generation invalidation cancels its transactional token.
                await Task.Run(() => { CheckCurrent(); AuthoringBuildService.Publish(prepared, outputDirectory!, token); }, token).ConfigureAwait(false);
            }
            else if (kind == AuthoringOperationKind.Validate)
            {
                if (value.Validation is null || value.Build is not null || result.Snapshot is not null)
                    throw new InvalidDataException("Child returned invalid validation output.");
            }
            else if (value.Validation is not null || value.Build is not null || result.Snapshot is not null)
                throw new InvalidDataException("Child returned invalid bootstrap output.");
            if (kind == AuthoringOperationKind.Bootstrap)
            {
                Report(new("Publishing prepared home"));
                var selected = result.SelectedHome ?? throw new InvalidDataException("Child omitted home identity.");
                var expectedHome = Path.GetFullPath(string.IsNullOrWhiteSpace(home)
                    ? Path.Combine(request.WorkspaceRoot, OpenTapHomeBootstrapper.DefaultHomeRelativePath) : home);
                if (selected.Root != expectedHome || value.Home != expectedHome || selected.StageRelativePath != "operation-home"
                    || selected.Materialize || !selected.Recursive)
                    throw new InvalidDataException("Child home does not match selected destination.");
                var manifest = result.ManifestIdentity ?? throw new InvalidDataException("Child omitted workspace identity.");
                if (manifest.Root != request.WorkspaceRoot || manifest.StageRelativePath != "workspace")
                    throw new InvalidDataException("Child workspace identity does not match this request.");
                var identity = new AuthoringBuildRequest(request.WorkspaceRoot, new PackOptions(), [selected, manifest], inheritedEnvironment);
                var workspaceIdentity = new AuthoringBuildRequest(request.WorkspaceRoot, new PackOptions(), [manifest], inheritedEnvironment);
                await Task.Run(() =>
                {
                    CheckCurrent();
                    AuthoringBuildService.Recheck(identity);
                    AuthoringBuildService.Publish(Path.Combine(owned, "home"), expectedHome, token,
                        validate: () => { CheckCurrent(); AuthoringBuildService.Recheck(workspaceIdentity); });
                }, token).ConfigureAwait(false);
            }
            CheckCurrent();
            return value;
        }
        finally
        {
            if (entered) Lane.Release();
            try
            {
                if (token.IsCancellationRequested)
                {
                    try { await CancelledCleanup(owned).ConfigureAwait(false); }
                    catch
                    {
                        lock (gate) pendingCleanup = owned;
                        throw;
                    }
                }
                else AuthoringBuildService.CleanupStaging(owned);
            }
            finally
            {
                lock (gate) { if (ReferenceEquals(active, operation)) active = null; operation.Dispose(); }
            }
        }
        void Report(AuthoringOperationProgress update)
        {
            lock (gate)
                if (!disposed && generation == capturedGeneration && !token.IsCancellationRequested) progress?.Invoke(update);
        }
        void CheckCurrent()
        {
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (disposed || capturedGeneration != generation) throw new OperationCanceledException("Workspace changed.", token);
            }
        }
    }

    internal static async Task CleanupCancelledOperationAsync(string owned, Action<string>? delete = null, TimeSpan? timeout = null)
    {
        delete ??= path =>
        {
            try { Directory.Delete(path, recursive: true); }
            catch (DirectoryNotFoundException) { }
        };
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        while (true)
        {
            try { delete(owned); return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Reaping can precede the last file handle release. Owner close must await removal,
                // and a persistent failure must reach its error handler rather than report success.
                if (elapsed.Elapsed >= limit) throw;
                await Task.Delay(20).ConfigureAwait(false);
            }
        }
    }

    private void RetainLog(AuthoringOperationLog log)
    {
        lock (gate)
        {
            logs.Enqueue(log with { Text = log.Text.Length > 1024 ? log.Text[..1024] : log.Text });
            while (logs.Count > 128) logs.Dequeue();
            var now = Environment.TickCount64;
            if (now >= nextLogNotification)
            {
                nextLogNotification = now + 250;
                LogsChanged?.Invoke();
            }
        }
    }
}
