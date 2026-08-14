using System.ComponentModel;
using System.Runtime.CompilerServices;
using HardwareTest.Core.Crash;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
using HardwareTest.OpenTap.Plugins.Basic;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host.Worker;

/// UI-process <see cref="IOpenTapSession"/> façade over the killable OpenTAP worker.
public sealed class OpenTapWorkerClient : IOpenTapSession, INotifyPropertyChanged, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly ISafetyController _safety;
    private readonly IBenchOperationCoordinator? _bench;
    private readonly CrashDossierWriter? _crashWriter;
    private readonly BuildInfo? _buildInfo;
    private readonly IClock _clock;
    private readonly TimeSpan _killTimeout;
    private readonly OpenTapWorkerProcess _process;
    private List<OpenTapStepNode> _stepTree = [];
    private List<OpenTapInstrumentSlot> _slots = [];
    private bool _isExecuting;
    private bool _awaitingOperator;
    private string? _operatorPrompt;
    private OperatorInteractionRequest? _pendingInteraction;
    private CancellationTokenSource? _killCts;
    private bool _workerUseMockVisa;
    private int _disposed;

    public OpenTapWorkerClient(
        AppSettings settings,
        ILogger? logger = null,
        ISafetyController? safety = null,
        IBenchOperationCoordinator? bench = null,
        CrashDossierWriter? crashWriter = null,
        BuildInfo? buildInfo = null,
        TimeSpan? killTimeout = null,
        string? executablePath = null,
        IClock? clock = null)
    {
        _settings = settings;
        _logger = logger ?? Log.ForContext<OpenTapWorkerClient>();
        _safety = safety ?? new NoOpSafetyController();
        _bench = bench;
        _crashWriter = crashWriter;
        _buildInfo = buildInfo;
        _clock = clock ?? SystemClock.Instance;
        _killTimeout = killTimeout is { } overrideTimeout && overrideTimeout > TimeSpan.Zero
            ? overrideTimeout
            : TimeSpan.FromMilliseconds(Math.Clamp(
                settings.OpenTapWorkerKillTimeoutMilliseconds,
                AppSettings.MinOpenTapWorkerKillTimeoutMilliseconds,
                AppSettings.MaxOpenTapWorkerKillTimeoutMilliseconds));
        _process = new OpenTapWorkerProcess(_logger, executablePath);
    }

    public string? LoadedPlanPath { get; private set; }
    public string? LoadedPlanName { get; private set; }
    public IReadOnlyList<OpenTapStepNode> StepTree => _stepTree;
    public IReadOnlyList<OpenTapInstrumentSlot> InstrumentSlots => _slots;
    public bool IsExecuting => _isExecuting;
    public bool IsAwaitingOperator => _awaitingOperator;
    public string? OperatorPromptMessage => _operatorPrompt;
    public OperatorInteractionRequest? PendingInteraction => _pendingInteraction;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task LoadPlanAsync(string tapPlanPath, CancellationToken cancellationToken = default)
        => MutateAsync(
            WorkerProtocol.LoadPlan,
            new WorkerPathRequest { Path = tapPlanPath },
            WorkerJsonContext.Default.WorkerPathRequest,
            cancellationToken);

    public Task LoadSampleProgramAsync(CancellationToken cancellationToken = default)
        => MutateAsync(WorkerProtocol.LoadSample, cancellationToken);

    public Task LoadBoardDemoProgramAsync(CancellationToken cancellationToken = default)
        => MutateAsync(WorkerProtocol.LoadBoardDemo, cancellationToken);

    public Task LoadSweepDemoProgramAsync(CancellationToken cancellationToken = default)
        => MutateAsync(WorkerProtocol.LoadSweepDemo, cancellationToken);

    public Task LoadTimingDemoProgramAsync(CancellationToken cancellationToken = default)
        => MutateAsync(WorkerProtocol.LoadTimingDemo, cancellationToken);

    /// Test-only fixture load (not on <see cref="IOpenTapSession"/>).
    public Task LoadPlanShapeAsync(string fixtureFileName, CancellationToken cancellationToken = default)
        => MutateAsync(
            WorkerProtocol.LoadPlanShape,
            new WorkerFixtureRequest { FixtureFileName = fixtureFileName },
            WorkerJsonContext.Default.WorkerFixtureRequest,
            cancellationToken);

    public Task ApplyStationAndDutAsync(StationProfile station, DutIdentity dut, CancellationToken cancellationToken = default)
        => MutateAsync(
            WorkerProtocol.ApplyStationAndDut,
            new WorkerStationDutRequest
            {
                RoleToResource = new Dictionary<string, string>(station.RoleToResource, StringComparer.OrdinalIgnoreCase),
                Serial = dut.Serial,
                PartNumber = dut.PartNumber,
                Revision = dut.Revision,
                Family = dut.Family,
            },
            WorkerJsonContext.Default.WorkerStationDutRequest,
            cancellationToken);

    public Task<OpenTapRunSummary> RunAsync(
        IProgress<OpenTapProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? runId = null)
        => RunCoreAsync(
            WorkerProtocol.Run,
            new WorkerRunRequest { RunId = runId },
            WorkerJsonContext.Default.WorkerRunRequest,
            progress,
            cancellationToken);

    public Task<OpenTapRunSummary> RunSelectionAsync(
        string stepPath,
        IProgress<OpenTapProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? runId = null,
        bool includeCleanup = true)
        => RunCoreAsync(
            WorkerProtocol.RunSelection,
            new WorkerRunSelectionRequest
            {
                StepPath = stepPath,
                IncludeCleanup = includeCleanup,
                RunId = runId,
            },
            WorkerJsonContext.Default.WorkerRunSelectionRequest,
            progress,
            cancellationToken);

    public void Pause()
    {
        try
        {
            EnsureStarted();
            var envelope = _process.Request(
                    WorkerProtocol.Pause,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ApplySnapshotEnvelope(envelope);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP worker pause failed.");
        }
    }

    public void Resume(OperatorInteractionResponse? response = null)
    {
        try
        {
            EnsureStarted();
            var envelope = _process.Request(
                    WorkerProtocol.Resume,
                    new WorkerResumeRequest { Response = response },
                    WorkerJsonContext.Default.WorkerResumeRequest,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ApplySnapshotEnvelope(envelope);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP worker resume failed.");
        }
    }

    public void Abort(bool safetyStop = false)
    {
        try
        {
            if (_process.IsAlive)
            {
                _ = _process.Request(
                    WorkerProtocol.Abort,
                    new WorkerAbortRequest { SafetyStop = safetyStop },
                    WorkerJsonContext.Default.WorkerAbortRequest,
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP worker cooperative abort failed.");
        }

        ArmKillTimer();
    }

    public bool TrySetStepEnabled(string stepPath, bool enabled)
        => TryBool(
            WorkerProtocol.TrySetStepEnabled,
            new WorkerSetEnabledRequest { StepPath = stepPath, Enabled = enabled },
            WorkerJsonContext.Default.WorkerSetEnabledRequest);

    public bool TryGetStepConditionSummary(string stepPath, out string? summary)
    {
        var result = TryBoolResult(
            WorkerProtocol.TryGetStepConditionSummary,
            new WorkerPathRequest { Path = stepPath },
            WorkerJsonContext.Default.WorkerPathRequest);
        summary = result?.Value;
        return result?.Ok == true;
    }

    public bool TrySetAcquireSettings(string stepPath, int? sampleCount, int? intervalMs)
        => TryBool(
            WorkerProtocol.TrySetAcquireSettings,
            new WorkerAcquireSettingsRequest
            {
                StepPath = stepPath,
                SampleCount = sampleCount,
                IntervalMs = intervalMs,
            },
            WorkerJsonContext.Default.WorkerAcquireSettingsRequest);

    public bool TrySetMeanGteThreshold(string stepPath, double threshold)
        => TryBool(
            WorkerProtocol.TrySetMeanGteThreshold,
            new WorkerMeanGteRequest { StepPath = stepPath, Threshold = threshold },
            WorkerJsonContext.Default.WorkerMeanGteRequest);

    public bool TryRebindDmmResource(string resource)
        => TryBool(
            WorkerProtocol.TryRebindDmmResource,
            new WorkerResourceRequest { Resource = resource },
            WorkerJsonContext.Default.WorkerResourceRequest);

    public bool TryBindSlotResource(string slotName, string resource)
        => TryBool(
            WorkerProtocol.TryBindSlotResource,
            new WorkerBindSlotRequest { SlotName = slotName, Resource = resource },
            WorkerJsonContext.Default.WorkerBindSlotRequest);

    public IReadOnlyList<OpenTapParameterInfo> EnumerateParameters(
        OpenTapParameterScope scope,
        string? stepPath = null,
        bool includeReadOnly = false,
        OpenTapParameterListing listing = OpenTapParameterListing.StationOverrides)
    {
        try
        {
            EnsureStarted();
            var envelope = _process.Request(
                    WorkerProtocol.EnumerateParameters,
                    new WorkerEnumerateParametersRequest
                    {
                        Scope = scope,
                        StepPath = stepPath,
                        IncludeReadOnly = includeReadOnly,
                        Listing = listing,
                    },
                    WorkerJsonContext.Default.WorkerEnumerateParametersRequest,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ThrowIfFailed(envelope);
            return WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerParameterListResult)?.Items
                   ?? [];
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP worker enumerate parameters failed.");
            return [];
        }
    }

    public bool TryGetParameter(string memberKey, out string? value)
    {
        var result = TryBoolResult(
            WorkerProtocol.TryGetParameter,
            new WorkerMemberKeyRequest { MemberKey = memberKey },
            WorkerJsonContext.Default.WorkerMemberKeyRequest);
        value = result?.Value;
        return result?.Ok == true;
    }

    public bool TrySetParameter(string memberKey, string value)
        => TryBool(
            WorkerProtocol.TrySetParameter,
            new WorkerMemberKeyRequest { MemberKey = memberKey, Value = value },
            WorkerJsonContext.Default.WorkerMemberKeyRequest);

    public IReadOnlyList<OpenTapPluginDirectoryInfo> ListPluginDirectories()
        => List(
            WorkerProtocol.ListPluginDirectories,
            WorkerJsonContext.Default.WorkerPluginDirectoryListResult,
            r => r.Items);

    public IReadOnlyList<OpenTapPackageInfo> ListInstalledPackages()
        => List(
            WorkerProtocol.ListInstalledPackages,
            WorkerJsonContext.Default.WorkerPackageListResult,
            r => r.Items);

    public IReadOnlyList<OpenTapDiscoveredAddress> ListDiscoveredDeviceAddresses()
        => List(
            WorkerProtocol.ListDiscoveredDeviceAddresses,
            WorkerJsonContext.Default.WorkerDiscoveredAddressListResult,
            r => r.Items);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        DisarmKillTimer();
        _process.Dispose();
    }

    private async Task MutateAsync(string method, CancellationToken cancellationToken)
    {
        EnsureStarted();
        var envelope = await _process.Request(method, cancellationToken).ConfigureAwait(false);
        ThrowIfFailed(envelope);
        ApplySnapshotEnvelope(envelope);
    }

    private async Task MutateAsync<T>(
        string method,
        T payload,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        EnsureStarted();
        var envelope = await _process.Request(method, payload, typeInfo, cancellationToken).ConfigureAwait(false);
        ThrowIfFailed(envelope);
        ApplySnapshotEnvelope(envelope);
    }

    private async Task<OpenTapRunSummary> RunCoreAsync<T>(
        string method,
        T payload,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        IProgress<OpenTapProgress>? progress,
        CancellationToken cancellationToken)
    {
        IDisposable? benchLease = null;
        if (_bench is not null && !_bench.TryEnter(BenchOperation.Run, out benchLease, out var busy))
        {
            throw new InvalidOperationException(busy);
        }

        EnsureStarted();
        _isExecuting = true;
        Raise(nameof(IsExecuting));
        try
        {
            // Do not pass the caller token into the IPC wait. RequestSafetyStop cancels that
            // token, but the worker run only stops via Abort (it uses CancellationToken.None).
            // Cancelling the wait would throw, skip DisarmKillTimer, and leave a kill timer
            // that can tear down a later run.
            var requestTask = _process.Request(
                    method,
                    payload,
                    typeInfo,
                    CancellationToken.None,
                    onEvent: ev => HandleRunEvent(ev, progress));
            using (cancellationToken.Register(() => Abort(safetyStop: false)))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Abort(safetyStop: false);
                }

                var envelope = await requestTask.ConfigureAwait(false);
                ThrowIfFailed(envelope);
                var result = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerRunResult)
                             ?? throw new InvalidOperationException("Worker run response was empty.");
                if (result.Snapshot is not null)
                {
                    ApplySnapshot(result.Snapshot);
                }

                return result.Summary;
            }
        }
        catch (Exception ex) when (!_process.IsAlive || ex is OpenTapWorkerProcessException)
        {
            HandleWorkerDeath(ex);
            return CancelledSummary("OpenTAP worker was terminated.");
        }
        finally
        {
            DisarmKillTimer();
            if (_isExecuting)
            {
                _isExecuting = false;
                Raise(nameof(IsExecuting));
            }

            benchLease?.Dispose();
        }
    }

    private void HandleRunEvent(WorkerEnvelope envelope, IProgress<OpenTapProgress>? progress)
    {
        if (!string.Equals(envelope.Method, WorkerProtocol.Progress, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var reported = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.OpenTapProgress);
        if (reported is null)
        {
            return;
        }

        ApplyFromProgress(reported);
        progress?.Report(reported);
    }

    private void ApplyFromProgress(OpenTapProgress progress)
    {
        _awaitingOperator = progress.AwaitingOperator;
        _operatorPrompt = progress.OperatorPromptMessage;
        if (progress.InteractionRequest is not null)
        {
            _pendingInteraction = progress.InteractionRequest;
        }
        else if (!progress.AwaitingOperator)
        {
            _pendingInteraction = null;
        }

        Raise(nameof(IsAwaitingOperator));
        Raise(nameof(OperatorPromptMessage));
        Raise(nameof(PendingInteraction));
    }

    private bool TryBool<T>(string method, T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => TryBoolResult(method, payload, typeInfo)?.Ok == true;

    private WorkerBoolResult? TryBoolResult<T>(
        string method,
        T payload,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        try
        {
            EnsureStarted();
            var envelope = _process.Request(method, payload, typeInfo, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ThrowIfFailed(envelope);
            var result = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerBoolResult);
            if (result?.Snapshot is not null)
            {
                ApplySnapshot(result.Snapshot);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP worker {Method} failed.", method);
            return null;
        }
    }

    private IReadOnlyList<TItem> List<TResult, TItem>(
        string method,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> typeInfo,
        Func<TResult, List<TItem>?> select)
    {
        try
        {
            EnsureStarted();
            var envelope = _process.Request(method, CancellationToken.None).GetAwaiter().GetResult();
            ThrowIfFailed(envelope);
            var result = WorkerProtocol.ReadPayload(envelope, typeInfo);
            return result is null ? [] : select(result) ?? [];
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP worker {Method} failed.", method);
            return [];
        }
    }

    private void EnsureStarted()
    {
        if (_process.IsAlive
            && _workerUseMockVisa == _settings.UseMockVisa)
        {
            return;
        }

        if (_process.IsAlive && !_isExecuting)
        {
            _process.Stop(writeDossier: false);
        }

        _process.EnsureStarted(_settings);
        _workerUseMockVisa = _settings.UseMockVisa;
    }

    private void ApplySnapshotEnvelope(WorkerEnvelope envelope)
    {
        var snapshot = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerSnapshot);
        if (snapshot is not null)
        {
            ApplySnapshot(snapshot);
        }
    }

    private void ApplySnapshot(WorkerSnapshot snapshot)
    {
        LoadedPlanPath = snapshot.LoadedPlanPath;
        LoadedPlanName = snapshot.LoadedPlanName;
        _stepTree = snapshot.StepTree;
        _slots = snapshot.InstrumentSlots;
        _isExecuting = snapshot.IsExecuting;
        _awaitingOperator = snapshot.IsAwaitingOperator;
        _operatorPrompt = snapshot.OperatorPromptMessage;
        _pendingInteraction = snapshot.PendingInteraction;
        Raise(nameof(LoadedPlanPath));
        Raise(nameof(LoadedPlanName));
        Raise(nameof(StepTree));
        Raise(nameof(InstrumentSlots));
        Raise(nameof(IsExecuting));
        Raise(nameof(IsAwaitingOperator));
        Raise(nameof(OperatorPromptMessage));
        Raise(nameof(PendingInteraction));
    }

    private void ArmKillTimer()
    {
        DisarmKillTimer();
        var cts = new CancellationTokenSource();
        _killCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_killTimeout, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!ReferenceEquals(_killCts, cts))
            {
                return;
            }

            KillWorkerAfterTimeout();
        });
    }

    private void DisarmKillTimer()
    {
        var cts = Interlocked.Exchange(ref _killCts, null);
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }
    }

    private void KillWorkerAfterTimeout()
    {
        try
        {
            _safety.SafeIdle();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "ISafetyController.SafeIdle failed before worker kill.");
        }

        var stderr = _process.StderrTail;
        _process.KillTree();
        var exit = _process.ExitCode;
        WriteWorkerDossier(exit, stderr, new TimeoutException("OpenTAP worker kill timeout."));
        _process.Stop(writeDossier: false);
        ApplyDeadSnapshot();
    }

    private void HandleWorkerDeath(Exception ex)
    {
        try
        {
            _safety.SafeIdle();
        }
        catch (Exception idleEx)
        {
            _logger.Warning(idleEx, "ISafetyController.SafeIdle failed after worker death.");
        }

        WriteWorkerDossier(_process.ExitCode, _process.StderrTail, ex);
        _process.Stop(writeDossier: false);
        ApplyDeadSnapshot();
    }

    private void ApplyDeadSnapshot()
    {
        _isExecuting = false;
        _awaitingOperator = false;
        _operatorPrompt = null;
        _pendingInteraction = null;
        Raise(nameof(IsExecuting));
        Raise(nameof(IsAwaitingOperator));
        Raise(nameof(OperatorPromptMessage));
        Raise(nameof(PendingInteraction));
    }

    private void WriteWorkerDossier(int? exitCode, string stderr, Exception? exception)
    {
        if (_crashWriter is null || !_settings.CrashEnabled)
        {
            return;
        }

        try
        {
            var report = CrashDossierWriter.BuildReport(
                exception,
                isFatal: false,
                source: "opentap-worker",
                SafeStopOutcome.Confirmed,
                _buildInfo,
                activeRunId: null,
                activePlanId: LoadedPlanPath,
                redact: _settings.RedactIdentifiersInDiagnostics);
            report.WorkerExitCode = exitCode;
            report.WorkerStdErrTail = string.IsNullOrWhiteSpace(stderr) ? null : stderr;
            _crashWriter.TryWrite(new CrashCaptureContext
            {
                Report = report,
                LogTail = CrashDossierWriter.CaptureLogTail() + Environment.NewLine + stderr,
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to write OpenTAP worker crash dossier.");
        }
    }

    private OpenTapRunSummary CancelledSummary(string message)
        => new()
        {
            RunId = string.Empty,
            PlanName = string.Empty,
            Result = RunResult.Cancelled,
            ErrorMessage = message,
            StartedAt = _clock.UtcNow,
            CompletedAt = _clock.UtcNow,
            Verdict = "Aborted",
        };

    private static void ThrowIfFailed(WorkerEnvelope envelope)
    {
        if (!envelope.Ok)
        {
            throw new InvalidOperationException(envelope.Error ?? "OpenTAP worker request failed.");
        }
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
