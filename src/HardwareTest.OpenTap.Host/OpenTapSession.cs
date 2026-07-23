using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
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
}

public sealed record MeasurementSampleEvent(string Channel, int Index, double Value, DateTimeOffset Timestamp);

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

public interface IOpenTapSession
{
    string? LoadedPlanPath { get; }
    string? LoadedPlanName { get; }
    IReadOnlyList<OpenTapStepNode> StepTree { get; }
    IReadOnlyList<OpenTapInstrumentSlot> InstrumentSlots { get; }
    bool IsAwaitingOperator { get; }
    string? OperatorPromptMessage { get; }
    OperatorInteractionRequest? PendingInteraction { get; }

    Task LoadPlanAsync(string tapPlanPath, CancellationToken cancellationToken = default);
    Task LoadSampleProgramAsync(CancellationToken cancellationToken = default);
    Task LoadBoardDemoProgramAsync(CancellationToken cancellationToken = default);
    Task ApplyStationAndDutAsync(StationProfile station, DutIdentity dut, CancellationToken cancellationToken = default);
    Task<OpenTapRunSummary> RunAsync(IProgress<OpenTapProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<OpenTapRunSummary> RunSelectionAsync(string stepPath, IProgress<OpenTapProgress>? progress = null, CancellationToken cancellationToken = default);
    void Pause();
    void Resume(OperatorInteractionResponse? response = null);
    void Abort(bool safetyStop = false);

    bool TrySetStepEnabled(string stepPath, bool enabled);
    /// Sample adapter — prefer <see cref="TrySetParameter"/> / TypeData bridge for new code.
    bool TrySetAcquireSettings(string stepPath, int? sampleCount, int? intervalMs);
    /// Sample adapter — prefer <see cref="TrySetParameter"/> / TypeData bridge for new code.
    bool TrySetMeanGteThreshold(string stepPath, double threshold);
    bool TryGetStepConditionSummary(string stepPath, out string? summary);
    bool TryRebindDmmResource(string resource);
    bool TryBindSlotResource(string slotName, string resource);

    IReadOnlyList<OpenTapParameterInfo> EnumerateParameters(
        OpenTapParameterScope scope,
        string? stepPath = null,
        bool includeReadOnly = false,
        OpenTapParameterListing listing = OpenTapParameterListing.StationOverrides);
    bool TryGetParameter(string memberKey, out string? value);
    bool TrySetParameter(string memberKey, string value);
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

    public OpenTapSession(AppSettings? settings = null, ILogger? logger = null)
    {
        _settings = settings ?? new AppSettings();
        _logger = logger ?? Serilog.Log.ForContext<OpenTapSession>();
    }

    public string? LoadedPlanPath { get; private set; }
    public string? LoadedPlanName { get; private set; }
    public IReadOnlyList<OpenTapStepNode> StepTree => _stepTree;
    public IReadOnlyList<OpenTapInstrumentSlot> InstrumentSlots => _slots;
    public bool IsAwaitingOperator => _awaitingOperator;
    public string? OperatorPromptMessage => _operatorPromptMessage;
    public OperatorInteractionRequest? PendingInteraction => _pendingInteraction;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task LoadSampleProgramAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePlugins();
        var plan = SampleProgramFactory.Create();
        BindPlan(plan, SampleProgramFactory.EmbeddedName, "Sample Hardware Suite");
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
            _slots = _instruments.Select(i => new OpenTapInstrumentSlot
            {
                Name = string.IsNullOrWhiteSpace(i.Name) ? i.GetType().Name : i.Name,
                TypeName = i.GetType().Name,
                RoleHint = GuessRole(i.Name),
                ResourceName = InstrumentResourceAccess.GetResource(i),
            }).ToList();
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
        CancellationToken cancellationToken = default)
    {
        if (_plan is null)
        {
            throw new InvalidOperationException("Load a plan before running.");
        }

        var selected = FindStepByPath(stepPath)
                       ?? throw new InvalidOperationException($"Step not found: {stepPath}");
        var mask = FlattenSteps(_plan).ToDictionary(s => s.Id, s => s.Enabled);

        // Reset only steps that will execute (subtree + SafeShutdown). Ancestors stay enabled for
        // OpenTAP structure but keep prior live status; rollup refreshes them afterward.
        var resetStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sampleScopePaths = new List<string>();
        foreach (var step in FlattenSteps(_plan))
        {
            if (!IsInSubtree(step, selected) && step is not SafeShutdownStep)
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
            foreach (var step in FlattenSteps(_plan))
            {
                var keep = IsInSubtree(step, selected)
                           || IsAncestorOf(step, selected)
                           || step is SafeShutdownStep;
                step.Enabled = keep;
            }

            RefreshTreeEnabled();
            return await RunAsyncCore(progress, cancellationToken, resetStepIds, sampleScopePaths)
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
        CancellationToken cancellationToken = default)
        => RunAsyncCore(progress, cancellationToken, resetStepIds: null, sampleScopePaths: null);

    private async Task<OpenTapRunSummary> RunAsyncCore(
        IProgress<OpenTapProgress>? progress,
        CancellationToken cancellationToken,
        HashSet<string>? resetStepIds,
        IReadOnlyList<string>? sampleScopePaths)
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
            _paused = false;
            _pauseGate.Set();
            ResetTreeLiveState(_stepTree, resetStepIds);
        }

        Raise(nameof(IsAwaitingOperator));
        Raise(nameof(OperatorPromptMessage));
        Raise(nameof(PendingInteraction));

        var started = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid().ToString("N");
        progress?.Report(new OpenTapProgress { Message = $"Starting '{LoadedPlanName ?? plan.Name}'", OverallPercent = 0 });

        try
        {
            var listener = new ProgressResultListener(progress, _samples, _steps, _stepStarted, UpdateNodeLive, ResolveStepPath);
            using var reg = _runCts!.Token.Register(() =>
            {
                try
                {
                    TapThread.Current?.Abort();
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
                        return plan.Execute([listener], []);
                    }
                    finally
                    {
                        StepRuntime.WaitIfPaused = null;
                        StepRuntime.RequestInteraction = null;
                    }
                },
                _runCts.Token).ConfigureAwait(false);

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

        try
        {
            TapThread.Current?.Abort();
        }
        catch
        {
            // ignore
        }

        _logger.Warning("OpenTAP abort requested (safety={Safety})", safetyStop);
    }

    public bool TrySetStepEnabled(string stepPath, bool enabled)
    {
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
        while (_paused)
        {
            _runCts?.Token.ThrowIfCancellationRequested();
            _pauseGate.Wait(50);
        }
    }

    private void EnsurePlugins()
    {
        if (_pluginSearchDone)
        {
            return;
        }

        var basicDir = Path.GetDirectoryName(typeof(MockDmmInstrument).Assembly.Location)
                       ?? AppContext.BaseDirectory;
        AddPluginSearchDir(basicDir);

        foreach (var dir in _settings.OpenTapPluginDirectories)
        {
            AddPluginSearchDir(dir);
        }

        var env = Environment.GetEnvironmentVariable("HARDWARETEST_OPENTAP_PLUGIN_DIRS");
        if (!string.IsNullOrWhiteSpace(env))
        {
            foreach (var part in env.Split([Path.PathSeparator, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddPluginSearchDir(part);
            }
        }

        PluginManager.Search();
        _pluginSearchDone = true;
    }

    private static void AddPluginSearchDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return;
        }

        var full = Path.GetFullPath(dir);
        if (!PluginManager.DirectoriesToSearch.Contains(full))
        {
            PluginManager.DirectoriesToSearch.Add(full);
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

    private static string GuessRole(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "dmm";
        }

        var n = name.Trim().ToLowerInvariant();
        if (n.Contains("scope", StringComparison.Ordinal))
        {
            return "scope";
        }

        if (n.Contains("supply", StringComparison.Ordinal) || n.Contains("psu", StringComparison.Ordinal))
        {
            return "psu";
        }

        return "dmm";
    }

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

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class ProgressResultListener : ResultListener
{
    private static readonly long SampleReportIntervalTicks = TimeSpan.FromMilliseconds(50).Ticks;

    private readonly IProgress<OpenTapProgress>? _progress;
    private readonly List<StoredSample> _samples;
    private readonly List<StepResultRecord> _steps;
    private readonly Dictionary<string, DateTimeOffset> _stepStarted;
    private readonly Action<string, string?, string, string, string?> _updateNode;
    private readonly Func<string, string?, string?> _resolvePath;
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
        Func<string, string?, string?> resolvePath)
    {
        _progress = progress;
        _samples = samples;
        _steps = steps;
        _stepStarted = stepStarted;
        _updateNode = updateNode;
        _resolvePath = resolvePath;
        Name = "HardwareTestProgress";
    }

    public override void OnTestPlanRunStart(TestPlanRun planRun)
    {
        _stepCount = 12;
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
        _progress?.Report(new OpenTapProgress
        {
            Message = $"Running {stepRun.TestStepName}",
            StepName = stepRun.TestStepName,
            StepId = id,
            StepPath = _resolvePath(id, stepRun.TestStepName),
            StatusText = "Running",
            Verdict = "NotSet",
            OverallPercent = (double)_stepIndex / _stepCount * 100,
        });
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
        _progress?.Report(new OpenTapProgress
        {
            Message = $"{stepRun.TestStepName}: {stepRun.Verdict}",
            StepName = stepRun.TestStepName,
            StepId = id,
            StepPath = _resolvePath(id, stepRun.TestStepName),
            StatusText = verdict,
            Verdict = verdict,
            OverallPercent = (double)_stepIndex / _stepCount * 100,
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
                _progress?.Report(new OpenTapProgress
                {
                    Message = msg ?? "Awaiting operator",
                    StepId = _currentStepId,
                    StepName = _currentStepName,
                    AwaitingOperator = true,
                    OperatorPromptMessage = msg,
                    StatusText = "Awaiting operator",
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

        for (var i = 0; i < valueCol.Data.Length; i++)
        {
            var value = Convert.ToDouble(valueCol.Data.GetValue(i));
            var channel = channelCol is null ? "VDC" : Convert.ToString(channelCol.Data.GetValue(i)) ?? "VDC";
            var index = indexCol is null ? i : Convert.ToInt32(indexCol.Data.GetValue(i));
            var ts = DateTimeOffset.UtcNow;
            var stepPath = _resolvePath(_currentStepId ?? string.Empty, _currentStepName) ?? string.Empty;
            _samples.Add(new StoredSample
            {
                Channel = channel,
                Timestamp = ts,
                Value = value,
                StepPath = stepPath,
            });

            var sampleEvent = new MeasurementSampleEvent(channel, index, value, ts);
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
                    KeyValue = $"{channel}={value:F3}",
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
