using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using HardwareTest.Core.IO;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host;

public sealed record DutIdentity(string Serial, string? PartNumber = null, string? Revision = null, string Family = "generic");

public sealed record StationProfile(IReadOnlyDictionary<string, string> RoleToResource);

public sealed class OpenTapInstrumentSlot
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required string RoleHint { get; init; }
    public string ResourceName { get; set; } = string.Empty;
}

public sealed class OpenTapProgress
{
    public required string Message { get; init; }
    public string? StepName { get; init; }
    public string? StepPath { get; init; }
    public string? StepId { get; init; }
    public string? Verdict { get; init; }
    public string? StatusText { get; init; }
    public string? KeyValue { get; init; }
    public double OverallPercent { get; init; }
    public bool IsCompleted { get; init; }
    public bool AwaitingOperator { get; init; }
    public string? OperatorPromptMessage { get; init; }
    public OperatorInteractionRequest? InteractionRequest { get; init; }
    public RunResult? Result { get; init; }
    public MeasurementSampleEvent? Sample { get; init; }
    /// 1-based iteration index for innermost active Repeat/Sweep loop.
    public int? IterationIndex { get; init; }
    public int? IterationTotal { get; init; }
    /// Convenience text such as "3/5" or "#3".
    public string? IterationText { get; init; }
}

public sealed record MeasurementSampleEvent(
    string Channel,
    int Index,
    double Value,
    DateTimeOffset Timestamp,
    string? MetricKey = null,
    string? DisplayRole = null,
    string? Unit = null,
    double? LimitLow = null,
    double? LimitHigh = null)
{
    /// Builds a live event from a normalized stored sample.
    public static MeasurementSampleEvent FromStored(StoredSample sample, int index = 0) => new(
        sample.Channel,
        index,
        sample.Value,
        sample.Timestamp,
        string.IsNullOrWhiteSpace(sample.MetricKey) ? null : sample.MetricKey,
        sample.DisplayRole,
        sample.Unit,
        sample.LimitLow,
        sample.LimitHigh);

    /// Metric grouping key for tiles/charts.
    public string EffectiveMetricKey
        => string.IsNullOrWhiteSpace(MetricKey) ? Channel : MetricKey!;
}

public sealed class OpenTapRunSummary
{
    public required string RunId { get; init; }
    public required string PlanName { get; init; }
    public required RunResult Result { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DutSerial { get; init; }
    public string? DutPartNumber { get; init; }
    public string? DutRevision { get; init; }
    public string? SessionId { get; init; }
    public string? OperatorName { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public List<StoredSample> Samples { get; init; } = [];
    public List<StepResultRecord> Steps { get; init; } = [];
    public string Verdict { get; init; } = "NotSet";
}

/// Aggregating OpenTAP session façade (plan + run + station + catalog).
/// Prefer injecting a focused surface (<see cref="IOpenTapPlanSession"/>, <see cref="IOpenTapRunSession"/>,
/// <see cref="IOpenTapStationSession"/>, or <see cref="IOpenTapHostCatalog"/>) from Feature ViewModels.
/// Kept for Phase 8 contract suites and Composition until OpenTAP Phase K no longer needs the aggregate.
public interface IOpenTapSession :
    IOpenTapPlanSession,
    IOpenTapRunSession,
    IOpenTapStationSession,
    IOpenTapHostCatalog
{
}

public sealed class OpenTapStepNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool Enabled { get; set; } = true;
    public string Verdict { get; set; } = "NotSet";
    public string StatusText { get; set; } = "Pending";
    public string? KeyValue { get; set; }
    public bool IsStage { get; set; }
    public List<OpenTapStepNode> Children { get; init; } = [];
}

/// OpenTAP-backed run session used by the Avalonia shell.
public sealed class OpenTapSession : IOpenTapSession, INotifyPropertyChanged
{
    private readonly ILogger _logger;
    private readonly AppSettings _settings;
    private readonly object _sync = new();
    private TestPlan? _plan;
    private readonly List<Instrument> _instruments = [];
    private HardwareDut? _dut;
    private DutIdentity? _dutIdentity;
    private CancellationTokenSource? _runCts;
    private bool _paused;
    private ManualResetEventSlim _pauseGate = new(true);
    private List<OpenTapStepNode> _stepTree = [];
    private List<OpenTapInstrumentSlot> _slots = [];
    private readonly List<StoredSample> _samples = [];
    private readonly List<StepResultRecord> _steps = [];
    private readonly Dictionary<string, DateTimeOffset> _stepStarted = new(StringComparer.OrdinalIgnoreCase);
    private bool _pluginSearchDone;
    private bool _awaitingOperator;
    private string? _operatorPromptMessage;
    private OperatorInteractionRequest? _pendingInteraction;
    private OperatorInteractionResponse? _interactionResponse;
    private readonly ManualResetEventSlim _interactionGate = new(false);
    /// 0 = idle, 1 = a run holds the single-flight gate.
    private int _runGate;

    public OpenTapSession(AppSettings? settings = null, ILogger? logger = null)
    {
        _settings = settings ?? new AppSettings();
        _logger = logger ?? Serilog.Log.ForContext<OpenTapSession>();
    }

    public string? LoadedPlanPath { get; private set; }
    public string? LoadedPlanName { get; private set; }
    public IReadOnlyList<OpenTapStepNode> StepTree => _stepTree;
    public IReadOnlyList<OpenTapInstrumentSlot> InstrumentSlots => _slots;
    public bool IsExecuting => Volatile.Read(ref _runGate) != 0;
    public bool IsAwaitingOperator => _awaitingOperator;
    public string? OperatorPromptMessage => _operatorPromptMessage;
    public OperatorInteractionRequest? PendingInteraction => _pendingInteraction;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task LoadSampleProgramAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePlugins();
        var plan = SampleProgramFactory.Create();
        BindPlan(plan, SampleProgramFactory.EmbeddedName, "Sample Hardware Suite (Demo)");
        return Task.CompletedTask;
    }

    public Task LoadBoardDemoProgramAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePlugins();
        var plan = BoardDemoProgramFactory.Create();
        BindPlan(plan, BoardDemoProgramFactory.EmbeddedName, BoardDemoProgramFactory.DisplayName);
        return Task.CompletedTask;
    }

    public Task LoadSweepDemoProgramAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePlugins();
        var plan = SweepDemoProgramFactory.Create();
        BindPlan(plan, SweepDemoProgramFactory.EmbeddedName, SweepDemoProgramFactory.DisplayName);
        return Task.CompletedTask;
    }

    public Task LoadTimingDemoProgramAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePlugins();
        var plan = TimingDemoProgramFactory.Create();
        BindPlan(plan, TimingDemoProgramFactory.EmbeddedName, TimingDemoProgramFactory.DisplayName);
        return Task.CompletedTask;
    }

    public Task LoadPlanShapeAsync(string fixtureFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePlugins();
        var match = PlanShapeFixtures.All.FirstOrDefault(f =>
            string.Equals(f.FileName, fixtureFileName, StringComparison.OrdinalIgnoreCase));
        if (match.Create is null)
        {
            throw new ArgumentException($"Unknown plan-shape fixture '{fixtureFileName}'.", nameof(fixtureFileName));
        }

        var display = Path.GetFileNameWithoutExtension(fixtureFileName);
        BindPlan(match.Create(), fixtureFileName, display);
        return Task.CompletedTask;
    }

    public Task LoadPlanAsync(string tapPlanPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePlugins();
        if (!File.Exists(tapPlanPath))
        {
            throw new FileNotFoundException("Test plan not found.", tapPlanPath);
        }

        var plan = TestPlan.Load(tapPlanPath);
        BindPlan(plan, tapPlanPath, string.IsNullOrWhiteSpace(plan.Name) ? Path.GetFileNameWithoutExtension(tapPlanPath) : plan.Name);
        return Task.CompletedTask;
    }

    private void BindPlan(TestPlan plan, string path, string displayName)
    {
        lock (_sync)
        {
            ThrowIfExecuting("load or bind a plan");
            _plan = plan;
            _instruments.Clear();
            foreach (var instr in InstrumentResourceAccess.CollectFromPlan(plan))
            {
                _instruments.Add(instr);
            }

            _dut = FindDut(plan);
            LoadedPlanPath = path;
            LoadedPlanName = displayName;
            _stepTree = BuildTree(plan);
            _slots = InstrumentSlotCollector.FromPlan(plan).ToList();
        }

        Raise(nameof(LoadedPlanPath));
        Raise(nameof(LoadedPlanName));
        Raise(nameof(StepTree));
        Raise(nameof(InstrumentSlots));
    }

    public Task ApplyStationAndDutAsync(StationProfile station, DutIdentity dut, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfExecuting("apply station or DUT bindings");
            _dutIdentity = dut;
            if (_dut is not null)
            {
                _dut.SerialNumber = dut.Serial;
                _dut.PartNumber = dut.PartNumber ?? string.Empty;
                _dut.Revision = dut.Revision ?? string.Empty;
                _dut.Family = dut.Family;
            }

            foreach (var slot in _slots)
            {
                if ((station.RoleToResource.TryGetValue(slot.RoleHint, out var resource)
                     || station.RoleToResource.TryGetValue(slot.Name, out resource))
                    && !string.IsNullOrWhiteSpace(resource))
                {
                    TryBindSlotResource_NoLock(slot.Name, resource);
                }
            }

            // Legacy: any "dmm" binding applies to first instrument if role hint missed.
            if (station.RoleToResource.TryGetValue("dmm", out var dmm)
                && !string.IsNullOrWhiteSpace(dmm)
                && _instruments.Count > 0)
            {
                TryBindSlotResource_NoLock(_slots.FirstOrDefault()?.Name ?? _instruments[0].Name, dmm);
            }
        }

        Raise(nameof(InstrumentSlots));
        return Task.CompletedTask;
    }

    public async Task<OpenTapRunSummary> RunSelectionAsync(
        string stepPath,
        IProgress<OpenTapProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? runId = null,
        bool includeCleanup = true)
    {
        if (_plan is null)
        {
            throw new InvalidOperationException("Load a plan before running.");
        }

        var selected = FindStepByPath(stepPath)
                       ?? throw new InvalidOperationException($"Step not found: {stepPath}");
        var mask = FlattenSteps(_plan).ToDictionary(s => s.Id, s => s.Enabled);

        // Reset only steps that will execute (subtree + optional SafeShutdown). Ancestors stay enabled for
        // OpenTAP structure but keep prior live status; rollup refreshes them afterward.
        var resetStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sampleScopePaths = new List<string>();
        foreach (var step in FlattenSteps(_plan))
        {
            var isCleanup = includeCleanup && step is SafeShutdownStep;
            if (!IsInSubtree(step, selected) && !isCleanup)
            {
                continue;
            }

            resetStepIds.Add(step.Id.ToString());
            var node = FindNode(_stepTree, step.Id.ToString());
            if (node is not null && !string.IsNullOrWhiteSpace(node.Path))
            {
                sampleScopePaths.Add(node.Path);
            }
        }

        try
        {
            var cleanupSteps = includeCleanup
                ? FlattenSteps(_plan).Where(s => s is SafeShutdownStep).ToList()
                : [];
            foreach (var step in FlattenSteps(_plan))
            {
                var keep = IsInSubtree(step, selected)
                           || IsAncestorOf(step, selected)
                           || cleanupSteps.Any(c =>
                               ReferenceEquals(step, c) || IsAncestorOf(step, c));
                step.Enabled = keep;
            }

            // Also reset ancestors of cleanup so live status is refreshed.
            foreach (var cleanup in cleanupSteps)
            {
                foreach (var step in FlattenSteps(_plan))
                {
                    if (ReferenceEquals(step, cleanup) || IsAncestorOf(step, cleanup))
                    {
                        resetStepIds.Add(step.Id.ToString());
                    }
                }
            }

            RefreshTreeEnabled();
            return await RunAsyncCore(progress, cancellationToken, resetStepIds, sampleScopePaths, runId)
                .ConfigureAwait(false);
        }
        finally
        {
            foreach (var step in FlattenSteps(_plan))
            {
                if (mask.TryGetValue(step.Id, out var enabled))
                {
                    step.Enabled = enabled;
                }
            }

            RefreshTreeEnabled();
        }
    }

    public Task<OpenTapRunSummary> RunAsync(
        IProgress<OpenTapProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? runId = null)
        => RunAsyncCore(progress, cancellationToken, resetStepIds: null, sampleScopePaths: null, runId);

    private async Task<OpenTapRunSummary> RunAsyncCore(
        IProgress<OpenTapProgress>? progress,
        CancellationToken cancellationToken,
        HashSet<string>? resetStepIds,
        IReadOnlyList<string>? sampleScopePaths,
        string? runId = null)
    {
        if (Interlocked.CompareExchange(ref _runGate, 1, 0) != 0)
        {
            throw new InvalidOperationException("A run is already in progress.");
        }

        Raise(nameof(IsExecuting));
        try
        {
            return await RunAsyncCoreUnderGate(
                    progress,
                    cancellationToken,
                    resetStepIds,
                    sampleScopePaths,
                    runId)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _runGate, 0);
            Raise(nameof(IsExecuting));
        }
    }

    private async Task<OpenTapRunSummary> RunAsyncCoreUnderGate(
        IProgress<OpenTapProgress>? progress,
        CancellationToken cancellationToken,
        HashSet<string>? resetStepIds,
        IReadOnlyList<string>? sampleScopePaths,
        string? runId)
    {
        TestPlan plan;
        List<StoredSample>? preservedSamples = null;
        lock (_sync)
        {
            plan = _plan ?? throw new InvalidOperationException("Load a plan before running.");
            if (sampleScopePaths is not null)
            {
                preservedSamples = _samples
                    .Where(s => !IsPathUnderAnyScope(s.StepPath, sampleScopePaths))
                    .ToList();
            }

            _samples.Clear();
            _steps.Clear();
            _stepStarted.Clear();
            _awaitingOperator = false;
            _operatorPromptMessage = null;
            _pendingInteraction = null;
            _interactionResponse = null;
            _interactionGate.Reset();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Preserve an existing Pause so WaitIfPaused at execute-start can block short plans.
            ResetTreeLiveState(_stepTree, resetStepIds);
        }

        Raise(nameof(IsAwaitingOperator));
        Raise(nameof(OperatorPromptMessage));
        Raise(nameof(PendingInteraction));

        var started = DateTimeOffset.UtcNow;
        runId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
        progress?.Report(new OpenTapProgress { Message = $"Starting '{LoadedPlanName ?? plan.Name}'", OverallPercent = 0 });

        try
        {
            var listener = new ProgressResultListener(
                progress,
                _samples,
                _steps,
                _stepStarted,
                UpdateNodeLive,
                ResolveStepPath,
                plan);
            OpenTapFileResultExportListener? exportListener = null;
            if (_settings.ExportOpenTapResults)
            {
                if (string.IsNullOrWhiteSpace(_settings.DataDirectory))
                {
                    _logger.Warning(
                        "ExportOpenTapResults is enabled but DataDirectory is empty; skipping CSV export.");
                }
                else
                {
                    var exportDir = Path.Combine(
                        _settings.DataDirectory,
                        "runs",
                        SanitizeRunId(runId),
                        "opentap-results");
                    exportListener = new OpenTapFileResultExportListener(exportDir, _logger);
                }
            }

            using var reg = _runCts!.Token.Register(() =>
            {
                // Prefer cooperative cancel via WaitIfPaused / interaction gates.
                // TapThread.Abort can poison later Execute calls in the same process (serial host suite).
                try
                {
                    _paused = false;
                    _pauseGate.Set();
                    _interactionGate.Set();
                }
                catch
                {
                    // ignored
                }
            });

            var planRun = await Task.Run(
                () =>
                {
                    StepRuntime.WaitIfPaused = WaitIfPaused;
                    StepRuntime.RequestInteraction = request => HandleInteraction(request, progress);
                    try
                    {
                        WaitIfPaused();
                        ResultListener[] listeners = exportListener is null
                            ? [listener]
                            : [listener, exportListener];
                        return plan.Execute(listeners, []);
                    }
                    finally
                    {
                        StepRuntime.WaitIfPaused = null;
                        StepRuntime.RequestInteraction = null;
                        exportListener?.Close();
                    }
                },
                CancellationToken.None).ConfigureAwait(false);

            MergePreservedSamples(preservedSamples);

            var verdict = planRun.Verdict;
            var result = MapVerdict(verdict, cancelled: _runCts.IsCancellationRequested);
            var summary = BuildSummary(runId, plan, result, started, verdict.ToString(),
                result == RunResult.Cancelled
                    ? (_runCts.IsCancellationRequested ? "Safety stop" : "Cancelled")
                    : (verdict == Verdict.Fail ? "One or more steps failed" : null));

            progress?.Report(new OpenTapProgress
            {
                Message = $"Completed: {summary.Result}",
                OverallPercent = 100,
                IsCompleted = true,
                Result = summary.Result,
            });
            return summary;
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex.GetType().Name.Contains("Abort", StringComparison.Ordinal))
        {
            MergePreservedSamples(preservedSamples);
            var summary = BuildSummary(runId, plan, RunResult.Cancelled, started, "Aborted", "Safety stop");
            progress?.Report(new OpenTapProgress
            {
                Message = "Cancelled",
                OverallPercent = 100,
                IsCompleted = true,
                Result = RunResult.Cancelled,
            });
            return summary;
        }
        finally
        {
            lock (_sync)
            {
                _awaitingOperator = false;
                _operatorPromptMessage = null;
                _pendingInteraction = null;
                _interactionResponse = null;
                _interactionGate.Set();
                _paused = false;
                _pauseGate.Set();
                _runCts?.Dispose();
                _runCts = null;
            }

            Raise(nameof(IsAwaitingOperator));
            Raise(nameof(OperatorPromptMessage));
            Raise(nameof(PendingInteraction));
        }
    }

    private void MergePreservedSamples(List<StoredSample>? preservedSamples)
    {
        if (preservedSamples is null || preservedSamples.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            var runSamples = _samples.ToList();
            var producedChannels = runSamples
                .Select(s => s.Channel)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Prior samples already exclude the selection scope by path. Drop path-less prior
            // samples when this run produced the same channel so new samples win.
            var kept = preservedSamples
                .Where(s => !(producedChannels.Contains(s.Channel) && string.IsNullOrWhiteSpace(s.StepPath)))
                .ToList();

            _samples.Clear();
            _samples.AddRange(kept);
            _samples.AddRange(runSamples);
        }
    }

    private OpenTapRunSummary BuildSummary(
        string runId,
        TestPlan plan,
        RunResult result,
        DateTimeOffset started,
        string verdict,
        string? error)
        => new()
        {
            RunId = runId,
            PlanName = LoadedPlanName ?? plan.Name,
            Result = result,
            ErrorMessage = error,
            DutSerial = _dutIdentity?.Serial ?? _dut?.SerialNumber,
            DutPartNumber = _dutIdentity?.PartNumber ?? _dut?.PartNumber,
            DutRevision = _dutIdentity?.Revision ?? _dut?.Revision,
            StartedAt = started,
            CompletedAt = DateTimeOffset.UtcNow,
            Samples = _samples.ToList(),
            Steps = _steps.ToList(),
            Verdict = verdict,
        };

    private OperatorInteractionResponse HandleInteraction(
        OperatorInteractionRequest request,
        IProgress<OpenTapProgress>? progress)
    {
        lock (_sync)
        {
            _pendingInteraction = request;
            _interactionResponse = null;
            _interactionGate.Reset();
            _awaitingOperator = true;
            _operatorPromptMessage = request.Message;
        }

        Pause();
        Raise(nameof(IsAwaitingOperator));
        Raise(nameof(OperatorPromptMessage));
        Raise(nameof(PendingInteraction));
        progress?.Report(new OpenTapProgress
        {
            Message = request.Message,
            AwaitingOperator = true,
            OperatorPromptMessage = request.Message,
            InteractionRequest = request,
            StatusText = "Awaiting operator",
        });

        while (!_interactionGate.Wait(50))
        {
            _runCts?.Token.ThrowIfCancellationRequested();
        }

        OperatorInteractionResponse response;
        lock (_sync)
        {
            response = _interactionResponse
                       ?? OperatorInteractionResponse.Cancel(request.Id);
            _pendingInteraction = null;
            _interactionResponse = null;
            _awaitingOperator = false;
            _operatorPromptMessage = null;
        }

        Raise(nameof(IsAwaitingOperator));
        Raise(nameof(OperatorPromptMessage));
        Raise(nameof(PendingInteraction));
        return response;
    }

    public void Pause()
    {
        _paused = true;
        _pauseGate.Reset();
    }

    public void Resume(OperatorInteractionResponse? response = null)
    {
        lock (_sync)
        {
            if (_pendingInteraction is not null)
            {
                _interactionResponse = response
                    ?? OperatorInteractionResponse.Continue(_pendingInteraction.Id);
            }

            _awaitingOperator = false;
            _operatorPromptMessage = null;
            _interactionGate.Set();
        }

        Raise(nameof(IsAwaitingOperator));
        Raise(nameof(OperatorPromptMessage));
        Raise(nameof(PendingInteraction));
        _paused = false;
        _pauseGate.Set();
    }

    public void Abort(bool safetyStop = false)
    {
        lock (_sync)
        {
            if (_pendingInteraction is not null)
            {
                _interactionResponse = OperatorInteractionResponse.Cancel(_pendingInteraction.Id);
            }

            _awaitingOperator = false;
            _operatorPromptMessage = null;
            _interactionGate.Set();
        }

        Raise(nameof(IsAwaitingOperator));
        Raise(nameof(OperatorPromptMessage));
        Raise(nameof(PendingInteraction));
        _paused = false;
        _pauseGate.Set();

        try
        {
            _runCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _logger.Warning("OpenTAP abort requested (safety={Safety})", safetyStop);
    }

    public bool TrySetStepEnabled(string stepPath, bool enabled)
    {
        if (IsExecuting)
        {
            return false;
        }

        var step = FindStepByPath(stepPath);
        if (step is null)
        {
            return false;
        }

        step.Enabled = enabled;
        RefreshTreeEnabled();
        return true;
    }

    public bool TrySetAcquireSettings(string stepPath, int? sampleCount, int? intervalMs)
    {
        if (IsExecuting)
        {
            return false;
        }

        if (FindStepByPath(stepPath) is not AcquireVoltageStep)
        {
            return false;
        }

        var ok = true;
        if (sampleCount is > 0)
        {
            ok &= TrySetParameterForStepPath(stepPath, nameof(AcquireVoltageStep.SampleCount), sampleCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (intervalMs is >= 0)
        {
            ok &= TrySetParameterForStepPath(stepPath, nameof(AcquireVoltageStep.IntervalMs), intervalMs.Value.ToString(CultureInfo.InvariantCulture));
        }

        return ok;
    }

    public bool TrySetMeanGteThreshold(string stepPath, double threshold)
    {
        if (IsExecuting)
        {
            return false;
        }

        if (FindStepByPath(stepPath) is not MeanGteStep)
        {
            return false;
        }

        return TrySetParameterForStepPath(
            stepPath,
            nameof(MeanGteStep.Threshold),
            threshold.ToString(CultureInfo.InvariantCulture));
    }

    public IReadOnlyList<OpenTapParameterInfo> EnumerateParameters(
        OpenTapParameterScope scope,
        string? stepPath = null,
        bool includeReadOnly = false,
        OpenTapParameterListing listing = OpenTapParameterListing.StationOverrides)
    {
        if (_plan is null)
        {
            return [];
        }

        if (scope == OpenTapParameterScope.Plan)
        {
            return OpenTapParameterBridge.Enumerate(
                _plan,
                "plan",
                stepId: null,
                stepPath: null,
                includeReadOnly,
                listing);
        }

        var step = FindStepByPath(stepPath ?? string.Empty);
        if (step is null)
        {
            return [];
        }

        var node = FindNode(_stepTree, step.Id.ToString());
        return OpenTapParameterBridge.Enumerate(
            step,
            step.Id.ToString(),
            step.Id.ToString(),
            node?.Path ?? stepPath,
            includeReadOnly,
            listing);
    }

    public bool TryGetParameter(string memberKey, out string? value)
    {
        value = null;
        if (!TryResolveOwner(memberKey, out var owner, out var memberName))
        {
            return false;
        }

        return OpenTapParameterBridge.TryGet(owner, memberName, out value);
    }

    public bool TrySetParameter(string memberKey, string value)
    {
        if (IsExecuting)
        {
            return false;
        }

        if (!TryResolveOwner(memberKey, out var owner, out var memberName))
        {
            return false;
        }

        var ok = OpenTapParameterBridge.TrySet(owner, memberName, value);
        if (ok && owner is ITestStep && string.Equals(memberName, nameof(ITestStep.Enabled), StringComparison.OrdinalIgnoreCase))
        {
            RefreshTreeEnabled();
        }

        return ok;
    }

    private bool TrySetParameterForStepPath(string stepPath, string memberName, string value)
    {
        var step = FindStepByPath(stepPath);
        if (step is null)
        {
            return false;
        }

        return TrySetParameter(OpenTapParameterBridge.FormatStepMemberKey(step.Id.ToString(), memberName), value);
    }

    private bool TryResolveOwner(string memberKey, out object owner, out string memberName)
    {
        owner = null!;
        memberName = string.Empty;
        if (_plan is null || !OpenTapParameterBridge.TryParseMemberKey(memberKey, out var ownerKey, out memberName))
        {
            return false;
        }

        if (string.Equals(ownerKey, "plan", StringComparison.OrdinalIgnoreCase))
        {
            owner = _plan;
            return true;
        }

        var step = FlattenSteps(_plan).FirstOrDefault(s =>
            string.Equals(s.Id.ToString(), ownerKey, StringComparison.OrdinalIgnoreCase));
        if (step is null)
        {
            return false;
        }

        owner = step;
        return true;
    }

    public bool TryGetStepConditionSummary(string stepPath, out string? summary)
    {
        summary = null;
        var step = FindStepByPath(stepPath);
        if (step is null)
        {
            return false;
        }

        var parts = new List<string>();
        switch (step)
        {
            case MeanGteStep mean:
                parts.Add($"Mean ≥ {mean.Threshold}");
                parts.Add($"Samples={mean.SampleCount}");
                parts.Add($"Enabled={mean.Enabled}");
                break;
            case AcquireVoltageStep acquire:
                parts.Add($"Samples={acquire.SampleCount}");
                parts.Add($"IntervalMs={acquire.IntervalMs}");
                parts.Add($"Enabled={acquire.Enabled}");
                break;
            default:
                parts.Add($"Enabled={step.Enabled}");
                break;
        }

        summary = string.Join(", ", parts);
        return true;
    }

    public bool TryRebindDmmResource(string resource)
        => TryBindSlotResource(_slots.FirstOrDefault()?.Name ?? "DMM", resource);

    public bool TryBindSlotResource(string slotName, string resource)
    {
        if (IsExecuting)
        {
            return false;
        }

        lock (_sync)
        {
            return TryBindSlotResource_NoLock(slotName, resource);
        }
    }

    private bool TryBindSlotResource_NoLock(string slotName, string resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            return false;
        }

        var slot = _slots.FirstOrDefault(s => string.Equals(s.Name, slotName, StringComparison.OrdinalIgnoreCase));
        var instr = _instruments.FirstOrDefault(i =>
                        string.Equals(i.Name, slotName, StringComparison.OrdinalIgnoreCase))
                    ?? _instruments.FirstOrDefault();
        if (instr is null)
        {
            return false;
        }

        var trimmed = resource.Trim();
        if (!InstrumentResourceAccess.TrySetResource(instr, trimmed))
        {
            return false;
        }

        if (slot is not null)
        {
            slot.ResourceName = trimmed;
        }

        return true;
    }

    private void UpdateNodeLive(string stepId, string? name, string status, string verdict, string? keyValue)
    {
        var node = FindNode(_stepTree, stepId) ?? FindNodeByName(_stepTree, name);
        if (node is null)
        {
            return;
        }

        node.StatusText = status;
        node.Verdict = verdict;
        if (keyValue is not null)
        {
            node.KeyValue = keyValue;
        }

        Raise(nameof(StepTree));
    }

    private string? ResolveStepPath(string stepId, string? name)
    {
        var node = FindNode(_stepTree, stepId) ?? FindNodeByName(_stepTree, name);
        return node?.Path;
    }

    private ITestStep? FindStepByPath(string stepPath)
    {
        if (_plan is null || string.IsNullOrWhiteSpace(stepPath))
        {
            return null;
        }

        return FlattenSteps(_plan).FirstOrDefault(s =>
        {
            var node = FindNode(_stepTree, s.Id.ToString());
            return (node is not null && string.Equals(node.Path, stepPath, StringComparison.OrdinalIgnoreCase))
                   || string.Equals(s.Name, stepPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool IsInSubtree(ITestStep candidate, ITestStep root)
    {
        if (ReferenceEquals(candidate, root) || candidate.Id == root.Id)
        {
            return true;
        }

        return FlattenSteps(root).Any(s => s.Id == candidate.Id);
    }

    /// True when candidate is an ancestor group that must stay enabled for selected to execute.
    private static bool IsAncestorOf(ITestStep candidate, ITestStep selected)
        => FlattenSteps(candidate).Any(s => s.Id == selected.Id);

    private static OpenTapStepNode? FindNode(IEnumerable<OpenTapStepNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var nested = FindNode(node.Children, id);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static OpenTapStepNode? FindNodeByName(IEnumerable<OpenTapStepNode> nodes, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (var node in nodes)
        {
            if (string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var nested = FindNodeByName(node.Children, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void RefreshTreeEnabled()
    {
        if (_plan is null)
        {
            return;
        }

        void Sync(OpenTapStepNode node, ITestStep step)
        {
            node.Enabled = step.Enabled;
            for (var i = 0; i < Math.Min(node.Children.Count, step.ChildTestSteps.Count); i++)
            {
                Sync(node.Children[i], step.ChildTestSteps[i]);
            }
        }

        for (var i = 0; i < Math.Min(_stepTree.Count, _plan.ChildTestSteps.Count); i++)
        {
            Sync(_stepTree[i], _plan.ChildTestSteps[i]);
        }

        Raise(nameof(StepTree));
    }

    private static void ResetTreeLiveState(IEnumerable<OpenTapStepNode> nodes, HashSet<string>? resetIds = null)
    {
        foreach (var node in nodes)
        {
            if (resetIds is null || resetIds.Contains(node.Id))
            {
                node.StatusText = "Pending";
                node.Verdict = "NotSet";
                node.KeyValue = null;
            }

            ResetTreeLiveState(node.Children, resetIds);
        }
    }

    private static bool IsPathUnderAnyScope(string? path, IReadOnlyList<string> scopeRoots)
    {
        if (string.IsNullOrWhiteSpace(path) || scopeRoots.Count == 0)
        {
            return false;
        }

        foreach (var root in scopeRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefix = root.TrimEnd('/') + "/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void WaitIfPaused()
    {
        while (true)
        {
            _runCts?.Token.ThrowIfCancellationRequested();
            if (!_paused)
            {
                return;
            }

            _pauseGate.Wait(50);
        }
    }

    public IReadOnlyList<OpenTapPluginDirectoryInfo> ListPluginDirectories()
    {
        EnsurePlugins();
        return OpenTapPackageCatalog.ListPluginDirectories(_settings);
    }

    public IReadOnlyList<OpenTapPackageInfo> ListInstalledPackages()
    {
        EnsurePlugins();
        return OpenTapPackageCatalog.ListInstalledPackages(_settings, _logger);
    }

    public IReadOnlyList<OpenTapDiscoveredAddress> ListDiscoveredDeviceAddresses()
    {
        EnsurePlugins();
        return OpenTapDeviceDiscovery.ListVisaAddresses(_logger);
    }

    private void EnsurePlugins()
    {
        if (_pluginSearchDone)
        {
            return;
        }

        var extras = new List<string>();
        foreach (var dir in CollectConfiguredPluginDirectories())
        {
            if (PluginDirectoryTrust.Allows(_settings.DataDirectory, dir, _settings.IsEngineerDebugMode))
            {
                extras.Add(dir);
                continue;
            }

            _logger.Warning(
                "Skipping OpenTAP plugin directory outside trusted root {Root}: {Dir}",
                PluginDirectoryTrust.TrustedRoot(_settings.DataDirectory),
                dir);
        }

        // Directory list mutations + Search share one gate (OpenTapPluginSearch.SearchSerialized).
        OpenTapPluginSearch.SearchSerialized(extras);
        _pluginSearchDone = true;
    }

    private IEnumerable<string> CollectConfiguredPluginDirectories()
    {
        foreach (var dir in _settings.OpenTapPluginDirectories)
        {
            if (!string.IsNullOrWhiteSpace(dir))
            {
                yield return dir;
            }
        }

        var env = Environment.GetEnvironmentVariable("HARDWARETEST_OPENTAP_PLUGIN_DIRS");
        if (string.IsNullOrWhiteSpace(env))
        {
            yield break;
        }

        foreach (var part in env.Split(
                     [Path.PathSeparator, ';'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return part;
        }
    }

    private static IEnumerable<ITestStep> FlattenSteps(ITestStepParent parent)
    {
        foreach (var child in parent.ChildTestSteps)
        {
            yield return child;
            foreach (var nested in FlattenSteps(child))
            {
                yield return nested;
            }
        }
    }

    private static HardwareDut? FindDut(TestPlan plan)
        => FlattenSteps(plan).OfType<IdentityCheckStep>().Select(s => s.Dut).FirstOrDefault();

    private static string SanitizeRunId(string runId)
        => HardwareTest.Core.IO.PortableFileNames.Sanitize(runId);

    private static List<OpenTapStepNode> BuildTree(TestPlan plan)
    {
        OpenTapStepNode Map(ITestStep step, string parentPath)
        {
            var path = string.IsNullOrEmpty(parentPath) ? step.Name : $"{parentPath}/{step.Name}";
            var node = new OpenTapStepNode
            {
                Id = step.Id.ToString(),
                Name = step.Name,
                Path = path,
                Enabled = step.Enabled,
                IsStage = step is TestGroupStep || step.ChildTestSteps.Count > 0,
            };
            foreach (var child in step.ChildTestSteps)
            {
                node.Children.Add(Map(child, path));
            }

            return node;
        }

        return plan.ChildTestSteps.Select(s => Map(s, string.Empty)).ToList();
    }

    private static RunResult MapVerdict(Verdict verdict, bool cancelled)
    {
        if (cancelled)
        {
            return RunResult.Cancelled;
        }

        return verdict switch
        {
            Verdict.Pass => RunResult.Passed,
            Verdict.Fail => RunResult.Failed,
            Verdict.Error => RunResult.Error,
            Verdict.Aborted => RunResult.Cancelled,
            _ => RunResult.Unknown,
        };
    }

    private void ThrowIfExecuting(string action)
    {
        if (IsExecuting)
        {
            throw new InvalidOperationException($"Cannot {action} while a run is in progress.");
        }
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class ProgressResultListener : ResultListener
{
    private sealed class LoopContext
    {
        public required Guid StepId { get; init; }
        public int? Total { get; init; }
        public int Index { get; set; }
    }

    private static readonly long SampleReportIntervalTicks = TimeSpan.FromMilliseconds(50).Ticks;

    private readonly IProgress<OpenTapProgress>? _progress;
    private readonly List<StoredSample> _samples;
    private readonly List<StepResultRecord> _steps;
    private readonly Dictionary<string, DateTimeOffset> _stepStarted;
    private readonly Action<string, string?, string, string, string?> _updateNode;
    private readonly Func<string, string?, string?> _resolvePath;
    private readonly TestPlan _plan;
    private readonly Stack<LoopContext> _loops = new();
    private int _stepIndex;
    private int _stepCount = 1;
    private long _lastSampleReportTimestamp;
    private MeasurementSampleEvent? _coalescedSample;
    private double _coalescedPercent;
    private string? _currentStepId;
    private string? _currentStepName;

    public ProgressResultListener(
        IProgress<OpenTapProgress>? progress,
        List<StoredSample> samples,
        List<StepResultRecord> steps,
        Dictionary<string, DateTimeOffset> stepStarted,
        Action<string, string?, string, string, string?> updateNode,
        Func<string, string?, string?> resolvePath,
        TestPlan plan)
    {
        _progress = progress;
        _samples = samples;
        _steps = steps;
        _stepStarted = stepStarted;
        _updateNode = updateNode;
        _resolvePath = resolvePath;
        _plan = plan;
        Name = "HardwareTestProgress";
    }

    public override void OnTestPlanRunStart(TestPlanRun planRun)
    {
        _stepCount = OpenTapLoopProgress.CountEnabledLeaves(_plan);
        _stepIndex = 0;
        _loops.Clear();
        _lastSampleReportTimestamp = 0;
        _coalescedSample = null;
        _progress?.Report(new OpenTapProgress { Message = "Plan started", OverallPercent = 0 });
    }

    public override void OnTestStepRunStart(TestStepRun stepRun)
    {
        FlushCoalescedSample();
        var id = stepRun.TestStepId.ToString();
        _currentStepId = id;
        _currentStepName = stepRun.TestStepName;
        _stepStarted[id] = DateTimeOffset.UtcNow;
        _updateNode(id, stepRun.TestStepName, "Running", "NotSet", null);

        var step = OpenTapLoopProgress.FindStepById(_plan, stepRun.TestStepId);
        if (step is not null && OpenTapLoopProgress.IsLoopStep(step))
        {
            _loops.Push(new LoopContext
            {
                StepId = step.Id,
                Total = OpenTapLoopProgress.TryGetLoopTotal(step),
                Index = 0,
            });
        }
        else if (step is not null && _loops.Count > 0)
        {
            var loopStep = OpenTapLoopProgress.FindStepById(_plan, _loops.Peek().StepId);
            if (loopStep is not null && IsFirstEnabledDirectChild(step, loopStep))
            {
                var loop = _loops.Peek();
                loop.Index++;
                if (loop.Total is > 0)
                {
                    loop.Index = Math.Min(loop.Index, loop.Total.Value);
                }
            }
        }

        ReportStepProgress(
            $"Running {stepRun.TestStepName}",
            stepRun.TestStepName,
            id,
            "Running",
            "NotSet");
    }

    public override void OnTestStepRunCompleted(TestStepRun stepRun)
    {
        FlushCoalescedSample();
        var id = stepRun.TestStepId.ToString();
        var started = _stepStarted.TryGetValue(id, out var s) ? s : DateTimeOffset.UtcNow;
        var verdict = stepRun.Verdict.ToString();
        var passed = stepRun.Verdict is Verdict.Pass or Verdict.NotSet;
        _steps.Add(new StepResultRecord
        {
            StepId = id,
            StepType = stepRun.TestStepName,
            StepPath = _resolvePath(id, stepRun.TestStepName) ?? string.Empty,
            Passed = passed && stepRun.Verdict != Verdict.Fail && stepRun.Verdict != Verdict.Error,
            Message = verdict,
            StartedAt = started,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        _updateNode(id, stepRun.TestStepName, verdict, verdict, null);
        _stepIndex++;

        if (_loops.Count > 0 && _loops.Peek().StepId == stepRun.TestStepId)
        {
            _loops.Pop();
        }

        ReportStepProgress(
            $"{stepRun.TestStepName}: {stepRun.Verdict}",
            stepRun.TestStepName,
            id,
            verdict,
            verdict);
    }

    private static bool IsFirstEnabledDirectChild(ITestStep child, ITestStep parent)
    {
        foreach (var sibling in parent.ChildTestSteps)
        {
            if (!sibling.Enabled)
            {
                continue;
            }

            return ReferenceEquals(sibling, child);
        }

        return false;
    }

    private void ReportStepProgress(
        string message,
        string? stepName,
        string stepId,
        string statusText,
        string verdict)
    {
        int? iterationIndex = null;
        int? iterationTotal = null;
        string? iterationText = null;
        if (_loops.Count > 0)
        {
            var loop = _loops.Peek();
            if (loop.Index > 0)
            {
                iterationIndex = loop.Index;
                iterationTotal = loop.Total;
                iterationText = OpenTapLoopProgress.FormatIteration(loop.Index, loop.Total);
            }
        }

        var denominator = Math.Max(_stepCount, _stepIndex);
        _progress?.Report(new OpenTapProgress
        {
            Message = message,
            StepName = stepName,
            StepId = stepId,
            StepPath = _resolvePath(stepId, stepName),
            StatusText = statusText,
            Verdict = verdict,
            OverallPercent = (double)_stepIndex / denominator * 100,
            IterationIndex = iterationIndex,
            IterationTotal = iterationTotal,
            IterationText = iterationText,
        });
    }

    public override void OnResultPublished(Guid stepRunId, ResultTable result)
    {
        try
        {
            if (string.Equals(result.Name, "Sample", StringComparison.OrdinalIgnoreCase))
            {
                PublishSamples(result);
                return;
            }

            if (string.Equals(result.Name, "Scalar", StringComparison.OrdinalIgnoreCase))
            {
                PublishScalars(result);
                return;
            }

            if (string.Equals(result.Name, "Identity", StringComparison.OrdinalIgnoreCase))
            {
                var idn = result.Columns.FirstOrDefault(c => c.Name == "Idn");
                var key = idn is null || idn.Data.Length == 0 ? null : Convert.ToString(idn.Data.GetValue(0));
                if (_currentStepId is not null)
                {
                    _updateNode(_currentStepId, _currentStepName, "Running", "NotSet", key);
                }

                _progress?.Report(new OpenTapProgress
                {
                    Message = $"IDN {key}",
                    StepId = _currentStepId,
                    StepName = _currentStepName,
                    KeyValue = key,
                    StatusText = "Identity",
                });
                return;
            }

            if (string.Equals(result.Name, "Analyze", StringComparison.OrdinalIgnoreCase))
            {
                var meanCol = result.Columns.FirstOrDefault(c => c.Name == "Mean");
                var key = meanCol is null || meanCol.Data.Length == 0
                    ? null
                    : $"mean={Convert.ToDouble(meanCol.Data.GetValue(0)):F4}";
                if (_currentStepId is not null)
                {
                    _updateNode(_currentStepId, _currentStepName, "Running", "NotSet", key);
                }

                _progress?.Report(new OpenTapProgress
                {
                    Message = key ?? "Analyze",
                    StepId = _currentStepId,
                    StepName = _currentStepName,
                    KeyValue = key,
                    StatusText = "Analyze",
                });
                return;
            }

            if (string.Equals(result.Name, "OperatorPrompt", StringComparison.OrdinalIgnoreCase))
            {
                var msgCol = result.Columns.FirstOrDefault(c => c.Name == "Message");
                var msg = msgCol is null || msgCol.Data.Length == 0
                    ? "Awaiting operator"
                    : Convert.ToString(msgCol.Data.GetValue(0));
                // Do not set AwaitingOperator here — HandleInteraction already reports the
                // authoritative pause (with InteractionRequest). Emitting a second
                // AwaitingOperator=true frame without a request races Progress handlers.
                _progress?.Report(new OpenTapProgress
                {
                    Message = msg ?? "Awaiting operator",
                    StepId = _currentStepId,
                    StepName = _currentStepName,
                    OperatorPromptMessage = msg,
                    StatusText = "Operator prompt noted",
                    KeyValue = msg,
                });
            }
        }
        catch
        {
            // Ignore malformed result rows.
        }
    }

    private void PublishSamples(ResultTable result)
    {
        var channelCol = result.Columns.FirstOrDefault(c => c.Name == "Channel");
        var valueCol = result.Columns.FirstOrDefault(c => c.Name == "Value");
        var indexCol = result.Columns.FirstOrDefault(c => c.Name == "Index");
        if (valueCol is null)
        {
            return;
        }

        var hints = CurrentPresentationHints();
        for (var i = 0; i < valueCol.Data.Length; i++)
        {
            var value = Convert.ToDouble(valueCol.Data.GetValue(i));
            var channel = channelCol is null ? "VDC" : Convert.ToString(channelCol.Data.GetValue(i)) ?? "VDC";
            var index = indexCol is null ? i : Convert.ToInt32(indexCol.Data.GetValue(i));
            var ts = DateTimeOffset.UtcNow;
            var stepPath = _resolvePath(_currentStepId ?? string.Empty, _currentStepName) ?? string.Empty;
            int? iterationIndex = null;
            string? loopPath = null;
            if (_loops.Count > 0)
            {
                var loop = _loops.Peek();
                if (loop.Index > 0)
                {
                    iterationIndex = loop.Index;
                    loopPath = _resolvePath(loop.StepId.ToString(), null);
                }
            }

            var stored = new StoredSample
            {
                Channel = channel,
                Timestamp = ts,
                Value = value,
                StepPath = stepPath,
                IterationIndex = iterationIndex,
                LoopPath = loopPath,
            };
            OpenTapPresentation.ApplySample(stored, channel, hints);
            _samples.Add(stored);

            var sampleEvent = MeasurementSampleEvent.FromStored(stored, index);
            var percent = (double)_stepIndex / _stepCount * 100;
            var nowTicks = Stopwatch.GetTimestamp();
            var elapsed = nowTicks - _lastSampleReportTimestamp;
            var interval = SampleReportIntervalTicks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
            if (_lastSampleReportTimestamp == 0 || elapsed >= interval)
            {
                _lastSampleReportTimestamp = nowTicks;
                _coalescedSample = null;
                _progress?.Report(new OpenTapProgress
                {
                    Message = "Sample",
                    Sample = sampleEvent,
                    StepId = _currentStepId,
                    StepName = _currentStepName,
                    StepPath = stepPath,
                    KeyValue = FormatSampleKey(stored),
                    OverallPercent = percent,
                });
            }
            else
            {
                _coalescedSample = sampleEvent;
                _coalescedPercent = percent;
            }
        }
    }

    private void PublishScalars(ResultTable result)
    {
        var nameCol = result.Columns.FirstOrDefault(c => c.Name == "Name");
        var valueCol = result.Columns.FirstOrDefault(c => c.Name == "Value");
        var unitCol = result.Columns.FirstOrDefault(c => c.Name == "Unit");
        var limitLowCol = result.Columns.FirstOrDefault(c => c.Name == "LimitLow");
        var limitHighCol = result.Columns.FirstOrDefault(c => c.Name == "LimitHigh");
        if (valueCol is null)
        {
            return;
        }

        var hints = CurrentPresentationHints();
        var stepPath = _resolvePath(_currentStepId ?? string.Empty, _currentStepName) ?? string.Empty;
        for (var i = 0; i < valueCol.Data.Length; i++)
        {
            var value = Convert.ToDouble(valueCol.Data.GetValue(i));
            var name = nameCol is null || i >= nameCol.Data.Length
                ? "Scalar"
                : Convert.ToString(nameCol.Data.GetValue(i)) ?? "Scalar";
            var unit = unitCol is null || i >= unitCol.Data.Length
                ? string.Empty
                : Convert.ToString(unitCol.Data.GetValue(i)) ?? string.Empty;
            var limitLow = TryReadOptionalDouble(limitLowCol, i);
            var limitHigh = TryReadOptionalDouble(limitHighCol, i);
            var stored = new StoredSample
            {
                Timestamp = DateTimeOffset.UtcNow,
                Value = value,
                StepPath = stepPath,
            };
            OpenTapPresentation.ApplyScalar(stored, name, unit, hints, limitLow, limitHigh);
            _samples.Add(stored);

            if (_currentStepId is not null)
            {
                _updateNode(_currentStepId, _currentStepName, "Running", "NotSet", FormatSampleKey(stored));
            }

            _progress?.Report(new OpenTapProgress
            {
                Message = FormatSampleKey(stored),
                StepId = _currentStepId,
                StepName = _currentStepName,
                StepPath = stepPath,
                KeyValue = FormatSampleKey(stored),
                StatusText = stored.DisplayRole ?? "Scalar",
                Sample = MeasurementSampleEvent.FromStored(stored),
                OverallPercent = (double)_stepIndex / Math.Max(_stepCount, 1) * 100,
            });
        }
    }

    private static double? TryReadOptionalDouble(ResultColumn? column, int index)
    {
        if (column is null || index >= column.Data.Length)
        {
            return null;
        }

        var raw = column.Data.GetValue(index);
        if (raw is null || raw is DBNull)
        {
            return null;
        }

        try
        {
            var value = Convert.ToDouble(raw);
            return double.IsNaN(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private OpenTapPresentation.MixinHints? CurrentPresentationHints()
    {
        if (_currentStepId is null || !Guid.TryParse(_currentStepId, out var id))
        {
            return null;
        }

        return OpenTapPresentation.TryReadMixin(OpenTapLoopProgress.FindStepById(_plan, id));
    }

    private static string FormatSampleKey(StoredSample sample)
    {
        var key = sample.EffectiveMetricKey;
        var role = string.IsNullOrWhiteSpace(sample.DisplayRole) ? null : sample.DisplayRole;
        var unit = string.IsNullOrWhiteSpace(sample.Unit) ? null : sample.Unit;
        var value = sample.Value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        if (role is null && unit is null)
        {
            return $"{key}={value}";
        }

        if (unit is null)
        {
            return $"{key} [{role}] {value}";
        }

        if (role is null)
        {
            return $"{key}={value} {unit}";
        }

        return $"{key} [{role}] {value} {unit}";
    }

    private void FlushCoalescedSample()
    {
        if (_coalescedSample is null)
        {
            return;
        }

        var sample = _coalescedSample;
        var percent = _coalescedPercent;
        _coalescedSample = null;
        _lastSampleReportTimestamp = Stopwatch.GetTimestamp();
        _progress?.Report(new OpenTapProgress
        {
            Message = "Sample",
            Sample = sample,
            StepId = _currentStepId,
            StepName = _currentStepName,
            OverallPercent = percent,
        });
    }
}
