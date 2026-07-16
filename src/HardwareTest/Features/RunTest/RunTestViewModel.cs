using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

public partial class HierarchyStepViewModel : ReactiveObject
{
    public HierarchyStepViewModel(OpenTapStepNode node)
    {
        Node = node;
        Name = node.Name;
        Path = node.Path;
        Id = node.Id;
        IsStage = node.IsStage;
        Children = new ObservableCollection<HierarchyStepViewModel>(
            node.Children.Select(c => new HierarchyStepViewModel(c)));
        SyncFromNode();
    }

    public OpenTapStepNode Node { get; }
    public string Id { get; }
    public string Name { get; }
    public string Path { get; }
    public bool IsStage { get; }
    public ObservableCollection<HierarchyStepViewModel> Children { get; }

    [Reactive] private string _statusText = "Pending";
    [Reactive] private string _verdict = "NotSet";
    [Reactive] private bool _enabled = true;
    [Reactive] private string? _keyValue;

    public void SyncFromNode()
    {
        StatusText = Node.StatusText;
        Verdict = Node.Verdict;
        Enabled = Node.Enabled;
        KeyValue = Node.KeyValue;
        foreach (var child in Children)
        {
            child.SyncFromNode();
        }
    }
}

public partial class StageItemViewModel : ReactiveObject
{
    public StageItemViewModel(HierarchyStepViewModel? step, string displayName)
    {
        Step = step;
        DisplayName = displayName;
        Path = step?.Path;
    }

    public HierarchyStepViewModel? Step { get; }
    public string DisplayName { get; }
    public string? Path { get; }

    [Reactive] private string _statusText = "Pending";
    [Reactive] private string _verdict = "NotSet";
    [Reactive] private string? _keyValue;
}

public partial class ProgramItemViewModel : ReactiveObject
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Path { get; init; }
    public string DutFamily { get; init; } = "generic";
    public bool IsSample { get; init; }
    public ProgramRequirements Requirements { get; init; } = ProgramRequirements.Sample;
}

public partial class RunTestViewModel : ReactiveObject
{
    private const int PlotCapacity = 2048;
    private const int DetailCap = 200;
    private const int MaxDebugSampleCount = 4096;
    private const int MinDebugIntervalMs = 1;

    private readonly IOpenTapSession _openTap;
    private readonly OperatorSession _session;
    private readonly IRunControl _runControl;
    private readonly IReportService _reportService;
    private readonly IRunStore _runStore;
    private readonly AppSettings _settings;
    private readonly object _progressSync = new();
    private readonly Queue<string> _pendingDetails = new();
    private readonly double[] _plotRing = new double[PlotCapacity];
    private readonly double[] _plotPublish = new double[PlotCapacity];
    private readonly ThrottledOpenTapProgress _progress;
    private readonly List<HierarchyStepViewModel> _fullHierarchy = [];

    private long _lastUiFlushTicks;
    private MeasurementSampleEvent? _pendingSample;
    private string? _pendingStatus;
    private double _pendingPercent;
    private bool _pendingForceFlush;
    private bool _pendingAwaitingOperator;
    private string? _pendingOperatorPrompt;
    private string? _pendingStepId;
    private string? _pendingVerdict;
    private string? _pendingKeyValue;
    private int _plotCount;
    private int _plotWrite;
    private int _plotPublishLength;
    private int _flushScheduled;

    public int PlotUiFlushCount { get; private set; }
    public int PlotYsLength => _plotPublishLength;
    public Action<Action>? UiScheduler { get; set; }
    public event EventHandler? PlotDataChanged;
    public event EventHandler? NavigateToResultsRequested;

    public RunTestViewModel(
        IOpenTapSession openTap,
        OperatorSession session,
        IRunControl runControl,
        IReportService reportService,
        IRunStore runStore,
        AppSettings settings)
    {
        _openTap = openTap;
        _session = session;
        _runControl = runControl;
        _reportService = reportService;
        _runStore = runStore;
        _settings = settings;
        _progress = new ThrottledOpenTapProgress(IngestProgress);
        Status = "Confirm DUT, then Run.";
        Programs = [];
        Hierarchy = [];
        Stages = [];
        DetailLines = [];
        DetailKeyValues = [];
        PlotYs = _plotPublish;
        IsEngineerDebugMode = settings.IsEngineerDebugMode;
        ShowSessionForm = true;

        ConfirmSessionCommand = ReactiveCommand.Create(ConfirmSession);
        ConfirmSameDutCommand = ReactiveCommand.Create(ConfirmSameDut);
        ChangeSessionCommand = ReactiveCommand.Create(ChangeSession);
        RefreshProgramsCommand = ReactiveCommand.CreateFromTask(RefreshProgramsAsync);
        OpenPlanFileCommand = ReactiveCommand.CreateFromTask(OpenPlanFileAsync);
        RunCommand = ReactiveCommand.CreateFromTask(() => ExecuteRunAsync(selectionOnly: false));
        RunSelectedCommand = ReactiveCommand.CreateFromTask(() => ExecuteRunAsync(selectionOnly: true));
        CancelCommand = ReactiveCommand.Create(Cancel);
        ContinueOperatorCommand = ReactiveCommand.Create(ContinueOperator);
        OpenStepDetailCommand = ReactiveCommand.Create(OpenSelectedDetail);
        CloseDetailCommand = ReactiveCommand.Create(() => { ShowDetailPane = false; });
        ToggleDetailsCommand = ReactiveCommand.Create(() => { ShowDetails = !ShowDetails; });
        ApplyDebugPatchCommand = ReactiveCommand.Create(ApplyDebugPatch);
        OpenLastRunResultsCommand = ReactiveCommand.Create(() => NavigateToResultsRequested?.Invoke(this, EventArgs.Empty));

        LoadPlanCommand = RefreshProgramsCommand;
        StartCommand = RunCommand;
        LoadSampleSuiteCommand = RefreshProgramsCommand;
        ConfirmDutCommand = ConfirmSessionCommand;
        ChangeDutCommand = ChangeSessionCommand;

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectedProgram) && SelectedProgram is not null)
            {
                Observe(LoadSelectedProgramAsync());
                RefreshRequirementFlags();
            }
            else if (args.PropertyName == nameof(SelectedStage))
            {
                ApplyStageFilter();
            }
            else if (args.PropertyName == nameof(SelectedStep) && SelectedStep is not null)
            {
                DebugStepEnabled = SelectedStep.Enabled;
            }
        };

        Observe(RefreshProgramsAsync());
    }

    private void Observe(Task task)
    {
        task.ContinueWith(
            t =>
            {
                if (t.Exception?.GetBaseException() is { } ex)
                {
                    Status = $"Error: {ex.Message}";
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public OperatorSession Session => _session;
    public ObservableCollection<ProgramItemViewModel> Programs { get; }
    public ObservableCollection<HierarchyStepViewModel> Hierarchy { get; }
    public ObservableCollection<StageItemViewModel> Stages { get; }
    public ObservableCollection<string> DetailLines { get; }
    public ObservableCollection<string> DetailKeyValues { get; }
    public Func<CancellationToken, Task<string?>>? RequestPlanFilePath { get; set; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ConfirmSessionCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ConfirmDutCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ConfirmSameDutCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ChangeSessionCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ChangeDutCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshProgramsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenPlanFileCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RunCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RunSelectedCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CancelCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ContinueOperatorCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenStepDetailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CloseDetailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleDetailsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ApplyDebugPatchCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenLastRunResultsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadPlanCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> StartCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadSampleSuiteCommand { get; }

    [Reactive] private ProgramItemViewModel? _selectedProgram;
    [Reactive] private StageItemViewModel? _selectedStage;
    [Reactive] private HierarchyStepViewModel? _selectedStep;
    [Reactive] private HierarchyStepViewModel? _detailStep;
    [Reactive] private string _dutSerialInput = string.Empty;
    [Reactive] private string _dutPartInput = string.Empty;
    [Reactive] private string _dutRevisionInput = string.Empty;
    [Reactive] private string _operatorInput = string.Empty;
    [Reactive] private bool _requirePartNumber;
    [Reactive] private bool _requireRevision;
    [Reactive] private bool _requireOperator;
    [Reactive] private bool _showSessionForm = true;
    [Reactive] private bool _showDetailPane;
    [Reactive] private string _status = string.Empty;
    [Reactive] private bool _isRunning;
    [Reactive] private string? _lastRunId;
    [Reactive] private double _overallPercent;
    [Reactive] private bool _showDetails;
    [Reactive] private double[] _plotYs = Array.Empty<double>();
    [Reactive] private string _sessionSummary = "Session: (confirm required)";
    [Reactive] private bool _needsDutConfirm = true;
    [Reactive] private bool _isStalePrompt;
    [Reactive] private bool _isAwaitingOperator;
    [Reactive] private string? _operatorPromptMessage;
    [Reactive] private bool _isEngineerDebugMode;
    [Reactive] private string _debugResource = "MOCK::INSTR0";
    [Reactive] private int _debugSampleCount = 32;
    [Reactive] private int _debugIntervalMs = 5;
    [Reactive] private double _debugThreshold;
    [Reactive] private bool _debugStepEnabled = true;
    [Reactive] private string _stationSlotSummary = "Station: (load program)";

    public void IngestProgress(OpenTapProgress progress)
    {
        lock (_progressSync)
        {
            _pendingPercent = progress.OverallPercent;
            if (!string.IsNullOrWhiteSpace(progress.Message))
            {
                _pendingStatus = progress.Message;
            }

            if (progress.Sample is { } sample)
            {
                _pendingSample = sample;
            }

            if (!string.IsNullOrWhiteSpace(progress.StepName) && (ShowDetails || ShowDetailPane))
            {
                EnqueueDetail_NoLock($"{progress.StepName}: {progress.Message}");
            }

            if (progress.AwaitingOperator)
            {
                _pendingAwaitingOperator = true;
                _pendingOperatorPrompt = progress.OperatorPromptMessage ?? progress.Message;
            }

            _pendingStepId = progress.StepId ?? _pendingStepId;
            _pendingVerdict = progress.Verdict ?? _pendingVerdict;
            _pendingKeyValue = progress.KeyValue ?? _pendingKeyValue;

            if (progress.IsCompleted)
            {
                _pendingForceFlush = true;
                _pendingAwaitingOperator = false;
            }
        }

        ScheduleUiFlush();
    }

    private void ConfirmSession()
    {
        var req = SelectedProgram?.Requirements ?? ProgramRequirements.Sample;
        var family = SelectedProgram?.DutFamily ?? "generic";
        if (!_session.TryConfirm(req, DutSerialInput, DutPartInput, DutRevisionInput, OperatorInput, family, out var error))
        {
            Status = error;
            return;
        }

        ShowSessionForm = false;
        RefreshSessionSummary();
        Status = $"Session confirmed: {_session.DutSerial}";
    }

    private void ConfirmSameDut()
    {
        _session.ConfirmSameDut();
        DutSerialInput = _session.DutSerial;
        DutPartInput = _session.DutPartNumber ?? string.Empty;
        DutRevisionInput = _session.DutRevision ?? string.Empty;
        OperatorInput = _session.OperatorName ?? string.Empty;
        ShowSessionForm = !_session.CanRun;
        RefreshSessionSummary();
        Status = _session.CanRun ? $"Still testing {_session.DutSerial}." : "Confirm DUT, then Run.";
    }

    private void ChangeSession()
    {
        _session.ChangeSession();
        DutSerialInput = string.Empty;
        DutPartInput = string.Empty;
        DutRevisionInput = string.Empty;
        ShowSessionForm = true;
        RefreshSessionSummary();
        Status = "Confirm DUT, then Run.";
    }

    private void RefreshRequirementFlags()
    {
        var req = SelectedProgram?.Requirements ?? ProgramRequirements.Sample;
        RequirePartNumber = req.RequirePartNumber;
        RequireRevision = req.RequireRevision;
        RequireOperator = req.RequireOperator;
    }

    private async Task RefreshProgramsAsync()
    {
        Programs.Clear();
        Programs.Add(new ProgramItemViewModel
        {
            Id = "sample",
            DisplayName = "Sample Hardware Suite",
            Path = SampleProgramFactory.EmbeddedName,
            DutFamily = "demo",
            IsSample = true,
            Requirements = ProgramRequirements.Sample,
        });

        foreach (var dir in EnumerateProgramDirectories())
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.TapPlan"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (Programs.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Programs.Add(new ProgramItemViewModel
                {
                    Id = id,
                    DisplayName = id,
                    Path = file,
                    DutFamily = "generic",
                    Requirements = ProgramRequirements.FromFamily("generic"),
                });
            }
        }

        SelectedProgram ??= Programs.FirstOrDefault();
        if (SelectedProgram is not null)
        {
            await LoadSelectedProgramAsync();
        }

        Status = $"Loaded {Programs.Count} program(s).";
        RefreshSessionSummary();
    }

    private static IEnumerable<string> EnumerateProgramDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Programs");
        var repoPlans = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "plans", "opentap"));
        if (Directory.Exists(repoPlans))
        {
            yield return repoPlans;
        }
    }

    private async Task LoadSelectedProgramAsync()
    {
        if (SelectedProgram is null)
        {
            return;
        }

        ApplyIdleStaleCheck();
        _session.SelectProgram(SelectedProgram.Id, SelectedProgram.Path, SelectedProgram.DisplayName, SelectedProgram.DutFamily);
        if (SelectedProgram.IsSample)
        {
            await _openTap.LoadSampleProgramAsync();
        }
        else
        {
            await _openTap.LoadPlanAsync(SelectedProgram.Path);
        }

        RebuildHierarchyFromHost();
        StationSlotSummary = _openTap.InstrumentSlots.Count == 0
            ? "Station: (no OpenTAP instruments)"
            : "Station: " + string.Join(", ", _openTap.InstrumentSlots.Select(s => $"{s.Name}→{s.ResourceName}"));
        RefreshSessionSummary();
    }

    private void RebuildHierarchyFromHost()
    {
        _fullHierarchy.Clear();
        Hierarchy.Clear();
        Stages.Clear();
        foreach (var node in _openTap.StepTree)
        {
            _fullHierarchy.Add(new HierarchyStepViewModel(node));
        }

        Stages.Add(new StageItemViewModel(null, "Entire program"));
        foreach (var root in _fullHierarchy)
        {
            foreach (var stage in EnumerateStages(root))
            {
                Stages.Add(new StageItemViewModel(stage, stage.Name));
            }
        }

        SelectedStage = Stages.FirstOrDefault();
        ApplyStageFilter();
    }

    private static IEnumerable<HierarchyStepViewModel> EnumerateStages(HierarchyStepViewModel root)
    {
        if (root.IsStage || root.Children.Count > 0)
        {
            if (root.Children.Count > 0 && root.Children.Any(c => c.IsStage || c.Children.Count > 0))
            {
                foreach (var child in root.Children.Where(c => c.IsStage || c.Children.Count > 0))
                {
                    yield return child;
                }
            }
            else
            {
                yield return root;
            }
        }
    }

    private void ApplyStageFilter()
    {
        Hierarchy.Clear();
        if (SelectedStage?.Step is null)
        {
            foreach (var root in _fullHierarchy)
            {
                Hierarchy.Add(root);
            }
        }
        else
        {
            Hierarchy.Add(SelectedStage.Step);
        }

        SelectedStep = FlattenHierarchy(Hierarchy).FirstOrDefault();
    }

    private async Task OpenPlanFileAsync()
    {
        if (!IsEngineerDebugMode)
        {
            Status = "Open arbitrary plan requires Engineer/Debug mode.";
            return;
        }

        if (RequestPlanFilePath is null)
        {
            Status = "File picker unavailable.";
            return;
        }

        var path = await RequestPlanFilePath(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var item = new ProgramItemViewModel
        {
            Id = Path.GetFileNameWithoutExtension(path),
            DisplayName = Path.GetFileNameWithoutExtension(path),
            Path = path,
            DutFamily = "generic",
            Requirements = ProgramRequirements.FromFamily("generic"),
        };
        Programs.Add(item);
        SelectedProgram = item;
        Status = $"Opened {item.DisplayName}";
    }

    private void ApplyDebugPatch()
    {
        if (!IsEngineerDebugMode || SelectedStep is null)
        {
            Status = "Select a step in Engineer/Debug mode.";
            return;
        }

        ClampDebugKnobs();
        _openTap.TrySetStepEnabled(SelectedStep.Path, DebugStepEnabled);
        SelectedStep.Enabled = DebugStepEnabled;
        _openTap.TrySetAcquireSettings(SelectedStep.Path, DebugSampleCount, DebugIntervalMs);
        _openTap.TrySetMeanGteThreshold(SelectedStep.Path, DebugThreshold);
        _openTap.TryRebindDmmResource(DebugResource);
        Status = $"Applied debug overlay to {SelectedStep.Name} (not saved to golden plan).";
    }

    private void ClampDebugKnobs()
    {
        DebugSampleCount = Math.Clamp(DebugSampleCount, 1, MaxDebugSampleCount);
        DebugIntervalMs = Math.Max(MinDebugIntervalMs, DebugIntervalMs);
    }

    private async Task ExecuteRunAsync(bool selectionOnly)
    {
        if (IsRunning)
        {
            Status = "Already running.";
            return;
        }

        ApplyIdleStaleCheck();
        if (!_session.CanRun)
        {
            ShowSessionForm = true;
            Status = _session.State == OperatorSessionState.Stale
                ? $"Still testing {_session.DutSerial}? Confirm Same DUT or Change Session."
                : "Confirm DUT to run.";
            RefreshSessionSummary();
            return;
        }

        if (SelectedProgram is null)
        {
            Status = "Select a program.";
            return;
        }

        if (selectionOnly && SelectedStep is null)
        {
            Status = "Select a stage or step to run.";
            return;
        }

        IsRunning = true;
        PlotUiFlushCount = 0;
        _lastUiFlushTicks = 0;
        ResetPlotBuffer();
        lock (_progressSync)
        {
            _pendingDetails.Clear();
            _pendingSample = null;
            _pendingStatus = null;
            _pendingForceFlush = false;
            _pendingAwaitingOperator = false;
        }

        DetailLines.Clear();
        OverallPercent = 0;
        var cts = new CancellationTokenSource();
        _runControl.AttachRun(cts);

        try
        {
            await LoadSelectedProgramAsync();
            if (IsEngineerDebugMode && SelectedStep is not null)
            {
                ApplyDebugPatch();
            }

            var station = BuildStationProfile();
            var unbound = _openTap.InstrumentSlots
                .Where(s => string.IsNullOrWhiteSpace(s.ResourceName)
                            && !station.RoleToResource.ContainsKey(s.RoleHint)
                            && !station.RoleToResource.ContainsKey(s.Name))
                .Select(s => s.Name)
                .ToList();
            if (unbound.Count > 0)
            {
                Status = $"Bind unbound instrument slots on Instruments page: {string.Join(", ", unbound)}";
                return;
            }

            await _openTap.ApplyStationAndDutAsync(station, _session.ToDutIdentity());
            _session.TouchActivity();

            var summary = selectionOnly
                ? await _openTap.RunSelectionAsync(SelectedStep!.Path, _progress, cts.Token).ConfigureAwait(false)
                : await _openTap.RunAsync(_progress, cts.Token).ConfigureAwait(false);

            lock (_progressSync)
            {
                _pendingForceFlush = true;
            }

            ScheduleUiFlush();
            await WaitForPendingFlushesAsync().ConfigureAwait(false);
            SyncHierarchyLive();

            LastRunId = summary.RunId;
            var record = new TestRunRecord
            {
                RunId = summary.RunId,
                PlanId = SelectedProgram.Id,
                PlanName = summary.PlanName,
                DutSerial = summary.DutSerial ?? _session.DutSerial,
                DutPartNumber = summary.DutPartNumber ?? _session.DutPartNumber,
                DutRevision = summary.DutRevision ?? _session.DutRevision,
                SessionId = _session.SessionId,
                OperatorName = _session.OperatorName,
                StartedAt = summary.StartedAt,
                CompletedAt = summary.CompletedAt,
                Result = summary.Result,
                ErrorMessage = summary.ErrorMessage,
                Samples = summary.Samples,
                Steps = summary.Steps,
            };
            await _runStore.SaveAsync(record).ConfigureAwait(false);

            if (summary.Result is RunResult.Passed or RunResult.Failed)
            {
                try
                {
                    await _reportService.GeneratePdfAsync(record).ConfigureAwait(false);
                    Status = $"Finished: {summary.Result}. Report generated.";
                }
                catch (Exception ex)
                {
                    Status = $"Finished: {summary.Result}. Report failed: {ex.Message}";
                }
            }
            else
            {
                Status = $"Finished: {summary.Result}";
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            _runControl.DetachRun();
            IsRunning = false;
            IsAwaitingOperator = false;
            OverallPercent = 100;
            cts.Dispose();
            RefreshSessionSummary();
        }
    }

    private StationProfile BuildStationProfile()
        => new(_settings.StationBindings.ToDictionary(
            b => b.Role,
            b =>
            {
                var instr = _settings.Instruments.FirstOrDefault(i =>
                    string.Equals(i.Id, b.InstrumentId, StringComparison.OrdinalIgnoreCase));
                return instr?.Resource ?? b.InstrumentId;
            },
            StringComparer.OrdinalIgnoreCase));

    public void OpenSelectedStepDetail() => OpenSelectedDetail();

    private void OpenSelectedDetail()
    {
        if (SelectedStep is null)
        {
            return;
        }

        DetailStep = SelectedStep;
        ShowDetailPane = true;
        DetailKeyValues.Clear();
        if (!string.IsNullOrWhiteSpace(SelectedStep.KeyValue))
        {
            DetailKeyValues.Add(SelectedStep.KeyValue);
        }

        DetailKeyValues.Add($"Status: {SelectedStep.StatusText}");
        DetailKeyValues.Add($"Verdict: {SelectedStep.Verdict}");
        DetailKeyValues.Add($"Path: {SelectedStep.Path}");
    }

    public void ContinueOperatorAttention() => ContinueOperator();

    private void ContinueOperator()
    {
        _openTap.Resume();
        _runControl.Resume();
        IsAwaitingOperator = false;
        OperatorPromptMessage = null;
        Status = "Continuing…";
    }

    private void Cancel()
    {
        _runControl.RequestSafetyStop();
        _openTap.Abort(safetyStop: true);
    }

    private void SyncHierarchyLive()
    {
        foreach (var root in _fullHierarchy)
        {
            root.SyncFromNode();
        }

        foreach (var stage in Stages)
        {
            if (stage.Step is null)
            {
                continue;
            }

            stage.StatusText = stage.Step.StatusText;
            stage.Verdict = stage.Step.Verdict;
            stage.KeyValue = stage.Step.KeyValue;
        }
    }

    private async Task WaitForPendingFlushesAsync()
    {
        for (var i = 0; i < 50; i++)
        {
            if (Volatile.Read(ref _flushScheduled) == 0)
            {
                bool needsForce;
                lock (_progressSync)
                {
                    needsForce = _pendingForceFlush || _pendingSample is not null || _pendingDetails.Count > 0;
                }

                if (!needsForce)
                {
                    return;
                }

                ScheduleUiFlush();
            }

            await Task.Delay(5).ConfigureAwait(false);
        }

        DrainUiFlush();
    }

    private void ScheduleUiFlush()
    {
        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0)
        {
            return;
        }

        PostToUi(DrainUiFlush);
    }

    private void PostToUi(Action action)
    {
        if (UiScheduler is not null)
        {
            UiScheduler(action);
            return;
        }

        try
        {
            var dispatcher = Dispatcher.UIThread;
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Post(action, DispatcherPriority.Background);
            }
        }
        catch (Exception)
        {
            action();
        }
    }

    private void DrainUiFlush()
    {
        var keepScheduledForDelay = false;
        try
        {
            while (true)
            {
                string? status;
                double percent;
                MeasurementSampleEvent? sample;
                bool force;
                bool awaiting;
                string? prompt;
                List<string>? details;
                lock (_progressSync)
                {
                    status = _pendingStatus;
                    percent = _pendingPercent;
                    sample = _pendingSample;
                    force = _pendingForceFlush;
                    awaiting = _pendingAwaitingOperator;
                    prompt = _pendingOperatorPrompt;
                    details = _pendingDetails.Count > 0 ? _pendingDetails.ToList() : null;
                    _pendingDetails.Clear();
                    _pendingForceFlush = false;
                    if (sample is not null)
                    {
                        _pendingSample = null;
                    }
                }

                var hz = Math.Clamp(_settings.PlotRefreshHz, 1, 120);
                var intervalTicks = Stopwatch.Frequency / hz;
                var now = Stopwatch.GetTimestamp();
                if (!force && _lastUiFlushTicks != 0 && now - _lastUiFlushTicks < intervalTicks)
                {
                    var delayMs = Math.Max(1, (int)((_lastUiFlushTicks + intervalTicks - now) * 1000.0 / Stopwatch.Frequency));
                    lock (_progressSync)
                    {
                        if (sample is not null)
                        {
                            _pendingSample ??= sample;
                        }

                        if (details is not null)
                        {
                            foreach (var line in details)
                            {
                                EnqueueDetail_NoLock(line);
                            }
                        }

                        if (force)
                        {
                            _pendingForceFlush = true;
                        }

                        if (awaiting)
                        {
                            _pendingAwaitingOperator = true;
                            _pendingOperatorPrompt = prompt;
                        }
                    }

                    keepScheduledForDelay = true;
                    _ = Task.Delay(delayMs).ContinueWith(
                        _ =>
                        {
                            Interlocked.Exchange(ref _flushScheduled, 0);
                            ScheduleUiFlush();
                        },
                        TaskScheduler.Default);
                    return;
                }

                _lastUiFlushTicks = now;
                OverallPercent = percent;
                if (status is not null)
                {
                    Status = _runControl.IsPaused || awaiting ? $"Paused — {status}" : status;
                }

                if (awaiting)
                {
                    IsAwaitingOperator = true;
                    OperatorPromptMessage = prompt;
                }

                if (details is not null)
                {
                    foreach (var line in details)
                    {
                        if (DetailLines.Count >= DetailCap)
                        {
                            DetailLines.RemoveAt(0);
                        }

                        DetailLines.Add(line);
                    }
                }

                SyncHierarchyLive();

                if (sample is not null && (ShowDetails || ShowDetailPane))
                {
                    AppendSampleToPlot(sample.Value);
                    PlotYs = _plotPublish;
                    PlotDataChanged?.Invoke(this, EventArgs.Empty);
                    PlotUiFlushCount++;
                }
                else if (force)
                {
                    PlotUiFlushCount++;
                }

                lock (_progressSync)
                {
                    if (_pendingForceFlush || _pendingSample is not null || _pendingDetails.Count > 0 || _pendingAwaitingOperator)
                    {
                        continue;
                    }
                }

                break;
            }
        }
        finally
        {
            if (!keepScheduledForDelay)
            {
                Interlocked.Exchange(ref _flushScheduled, 0);
                lock (_progressSync)
                {
                    if (_pendingForceFlush || _pendingSample is not null || _pendingDetails.Count > 0 || _pendingAwaitingOperator)
                    {
                        ScheduleUiFlush();
                    }
                }
            }
        }
    }

    private void EnqueueDetail_NoLock(string line)
    {
        while (_pendingDetails.Count >= DetailCap)
        {
            _pendingDetails.Dequeue();
        }

        _pendingDetails.Enqueue(line);
    }

    private void ResetPlotBuffer()
    {
        Array.Clear(_plotRing);
        Array.Clear(_plotPublish);
        _plotCount = 0;
        _plotWrite = 0;
        _plotPublishLength = 0;
        PlotYs = _plotPublish;
    }

    private void AppendSampleToPlot(double value)
    {
        _plotRing[_plotWrite] = value;
        _plotWrite = (_plotWrite + 1) % PlotCapacity;
        if (_plotCount < PlotCapacity)
        {
            _plotCount++;
        }

        var start = _plotCount == PlotCapacity ? _plotWrite : 0;
        for (var i = 0; i < _plotCount; i++)
        {
            _plotPublish[i] = _plotRing[(start + i) % PlotCapacity];
        }

        _plotPublishLength = _plotCount;
    }

    private void ApplyIdleStaleCheck()
    {
        var hours = Math.Max(1, _settings.OperatorSessionIdleHours);
        _session.CheckIdleStale(TimeSpan.FromHours(hours));
    }

    private void RefreshSessionSummary()
    {
        NeedsDutConfirm = _session.State == OperatorSessionState.NeedsDut;
        IsStalePrompt = _session.State == OperatorSessionState.Stale;
        ShowSessionForm = NeedsDutConfirm || IsStalePrompt || ShowSessionForm && !_session.CanRun;
        var program = _session.ProgramDisplayName ?? "(none)";
        if (_session.CanRun)
        {
            SessionSummary = $"DUT {_session.DutSerial} | {program}"
                             + (_session.OperatorName is { } op ? $" | Op {op}" : string.Empty);
            return;
        }

        if (_session.State == OperatorSessionState.Stale)
        {
            SessionSummary = $"DUT {_session.DutSerial} (re-confirm) | {program}";
            return;
        }

        SessionSummary = $"Session (confirm required) | {program}";
    }

    private static IEnumerable<HierarchyStepViewModel> FlattenHierarchy(IEnumerable<HierarchyStepViewModel> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in FlattenHierarchy(root.Children))
            {
                yield return child;
            }
        }
    }

    private sealed class ThrottledOpenTapProgress : IProgress<OpenTapProgress>
    {
        private readonly Action<OpenTapProgress> _ingest;
        public ThrottledOpenTapProgress(Action<OpenTapProgress> ingest) => _ingest = ingest;
        public void Report(OpenTapProgress value) => _ingest(value);
    }
}
