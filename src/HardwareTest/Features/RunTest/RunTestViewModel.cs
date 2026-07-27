using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Presentation;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using StepFilter = HardwareTest.Features.RunTest.StepStatusFilter;

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
        IsExpanded = node.IsStage || node.Children.Count > 0;
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
    [Reactive] private string? _extra;
    [Reactive] private string _attemptsText = string.Empty;
    [Reactive] private bool _isExpanded;
    [Reactive] private string _chipText = "Pending";
    [Reactive] private string _progressText = string.Empty;
    [Reactive] private double _progressPercent;
    [Reactive] private int _completedLeaves;
    [Reactive] private int _totalLeaves;
    [Reactive] private int _failedLeaves;

    public void SyncFromNode()
    {
        StatusText = Node.StatusText;
        Verdict = Node.Verdict;
        Enabled = Node.Enabled;
        KeyValue = Node.KeyValue;
        Extra = Node.KeyValue;
        ChipText = StatusChip.FromStatus(StatusText, Verdict);
        foreach (var child in Children)
        {
            child.SyncFromNode();
        }
    }

    public void ExpandAll()
    {
        IsExpanded = true;
        foreach (var child in Children)
        {
            child.ExpandAll();
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
    [Reactive] private string _chipText = "Pending";
    [Reactive] private string _progressText = "0/0";
    [Reactive] private double _progressPercent;
    [Reactive] private int _completedLeaves;
    [Reactive] private int _totalLeaves;
    [Reactive] private int _failedLeaves;
}

public partial class ProgramItemViewModel : ReactiveObject
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Path { get; init; }
    public string DutFamily { get; init; } = "generic";
    public bool IsSample { get; init; }
    public ProgramLoadKind LoadKind { get; init; } = ProgramLoadKind.TapPlanFile;
    public ProgramRequirements Requirements { get; init; } = ProgramRequirements.Sample;
    public IReadOnlyList<string> ReportKinds { get; init; } = [HardwareTest.Core.Runs.ReportKinds.Status];
}

public partial class RunTestViewModel : ReactiveObject
{
    private const int PlotCapacity = 2048;
    private const int DetailCap = 200;
    private const int MaxDetailLinesPerFlush = 16;
    private const int MaxDebugSampleCount = 4096;
    private const int MinDebugIntervalMs = 1;

    private readonly IOpenTapSession _openTap;
    private readonly OperatorSession _session;
    private readonly IRunControl _runControl;
    private readonly IReportService _reportService;
    private readonly IRunStore _runStore;
    private readonly AppSettings _settings;
    private readonly ISettingsStore? _settingsStore;
    private readonly IDutHistoryService? _dutHistory;
    private readonly object _progressSync = new();
    private readonly Queue<string> _pendingDetails = new();
    private readonly double[] _plotRing = new double[PlotCapacity];
    private readonly double[] _plotPublish = new double[PlotCapacity];
    private readonly ThrottledOpenTapProgress _progress;
    private readonly List<HierarchyStepViewModel> _fullHierarchy = [];
    private readonly Dictionary<string, StepAttemptSummary> _attemptLedger = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _stepsWithSamples = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PresentationTileViewModel> _gaugeTiles = [];

    private long _lastUiFlushTicks;
    private MeasurementSampleEvent? _pendingSample;
    private string? _pendingSampleStepPath;
    private string? _pendingStatus;
    private double _pendingPercent;
    private bool _pendingForceFlush;
    private bool _pendingAwaitingOperator;
    private string? _pendingOperatorPrompt;
    private OperatorInteractionRequest? _pendingInteractionRequest;
    private string? _pendingStepId;
    private string? _pendingStepPath;
    private string? _pendingStepName;
    private string? _pendingStatusText;
    private string? _pendingVerdict;
    private string? _pendingKeyValue;
    private string? _pendingIterationText;
    private int _plotCount;
    private int _plotWrite;
    private int _plotPublishLength;
    private int _flushScheduled;
    private bool _suppressStageFilter;
    private bool _suppressSubsectionFilter;
    private bool _suppressNestedFilter;

    public int PlotUiFlushCount { get; private set; }
    public int PlotYsLength => _plotPublishLength;
    public Action<Action>? UiScheduler { get; set; }
    public event EventHandler? PlotDataChanged;
    public event EventHandler? NavigateToResultsRequested;
    public event EventHandler? NavigateToInspectRequested;
    public event EventHandler? RequestScrollToSelectedStep;
    public event EventHandler? RequestFocusStepSearch;

    public RunTestViewModel(
        IOpenTapSession openTap,
        OperatorSession session,
        IRunControl runControl,
        IReportService reportService,
        IRunStore runStore,
        AppSettings settings,
        ISettingsStore? settingsStore = null,
        IDutHistoryService? dutHistory = null)
    {
        _openTap = openTap;
        _session = session;
        _runControl = runControl;
        _reportService = reportService;
        _runStore = runStore;
        _settings = settings;
        _settingsStore = settingsStore;
        _dutHistory = dutHistory;
        _progress = new ThrottledOpenTapProgress(IngestProgress);
        Status = "Confirm DUT, then Run.";
        Programs = [];
        Hierarchy = [];
        Stages = [];
        StepRows = [];
        Subsections = [];
        NestedSubsections = [];
        StepListItems = [];
        DetailLines = [];
        DetailKeyValues = [];
        InteractionFields = [];
        ParameterFields = [];
        PresentationTiles = [];
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
        OpenStepDetailCommand = ReactiveCommand.Create(() => OpenSelectedDetail(revealDetail: true));
        CloseDetailCommand = ReactiveCommand.Create(() => { ShowDetailRegion = false; });
        ToggleDetailsCommand = ReactiveCommand.Create(() => { ShowDetailRegion = !ShowDetailRegion; });
        ApplyDebugPatchCommand = ReactiveCommand.Create(ApplyDebugPatch);
        ApplyParametersCommand = ReactiveCommand.CreateFromTask(ApplyParametersAsync);
        OpenLastRunResultsCommand = ReactiveCommand.Create(() => NavigateToResultsRequested?.Invoke(this, EventArgs.Empty));
        InspectPlanCommand = ReactiveCommand.Create(() => NavigateToInspectRequested?.Invoke(this, EventArgs.Empty));
        NextFailCommand = ReactiveCommand.Create(NextFail);
        PrevFailCommand = ReactiveCommand.Create(PrevFail);
        JumpToCurrentCommand = ReactiveCommand.Create(JumpToCurrent);
        ClearSubsectionCommand = ReactiveCommand.Create(ClearSubsection);
        FilterFailCommand = ReactiveCommand.Create(FilterFail);
        ToggleCompactCommand = ReactiveCommand.Create(ToggleCompact);
        FocusStepSearchCommand = ReactiveCommand.Create(
            () => RequestFocusStepSearch?.Invoke(this, EventArgs.Empty));
        SetStepFilterCommand = ReactiveCommand.Create<string>(filter =>
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                StepStatusFilter = filter;
            }
        });

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
            else if (args.PropertyName == nameof(SelectedStage) && !_suppressStageFilter)
            {
                _suppressSubsectionFilter = true;
                try
                {
                    SelectedSubsection = null;
                }
                finally
                {
                    _suppressSubsectionFilter = false;
                }

                ApplyStageFilter();
            }
            else if (args.PropertyName == nameof(SelectedSubsection) && !_suppressSubsectionFilter)
            {
                _suppressNestedFilter = true;
                try
                {
                    SelectedNestedSubsection = null;
                }
                finally
                {
                    _suppressNestedFilter = false;
                }

                RebuildNestedSubsections();
                RebuildStepRows();
                ResolveSelectedStep();
            }
            else if (args.PropertyName == nameof(SelectedNestedSubsection) && !_suppressNestedFilter)
            {
                RebuildStepRows();
                ResolveSelectedStep();
            }
            else if (args.PropertyName is nameof(StepStatusFilter) or nameof(StepSearchText))
            {
                RebuildVisibleStepList();
            }
            else if (args.PropertyName == nameof(SelectedStepListItem))
            {
                if (SelectedStepListItem?.Step is not null)
                {
                    SelectedStep = SelectedStepListItem.Step;
                }
            }
            else if (args.PropertyName == nameof(SelectedStep) && SelectedStep is not null)
            {
                DebugStepEnabled = SelectedStep.Enabled;
                OpenSelectedDetail(revealDetail: false);
                RefreshParameterFields();
                RefreshPlotVisibility();
                RefreshPresentationTiles();
                RefreshHero();
            }
            else if (args.PropertyName == nameof(IsEngineerDebugMode))
            {
                RefreshParameterFields();
            }
            else if (args.PropertyName is nameof(IsRunning) or nameof(IsAwaitingOperator) or nameof(Status))
            {
                RefreshHero();
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
    public ObservableCollection<HierarchyStepViewModel> StepRows { get; }
    public ObservableCollection<StageItemViewModel> Stages { get; }
    public ObservableCollection<StageItemViewModel> Subsections { get; }
    public ObservableCollection<StageItemViewModel> NestedSubsections { get; }
    public ObservableCollection<StepListItemViewModel> StepListItems { get; }
    public ObservableCollection<string> DetailLines { get; }
    public ObservableCollection<string> DetailKeyValues { get; }
    public ObservableCollection<InteractionFieldViewModel> InteractionFields { get; }
    public ObservableCollection<InteractionFieldViewModel> ParameterFields { get; }
    public ObservableCollection<PresentationTileViewModel> PresentationTiles { get; }
    public ObservableCollection<string> AttemptHistoryLines { get; } = [];
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
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ApplyParametersCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenLastRunResultsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> InspectPlanCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadPlanCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> StartCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadSampleSuiteCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NextFailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> PrevFailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> JumpToCurrentCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ClearSubsectionCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> FilterFailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleCompactCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> FocusStepSearchCommand { get; }
    public ReactiveCommand<string, System.Reactive.Unit> SetStepFilterCommand { get; }

    [Reactive] private ProgramItemViewModel? _selectedProgram;
    [Reactive] private StageItemViewModel? _selectedStage;
    [Reactive] private StageItemViewModel? _selectedSubsection;
    [Reactive] private StageItemViewModel? _selectedNestedSubsection;
    [Reactive] private HierarchyStepViewModel? _selectedStep;
    [Reactive] private StepListItemViewModel? _selectedStepListItem;
    [Reactive] private string _stepStatusFilter = StepFilter.All;
    [Reactive] private string _stepSearchText = string.Empty;
    [Reactive] private bool _hasNestedSubsections;
    [Reactive] private bool _compactStepRows;
    [Reactive] private string _breadcrumbText = "Entire program";
    [Reactive] private string _breadcrumbDetailText = string.Empty;
    [Reactive] private int _suitePassedCount;
    [Reactive] private int _suiteFailedCount;
    [Reactive] private int _suitePendingCount;
    [Reactive] private HierarchyStepViewModel? _detailStep;
    [Reactive] private string _dutSerialInput = string.Empty;
    [Reactive] private string _dutPartInput = string.Empty;
    [Reactive] private string _dutRevisionInput = string.Empty;
    [Reactive] private string _operatorInput = string.Empty;
    [Reactive] private bool _requirePartNumber;
    [Reactive] private bool _requireRevision;
    [Reactive] private bool _requireOperator = true;
    [Reactive] private bool _showSessionForm = true;
    [Reactive] private bool _sessionBlocked = true;
    [Reactive] private bool _showDetailRegion = true;
    [Reactive] private bool _showLiveLog;
    [Reactive] private bool _hasPlotData;
    [Reactive] private bool _showPlotForSelection;
    [Reactive] private bool _hasPresentationTiles;
    [Reactive] private string _plotLegendText = "Channel";
    [Reactive] private string _plotYLabel = "Value";
    [Reactive] private string _plotTitle = "Live measurements";
    [Reactive] private bool _sessionLogExpanded;
    [Reactive] private string _attemptSummaryChip = string.Empty;
    [Reactive] private string _status = string.Empty;
    [Reactive] private string _historyBanner = string.Empty;
    [Reactive] private bool _isRunning;
    [Reactive] private string? _lastRunId;
    [Reactive] private double _overallPercent;
    [Reactive] private bool _showDetails = true;
    [Reactive] private double[] _plotYs = Array.Empty<double>();
    [Reactive] private string _sessionSummary = "Session: (confirm required)";
    [Reactive] private bool _needsDutConfirm = true;
    [Reactive] private bool _isStalePrompt;
    [Reactive] private bool _isAwaitingOperator;
    [Reactive] private string? _operatorPromptMessage;
    [Reactive] private string _interactionTitle = "Operator attention";
    [Reactive] private bool _hasInteractionFields;
    [Reactive] private string? _interactionValidationError;
    [Reactive] private bool _hasParameterFields;
    [Reactive] private bool _isEngineerDebugMode;
    [Reactive] private string _debugResource = "MOCK::INSTR0";
    [Reactive] private int _debugSampleCount = 32;
    [Reactive] private int _debugIntervalMs = 5;
    [Reactive] private double _debugThreshold;
    [Reactive] private bool _debugStepEnabled = true;
    [Reactive] private string _stationSlotSummary = "Station: (load program)";
    [Reactive] private string _currentStepName = string.Empty;
    [Reactive] private string _currentStepPath = string.Empty;
    [Reactive] private string _heroLabel = "SELECTED:";
    [Reactive] private string _heroStepName = string.Empty;
    [Reactive] private string _heroChipText = "Pending";
    [Reactive] private string _heroStatusLine = string.Empty;
    [Reactive] private string _iterationText = string.Empty;
    [Reactive] private string _detailChipText = "Pending";
    [Reactive] private string _detailPrimaryLine = string.Empty;
    [Reactive] private string _conditionSummary = string.Empty;
    [Reactive] private bool _hasSubsections;
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

            if (!string.IsNullOrWhiteSpace(progress.StepName))
            {
                EnqueueDetail_NoLock($"{progress.StepName}: {progress.Message}");
            }

            if (progress.AwaitingOperator)
            {
                _pendingAwaitingOperator = true;
                _pendingOperatorPrompt = progress.OperatorPromptMessage ?? progress.Message;
                _pendingInteractionRequest = progress.InteractionRequest ?? _openTap.PendingInteraction;
            }

            _pendingStepId = progress.StepId ?? _pendingStepId;
            _pendingStepPath = progress.StepPath ?? _pendingStepPath;
            _pendingStepName = progress.StepName ?? _pendingStepName;
            _pendingStatusText = progress.StatusText ?? _pendingStatusText;
            _pendingVerdict = progress.Verdict ?? _pendingVerdict;
            _pendingKeyValue = progress.KeyValue ?? _pendingKeyValue;
            if (progress.IterationText is not null || progress.IterationIndex is not null)
            {
                _pendingIterationText = progress.IterationText
                    ?? OpenTapLoopProgress.FormatIteration(
                        progress.IterationIndex ?? 0,
                        progress.IterationTotal);
                _pendingForceFlush = true;
            }

            if (progress.Sample is null
                && (!string.IsNullOrWhiteSpace(progress.StepPath) || !string.IsNullOrWhiteSpace(progress.StepName))
                && (!string.IsNullOrWhiteSpace(progress.StatusText)
                    || !string.IsNullOrWhiteSpace(progress.Verdict)
                    || progress.AwaitingOperator))
            {
                _pendingForceFlush = true;
            }

            if (progress.Sample is not null && !string.IsNullOrWhiteSpace(progress.StepPath))
            {
                _pendingSampleStepPath = progress.StepPath;
            }

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
            SessionBlocked = true;
            return;
        }

        ShowSessionForm = false;
        RefreshSessionSummary();
        Status = $"Session confirmed: {_session.DutSerial} / {_session.OperatorName}";
    }

    private void ConfirmSameDut()
    {
        if (string.IsNullOrWhiteSpace(OperatorInput) && string.IsNullOrWhiteSpace(_session.OperatorName))
        {
            Status = "Technician name is required.";
            SessionBlocked = true;
            ShowSessionForm = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(OperatorInput))
        {
            _session.OperatorName = OperatorInput.Trim();
        }

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
        _attemptLedger.Clear();
        ClearAttemptTexts();
        DutSerialInput = string.Empty;
        DutPartInput = string.Empty;
        DutRevisionInput = string.Empty;
        OperatorInput = string.Empty;
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
        foreach (var entry in ProgramCatalog.Enumerate())
        {
            Programs.Add(new ProgramItemViewModel
            {
                Id = entry.Id,
                DisplayName = entry.DisplayName,
                Path = entry.Path,
                DutFamily = entry.DutFamily,
                IsSample = entry.IsBuiltIn,
                LoadKind = entry.LoadKind,
                Requirements = entry.Requirements,
                ReportKinds = entry.ReportKinds,
            });
        }

        SelectedProgram ??= Programs.FirstOrDefault();
        if (SelectedProgram is not null)
        {
            await LoadSelectedProgramAsync();
        }

        Status = $"Loaded {Programs.Count} program(s).";
        RefreshSessionSummary();
    }

    private async Task LoadSelectedProgramAsync(string? preserveStagePath = null, string? preserveStepPath = null)
    {
        if (SelectedProgram is null)
        {
            return;
        }

        ApplyIdleStaleCheck();
        _session.SelectProgram(SelectedProgram.Id, SelectedProgram.Path, SelectedProgram.DisplayName, SelectedProgram.DutFamily);

        var alreadyLoaded = string.Equals(_openTap.LoadedPlanPath, SelectedProgram.Path, StringComparison.OrdinalIgnoreCase);

        if (!alreadyLoaded)
        {
            await LoadProgramEntryAsync(SelectedProgram).ConfigureAwait(false);
            RebuildHierarchyFromHost(preserveStagePath, preserveStepPath);
        }
        else if (Hierarchy.Count == 0 && _fullHierarchy.Count == 0)
        {
            RebuildHierarchyFromHost(preserveStagePath, preserveStepPath);
        }
        else if (!string.IsNullOrWhiteSpace(preserveStepPath))
        {
            // Keep Hierarchy instances; only re-resolve selection by path.
            ResolveSelectedStep(preserveStepPath);
        }
        else if (!string.IsNullOrWhiteSpace(preserveStagePath))
        {
            RestoreSelection(preserveStagePath, preserveStepPath);
        }

        StationSlotSummary = _openTap.InstrumentSlots.Count == 0
            ? "Station: (no OpenTAP instruments)"
            : "Station: " + string.Join(", ", _openTap.InstrumentSlots.Select(s => $"{s.Name}→{s.ResourceName}"));
        ApplySavedParameterOverrides();
        RefreshParameterFields();
        RefreshSessionSummary();
    }

    private Task LoadProgramEntryAsync(ProgramItemViewModel program)
        => program.LoadKind switch
        {
            ProgramLoadKind.FactorySample => _openTap.LoadSampleProgramAsync(),
            ProgramLoadKind.FactoryBoardDemo => _openTap.LoadBoardDemoProgramAsync(),
            ProgramLoadKind.FactorySweepDemo => _openTap.LoadSweepDemoProgramAsync(),
            _ => _openTap.LoadPlanAsync(program.Path),
        };

    private void RebuildHierarchyFromHost(string? preserveStagePath = null, string? preserveStepPath = null)
    {
        _fullHierarchy.Clear();
        Hierarchy.Clear();
        Stages.Clear();
        Subsections.Clear();
        NestedSubsections.Clear();
        StepRows.Clear();
        StepListItems.Clear();
        foreach (var node in _openTap.StepTree)
        {
            _fullHierarchy.Add(new HierarchyStepViewModel(node));
        }

        foreach (var root in _fullHierarchy)
        {
            Hierarchy.Add(root);
        }

        Stages.Add(new StageItemViewModel(null, "Entire program"));
        foreach (var root in _fullHierarchy)
        {
            foreach (var stage in EnumerateStages(root))
            {
                Stages.Add(new StageItemViewModel(stage, stage.Name));
            }
        }

        RollupParentStatuses();
        RestoreSelection(preserveStagePath, preserveStepPath);
    }

    private void RestoreSelection(string? preserveStagePath, string? preserveStepPath)
    {
        _suppressStageFilter = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(preserveStagePath))
            {
                SelectedStage = Stages.FirstOrDefault(s =>
                    string.Equals(s.Path, preserveStagePath, StringComparison.OrdinalIgnoreCase))
                    ?? Stages.FirstOrDefault();
            }
            else if (SelectedStage is null || !Stages.Contains(SelectedStage))
            {
                SelectedStage = Stages.FirstOrDefault();
            }
        }
        finally
        {
            _suppressStageFilter = false;
        }

        ApplyStageFilter(preserveStepPath);
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

    private HierarchyStepViewModel? ActiveScopeStep
        => SelectedNestedSubsection?.Step ?? SelectedSubsection?.Step ?? SelectedStage?.Step;

    private void ApplyStageFilter(string? preserveStepPath = null)
    {
        RebuildSubsections();
        RebuildStepRows();
        ResolveSelectedStep(preserveStepPath);
        ApplyAttemptTexts();
        RefreshHero();
    }

    private void RebuildSubsections()
    {
        Subsections.Clear();
        var stageStep = SelectedStage?.Step;
        if (stageStep is null)
        {
            HasSubsections = false;
            ClearNestedSubsectionsInternal();
            return;
        }

        foreach (var child in stageStep.Children.Where(c => c.IsStage || c.Children.Count > 0))
        {
            var item = new StageItemViewModel(child, child.Name);
            HierarchyRollup.ApplyToStage(item, _fullHierarchy);
            Subsections.Add(item);
        }

        HasSubsections = Subsections.Count > 0;
        ClearNestedSubsectionsInternal();
    }

    private void ClearNestedSubsectionsInternal()
    {
        NestedSubsections.Clear();
        HasNestedSubsections = false;
        _suppressNestedFilter = true;
        try
        {
            SelectedNestedSubsection = null;
        }
        finally
        {
            _suppressNestedFilter = false;
        }
    }

    private void RebuildNestedSubsections()
    {
        NestedSubsections.Clear();
        var subStep = SelectedSubsection?.Step;
        if (subStep is null)
        {
            HasNestedSubsections = false;
            return;
        }

        foreach (var child in subStep.Children.Where(c => c.IsStage || c.Children.Count > 0))
        {
            var item = new StageItemViewModel(child, child.Name);
            HierarchyRollup.ApplyToStage(item, _fullHierarchy);
            NestedSubsections.Add(item);
        }

        HasNestedSubsections = NestedSubsections.Count > 0;
    }

    private void RebuildStepRows() => RebuildVisibleStepList();

    private void RebuildVisibleStepList()
    {
        var scope = ActiveScopeStep;
        var items = new List<StepListItemViewModel>();

        if (scope is null)
        {
            // Entire program: keep stage / section markers across the full hierarchy.
            foreach (var root in _fullHierarchy)
            {
                AppendScopeWithMarkers(root, items, pathPrefix: null);
            }
        }
        else
        {
            var useSections = SelectedSubsection is null
                && SelectedNestedSubsection is null
                && scope.Children.Any(c => c.IsStage || c.Children.Count > 0);

            if (useSections)
            {
                AppendScopeWithMarkers(scope, items, pathPrefix: null);
            }
            else
            {
                foreach (var leaf in FilterLeaves(HierarchyRollup.EnumerateLeaves([scope])))
                {
                    items.Add(StepListItemViewModel.Leaf(leaf));
                }
            }
        }

        StepListItems.Clear();
        foreach (var item in items)
        {
            StepListItems.Add(item);
        }

        StepRows.Clear();
        foreach (var item in items)
        {
            if (!item.IsHeader && item.Step is not null)
            {
                StepRows.Add(item.Step);
            }
        }

        RefreshBreadcrumb();
        SyncSelectedStepListItem();
    }

    private void AppendScopeWithMarkers(
        HierarchyStepViewModel node,
        List<StepListItemViewModel> items,
        string? pathPrefix)
    {
        List<HierarchyStepViewModel>? pendingDirect = null;

        void FlushDirectLeaves()
        {
            if (pendingDirect is null)
            {
                return;
            }

            var leaves = FilterLeaves(pendingDirect);
            pendingDirect = null;
            if (leaves.Count == 0)
            {
                return;
            }

            // At the suite root, keep ungrouped leaves inline between stage blocks (no fake "suite" header
            // that would push them to the bottom). Under a named section path, group them with that header.
            if (!string.IsNullOrWhiteSpace(pathPrefix))
            {
                items.Add(StepListItemViewModel.Header(pathPrefix, node));
            }

            foreach (var leaf in leaves)
            {
                items.Add(StepListItemViewModel.Leaf(leaf));
            }
        }

        foreach (var child in node.Children)
        {
            var isSection = child.IsStage || child.Children.Count > 0;
            if (!isSection)
            {
                pendingDirect ??= [];
                pendingDirect.Add(child);
                continue;
            }

            FlushDirectLeaves();

            var header = string.IsNullOrWhiteSpace(pathPrefix)
                ? child.Name
                : $"{pathPrefix} / {child.Name}";

            if (child.Children.Any(c => c.IsStage || c.Children.Count > 0))
            {
                AppendScopeWithMarkers(child, items, header);
                continue;
            }

            var sectionLeaves = FilterLeaves(HierarchyRollup.EnumerateLeaves([child]));
            if (sectionLeaves.Count == 0)
            {
                continue;
            }

            items.Add(StepListItemViewModel.Header(header, child));
            foreach (var leaf in sectionLeaves)
            {
                items.Add(StepListItemViewModel.Leaf(leaf));
            }
        }

        FlushDirectLeaves();
    }

    private List<HierarchyStepViewModel> FilterLeaves(IEnumerable<HierarchyStepViewModel> leaves)
    {
        IEnumerable<HierarchyStepViewModel> query = leaves;
        if (!string.Equals(StepStatusFilter, StepFilter.All, StringComparison.Ordinal))
        {
            query = query.Where(l =>
                string.Equals(StatusChip.FromStatus(l.StatusText, l.Verdict), StepStatusFilter, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(StepSearchText))
        {
            var term = StepSearchText.Trim();
            query = query.Where(l =>
                l.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || l.Path.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    private void ResolveSelectedStep(string? preserveStepPath = null)
    {
        var listSteps = StepListItems.Where(i => i.Step is not null).Select(i => i.Step!).ToList();
        var flat = StepRows.Count > 0 ? StepRows.ToList() : listSteps;
        if (flat.Count == 0)
        {
            flat = HierarchyRollup.EnumerateLeaves(_fullHierarchy).ToList();
        }

        if (!string.IsNullOrWhiteSpace(preserveStepPath))
        {
            var match = listSteps.FirstOrDefault(s =>
                            string.Equals(s.Path, preserveStepPath, StringComparison.OrdinalIgnoreCase))
                        ?? flat.FirstOrDefault(s =>
                            string.Equals(s.Path, preserveStepPath, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                SelectedStep = match;
                SyncSelectedStepListItem();
                return;
            }
        }

        if (SelectedStep is not null && listSteps.Any(s => ReferenceEquals(s, SelectedStep)))
        {
            SyncSelectedStepListItem();
            return;
        }

        if (SelectedStep is not null)
        {
            var byPath = listSteps.FirstOrDefault(s =>
                             string.Equals(s.Path, SelectedStep.Path, StringComparison.OrdinalIgnoreCase))
                         ?? flat.FirstOrDefault(s =>
                             string.Equals(s.Path, SelectedStep.Path, StringComparison.OrdinalIgnoreCase));
            if (byPath is not null)
            {
                SelectedStep = byPath;
                SyncSelectedStepListItem();
                return;
            }
        }

        SelectedStep = flat.FirstOrDefault();
        SyncSelectedStepListItem();
    }

    private void RollupParentStatuses()
    {
        HierarchyRollup.Apply(_fullHierarchy);
        foreach (var stage in Stages)
        {
            HierarchyRollup.ApplyToStage(stage, _fullHierarchy);
        }

        foreach (var subsection in Subsections)
        {
            HierarchyRollup.ApplyToStage(subsection, _fullHierarchy);
        }

        foreach (var nested in NestedSubsections)
        {
            HierarchyRollup.ApplyToStage(nested, _fullHierarchy);
        }

        ReorderStagesByPriority();
        RefreshSuiteSummary();
        RebuildVisibleStepList();
    }

    private void ReorderStagesByPriority()
    {
        if (Stages.Count < 2)
        {
            return;
        }

        var ordered = Stages
            .Select((stage, index) => (stage, index))
            .OrderBy(x => StagePriority(x.stage))
            .ThenBy(x => x.index)
            .Select(x => x.stage)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            if (!ReferenceEquals(Stages[i], ordered[i]))
            {
                Stages.Move(Stages.IndexOf(ordered[i]), i);
            }
        }
    }

    private static int StagePriority(StageItemViewModel stage)
    {
        if (stage.Step is null)
        {
            return 0;
        }

        return stage.ChipText == "Fail" ? 1 : 2;
    }

    private void RefreshSuiteSummary()
    {
        var chips = HierarchyRollup.EnumerateLeaves(_fullHierarchy)
            .Select(l => StatusChip.FromStatus(l.StatusText, l.Verdict))
            .ToList();
        SuitePassedCount = chips.Count(c => c == "Pass");
        SuiteFailedCount = chips.Count(c => c == "Fail");
        SuitePendingCount = chips.Count(c => c is not "Pass" and not "Fail");
    }

    private void RefreshBreadcrumb()
    {
        var parts = new List<string>
        {
            SelectedStage?.Step is null ? "Entire program" : SelectedStage.DisplayName,
        };

        if (SelectedSubsection is not null)
        {
            parts.Add(SelectedSubsection.DisplayName);
        }

        if (SelectedNestedSubsection is not null)
        {
            parts.Add(SelectedNestedSubsection.DisplayName);
        }

        BreadcrumbText = string.Join(" › ", parts);

        var activeItem = SelectedNestedSubsection ?? SelectedSubsection ?? SelectedStage;
        BreadcrumbDetailText = activeItem is null
            ? string.Empty
            : $"({HierarchyRollup.FormatProgressText(activeItem.CompletedLeaves, activeItem.TotalLeaves, activeItem.FailedLeaves)})";
    }

    private void MaybeAutoFocusFail()
    {
        var scope = ActiveScopeStep;
        IEnumerable<HierarchyStepViewModel> roots = scope is null ? _fullHierarchy : [scope];
        var firstFail = HierarchyRollup.EnumerateLeaves(roots)
            .FirstOrDefault(l => StatusChip.FromStatus(l.StatusText, l.Verdict) == "Fail");
        if (firstFail is null)
        {
            return;
        }

        SelectedStep = firstFail;
        SyncSelectedStepListItem();
        OpenSelectedDetail(revealDetail: true);
    }

    private void NextFail() => CycleFail(forward: true);

    private void PrevFail() => CycleFail(forward: false);

    private void CycleFail(bool forward)
    {
        var scope = ActiveScopeStep;
        IEnumerable<HierarchyStepViewModel> roots = scope is null ? _fullHierarchy : [scope];
        var fails = HierarchyRollup.EnumerateLeaves(roots)
            .Where(l => StatusChip.FromStatus(l.StatusText, l.Verdict) == "Fail")
            .ToList();
        if (fails.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedStep is null ? -1 : fails.FindIndex(f => ReferenceEquals(f, SelectedStep));
        int nextIndex;
        if (currentIndex < 0)
        {
            nextIndex = forward ? 0 : fails.Count - 1;
        }
        else
        {
            nextIndex = forward
                ? (currentIndex + 1) % fails.Count
                : (currentIndex - 1 + fails.Count) % fails.Count;
        }

        SelectedStep = fails[nextIndex];
        SyncSelectedStepListItem();
        OpenSelectedDetail(revealDetail: true);
    }

    private void SyncSelectedStepListItem()
    {
        var match = StepListItems.FirstOrDefault(i => ReferenceEquals(i.Step, SelectedStep));
        if (match is not null)
        {
            SelectedStepListItem = match;
        }
    }

    private void JumpToCurrent()
    {
        if (string.IsNullOrWhiteSpace(CurrentStepPath))
        {
            return;
        }

        var match = FlattenHierarchy(_fullHierarchy)
            .FirstOrDefault(s => string.Equals(s.Path, CurrentStepPath, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        SelectScopeForStep(match);
        if (!StepRows.Any(r => ReferenceEquals(r, match)))
        {
            StepStatusFilter = StepFilter.All;
        }

        SelectedStep = match;
        SyncSelectedStepListItem();
        RequestScrollToSelectedStep?.Invoke(this, EventArgs.Empty);
    }

    private void ClearSubsection() => SelectedSubsection = null;

    public void ClearNestedSubsection() => SelectedNestedSubsection = null;

    private void FilterFail()
    {
        var entire = Stages.FirstOrDefault(s => s.Step is null);
        if (entire is not null)
        {
            SelectedStage = entire;
        }

        StepStatusFilter = StepFilter.Fail;
    }

    private void ToggleCompact() => CompactStepRows = !CompactStepRows;

    private void SelectScopeForStep(HierarchyStepViewModel leaf)
    {
        var containingStage = Stages.FirstOrDefault(s => s.Step is not null && IsWithin(s.Step, leaf))
            ?? Stages.FirstOrDefault(s => s.Step is null);
        if (containingStage is not null && !ReferenceEquals(SelectedStage, containingStage))
        {
            SelectedStage = containingStage;
        }

        var containingSubsection = Subsections.FirstOrDefault(s => s.Step is not null && IsWithin(s.Step, leaf));
        if (!ReferenceEquals(SelectedSubsection, containingSubsection))
        {
            SelectedSubsection = containingSubsection;
        }

        var containingNested = NestedSubsections.FirstOrDefault(s => s.Step is not null && IsWithin(s.Step, leaf));
        if (!ReferenceEquals(SelectedNestedSubsection, containingNested))
        {
            SelectedNestedSubsection = containingNested;
        }
    }

    private static bool IsWithin(HierarchyStepViewModel scope, HierarchyStepViewModel leaf)
        => ReferenceEquals(scope, leaf)
           || leaf.Path.StartsWith(scope.Path + "/", StringComparison.OrdinalIgnoreCase);

    public void ApplySelectionFromInspect(string? stepPath)
    {
        if (string.IsNullOrWhiteSpace(stepPath))
        {
            return;
        }

        var leaf = FlattenHierarchy(_fullHierarchy)
            .FirstOrDefault(s => s.Children.Count == 0
                && string.Equals(s.Path, stepPath, StringComparison.OrdinalIgnoreCase));
        if (leaf is null)
        {
            return;
        }

        SelectScopeForStep(leaf);
        if (!StepRows.Any(r => ReferenceEquals(r, leaf)))
        {
            StepStatusFilter = StepFilter.All;
        }

        SelectedStep = leaf;
        SyncSelectedStepListItem();
        RebuildVisibleStepList();
    }

    private void RefreshHero()
    {
        HeroLabel = IsRunning ? "CURRENT:" : "SELECTED:";
        if (IsRunning && !string.IsNullOrWhiteSpace(CurrentStepName))
        {
            HeroStepName = CurrentStepName;
            var live = FindStepVm(
                FlattenHierarchy(_fullHierarchy).ToList(),
                stepId: null,
                CurrentStepPath,
                CurrentStepName);
            if (live is not null)
            {
                HeroChipText = StatusChip.FromStatus(live.StatusText, live.Verdict);
            }
            else if (IsAwaitingOperator)
            {
                HeroChipText = "Awaiting";
            }
            else
            {
                HeroChipText = "Running";
            }
        }
        else if (SelectedStep is not null)
        {
            HeroStepName = SelectedStep.Name;
            HeroChipText = StatusChip.FromStatus(SelectedStep.StatusText, SelectedStep.Verdict);
        }
        else
        {
            HeroStepName = string.Empty;
            HeroChipText = "Pending";
        }

        if (IsAwaitingOperator)
        {
            // Prompt lives in the operator card; keep hero free of duplicate prose.
            HeroStatusLine = string.Empty;
            if (HeroChipText is not "Fail")
            {
                HeroChipText = "Awaiting";
            }
        }
        else if (string.IsNullOrWhiteSpace(IterationText))
        {
            HeroStatusLine = Status;
        }
        else if (string.IsNullOrWhiteSpace(Status))
        {
            HeroStatusLine = $"iter {IterationText}";
        }
        else
        {
            HeroStatusLine = $"{Status} · iter {IterationText}";
        }
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
            LoadKind = ProgramLoadKind.TapPlanFile,
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
        RefreshParameterFields();
        Status = $"Applied debug overlay to {SelectedStep.Name} (not saved to golden plan).";
    }

    private void RefreshParameterFields()
    {
        ParameterFields.Clear();
        if (!IsEngineerDebugMode || SelectedStep is null)
        {
            HasParameterFields = false;
            return;
        }

        var parameters = _openTap.EnumerateParameters(
            OpenTapParameterScope.Step,
            SelectedStep.Path,
            includeReadOnly: true,
            listing: OpenTapParameterListing.StationOverrides);
        foreach (var parameter in parameters)
        {
            ParameterFields.Add(new InteractionFieldViewModel(
                new OperatorInteractionField
                {
                    Id = parameter.MemberKey,
                    Label = string.IsNullOrWhiteSpace(parameter.Group)
                        ? parameter.DisplayName
                        : $"{parameter.Group}: {parameter.DisplayName}",
                    Kind = parameter.Kind,
                    DefaultValue = parameter.Value,
                },
                isReadOnly: parameter.IsReadOnly || IsRunning));
        }

        HasParameterFields = ParameterFields.Count > 0;
    }

    private void ApplySavedParameterOverrides()
    {
        if (SelectedProgram is null)
        {
            return;
        }

        var planId = SelectedProgram.Id;
        foreach (var ov in _settings.PlanParameterOverrides.Where(o =>
                     string.Equals(o.PlanId, planId, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(o.MemberKey)))
        {
            _openTap.TrySetParameter(ov.MemberKey, ov.Value ?? string.Empty);
        }
    }

    private async Task ApplyParametersAsync()
    {
        if (!IsEngineerDebugMode)
        {
            Status = "Parameter overrides require Engineer/Debug mode.";
            return;
        }

        if (SelectedProgram is null)
        {
            Status = "Select a program.";
            return;
        }

        if (ParameterFields.Count == 0)
        {
            Status = "Select a step with editable parameters.";
            return;
        }

        var planId = SelectedProgram.Id;
        var applied = 0;
        foreach (var field in ParameterFields.Where(f => !f.IsReadOnly))
        {
            var value = field.ToResponseValue();
            if (!_openTap.TrySetParameter(field.Id, value))
            {
                Status = $"Could not set {field.Label}.";
                return;
            }

            UpsertParameterOverride(planId, field.Id, value);
            applied++;
        }

        if (_settingsStore is not null)
        {
            await _settingsStore.SaveAppSettingsAsync().ConfigureAwait(false);
        }

        if (SelectedStep is not null
            && _openTap.TryGetStepConditionSummary(SelectedStep.Path, out var summary)
            && !string.IsNullOrWhiteSpace(summary))
        {
            ConditionSummary = summary!;
        }

        Status = applied == 0
            ? "No writable parameters to apply."
            : $"Applied {applied} parameter(s) for {SelectedProgram.DisplayName} (station override; TapPlan unchanged).";
    }

    private void UpsertParameterOverride(string planId, string memberKey, string value)
    {
        var existing = _settings.PlanParameterOverrides.FirstOrDefault(o =>
            string.Equals(o.PlanId, planId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(o.MemberKey, memberKey, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _settings.PlanParameterOverrides.Add(new PlanParameterOverride
            {
                PlanId = planId,
                MemberKey = memberKey,
                Value = value,
            });
            return;
        }

        existing.Value = value;
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

        var selectionPath = selectionOnly ? SelectedStep?.Path : null;
        var selectionName = selectionOnly ? SelectedStep?.Name : null;
        var stagePath = SelectedStage?.Path;
        if (selectionOnly)
        {
            if (string.IsNullOrWhiteSpace(selectionPath))
            {
                Status = "Select a stage or step to run.";
                return;
            }

            if (IsWholePlanSelection(selectionPath))
            {
                Status = "Run Selected needs a specific stage or step — not the entire program. Use Run for the full suite.";
                return;
            }
        }

        IsRunning = true;
        HasPlotData = false;
        ShowPlotForSelection = false;
        HistoryBanner = string.Empty;
        _stepsWithSamples.Clear();
        _gaugeTiles.Clear();
        PresentationTiles.Clear();
        HasPresentationTiles = false;
        PlotLegendText = "Channel";
        PlotYLabel = "Value";
        PlotTitle = "Live measurements";
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
            _pendingOperatorPrompt = null;
            _pendingInteractionRequest = null;
        }

        DetailLines.Clear();
        ClearInteractionUi();
        OverallPercent = 0;
        IterationText = string.Empty;
        var cts = new CancellationTokenSource();
        _runControl.AttachRun(cts);

        try
        {
            await LoadSelectedProgramAsync(stagePath, selectionPath);
            ApplySavedParameterOverrides();
            if (IsEngineerDebugMode && !string.IsNullOrWhiteSpace(selectionPath))
            {
                SelectedStep = FlattenHierarchy(_fullHierarchy)
                    .FirstOrDefault(s => string.Equals(s.Path, selectionPath, StringComparison.OrdinalIgnoreCase))
                    ?? SelectedStep;
                if (SelectedStep is not null)
                {
                    ApplyDebugPatch();
                }
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
                ? await _openTap.RunSelectionAsync(selectionPath!, _progress, cts.Token).ConfigureAwait(false)
                : await _openTap.RunAsync(_progress, cts.Token).ConfigureAwait(false);

            lock (_progressSync)
            {
                _pendingForceFlush = true;
            }

            ScheduleUiFlush();
            await WaitForPendingFlushesAsync().ConfigureAwait(false);

            LastRunId = summary.RunId;
            RecordAttempts(summary);

            await RunOnUiAsync(() =>
            {
                SyncHierarchyLive();
                ApplyResultsFromSummary(summary);
                ApplyAttemptTexts();
                if (!selectionOnly && summary.Result == RunResult.Failed)
                {
                    StepStatusFilter = StepFilter.Fail;
                }

                if (!string.IsNullOrWhiteSpace(selectionPath))
                {
                    ResolveSelectedStep(selectionPath);
                }

                OpenSelectedDetail(revealDetail: false);
                Status = BuildCompletionStatus(selectionOnly, selectionPath, selectionName, summary);
                AttemptSummaryChip = string.Empty;
                if (selectionOnly
                    && !string.IsNullOrWhiteSpace(selectionPath)
                    && _attemptLedger.TryGetValue(selectionPath, out var led))
                {
                    AttemptSummaryChip = led.Display;
                }
            }).ConfigureAwait(false);

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
                Steps = BuildRolledUpSteps(),
                StepAttempts = _attemptLedger.Values.OrderBy(a => a.StepPath).ToList(),
            };
            await _runStore.SaveAsync(record).ConfigureAwait(false);

            DutHistoryReport? historyReport = null;
            if (_dutHistory is not null && summary.Result is RunResult.Passed or RunResult.Failed)
            {
                try
                {
                    historyReport = await _dutHistory.AnalyzeAsync(record).ConfigureAwait(false);
                    await RunOnUiAsync(() =>
                    {
                        if (_settings.ShowDutHistoryOnRun)
                        {
                            HistoryBanner = historyReport.OperatorSummary;
                            if (!string.IsNullOrWhiteSpace(historyReport.OperatorSummary))
                            {
                                Status += " " + historyReport.OperatorSummary;
                            }
                        }
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (_settings.ShowDutHistoryOnRun)
                    {
                        await RunOnUiAsync(() => HistoryBanner = $"DUT history unavailable: {ex.Message}")
                            .ConfigureAwait(false);
                    }
                }
            }

            if (summary.Result is RunResult.Passed or RunResult.Failed)
            {
                try
                {
                    var kinds = SelectedProgram?.ReportKinds is { Count: > 0 }
                        ? SelectedProgram.ReportKinds
                        : ProgramCatalog.ResolveReportKinds(SelectedProgram?.Id);
                    await _reportService.GenerateReportsAsync(record, kinds, historyReport).ConfigureAwait(false);
                    await RunOnUiAsync(() => Status += " Report(s) generated.").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await RunOnUiAsync(() => Status += $" Report failed: {ex.Message}").ConfigureAwait(false);
                }
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
            OperatorPromptMessage = null;
            ClearInteractionUi();
            OverallPercent = 100;
            cts.Dispose();
            RefreshSessionSummary();
            RefreshHero();
        }
    }

    private bool IsWholePlanSelection(string path)
        => _fullHierarchy.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

    private string BuildCompletionStatus(
        bool selectionOnly,
        string? selectionPath,
        string? selectionName,
        OpenTapRunSummary summary)
    {
        if (selectionOnly && !string.IsNullOrWhiteSpace(selectionPath))
        {
            _attemptLedger.TryGetValue(selectionPath, out var ledger);
            var attemptNo = ledger?.AttemptCount ?? 1;
            var badge = ledger?.Display ?? $"{attemptNo}";
            return $"Attempt #{attemptNo} for {selectionName ?? selectionPath} ({badge}). Snapshot also saved to Results.";
        }

        return $"Suite finished: {summary.Result}. Session attempts rolled up; Results entry {summary.RunId}.";
    }

    private StationProfile BuildStationProfile()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var planId = SelectedProgram?.Id ?? string.Empty;
        foreach (var ov in _settings.PlanSlotOverrides.Where(o =>
                     string.Equals(o.PlanId, planId, StringComparison.OrdinalIgnoreCase)
                     || string.IsNullOrWhiteSpace(planId)))
        {
            if (string.IsNullOrWhiteSpace(ov.Resource))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ov.RoleHint))
            {
                map[ov.RoleHint] = ov.Resource;
            }

            if (!string.IsNullOrWhiteSpace(ov.SlotName))
            {
                map[ov.SlotName] = ov.Resource;
            }
        }

        // Legacy fallback: station bindings → instrument registry resources.
        if (map.Count == 0)
        {
            foreach (var b in _settings.StationBindings)
            {
                var instr = _settings.Instruments.FirstOrDefault(i =>
                    string.Equals(i.Id, b.InstrumentId, StringComparison.OrdinalIgnoreCase));
                if (instr is not null && !string.IsNullOrWhiteSpace(b.Role))
                {
                    map[b.Role] = instr.Resource;
                }
            }
        }

        return new StationProfile(map);
    }

    public void OpenSelectedStepDetail() => OpenSelectedDetail(revealDetail: true);

    private void OpenSelectedDetail(bool revealDetail = false)
    {
        if (SelectedStep is null)
        {
            return;
        }

        DetailStep = SelectedStep;
        if (revealDetail)
        {
            ShowDetailRegion = true;
        }

        ShowDetails = true;
        DetailChipText = StatusChip.FromStatus(SelectedStep.StatusText, SelectedStep.Verdict);
        DetailPrimaryLine = !string.IsNullOrWhiteSpace(SelectedStep.KeyValue)
            ? SelectedStep.KeyValue!
            : SelectedStep.StatusText;
        DetailKeyValues.Clear();
        if (!string.IsNullOrWhiteSpace(SelectedStep.KeyValue))
        {
            DetailKeyValues.Add(SelectedStep.KeyValue);
        }

        DetailKeyValues.Add($"Status: {SelectedStep.StatusText}");
        DetailKeyValues.Add($"Verdict: {SelectedStep.Verdict}");
        DetailKeyValues.Add($"Path: {SelectedStep.Path}");
        foreach (var field in ParameterFields.Where(f =>
                     f.Label.Contains("Presentation", StringComparison.OrdinalIgnoreCase)))
        {
            DetailKeyValues.Add(field.Label + ": " + field.Value);
        }

        if (!string.IsNullOrWhiteSpace(SelectedStep.AttemptsText))
        {
            DetailKeyValues.Add($"Attempts: {SelectedStep.AttemptsText}");
        }

        AttemptHistoryLines.Clear();
        if (_attemptLedger.TryGetValue(SelectedStep.Path, out var ledger))
        {
            AttemptSummaryChip = ledger.Display;
            foreach (var attempt in ledger.Attempts)
            {
                AttemptHistoryLines.Add(
                    $"#{attempt.AttemptNumber} {(attempt.Passed ? "PASS" : "FAIL")} — {attempt.Message} @ {attempt.CompletedAt:u}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(SelectedStep.AttemptsText))
        {
            AttemptSummaryChip = SelectedStep.AttemptsText;
        }
        else
        {
            AttemptSummaryChip = string.Empty;
        }

        ConditionSummary = string.Empty;
        if (IsEngineerDebugMode
            && _openTap.TryGetStepConditionSummary(SelectedStep.Path, out var summary)
            && !string.IsNullOrWhiteSpace(summary))
        {
            ConditionSummary = summary!;
        }

        RefreshPlotVisibility();
        RefreshPresentationTiles();
        RefreshHero();
    }

    private void RefreshPlotVisibility()
    {
        ShowPlotForSelection = SelectedStep is not null && StepHasSampleContext(SelectedStep);
    }

    private void RefreshPresentationTiles()
    {
        PresentationTiles.Clear();
        var path = SelectedStep?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            HasPresentationTiles = false;
            return;
        }

        foreach (var tile in _gaugeTiles
                     .Where(t => string.Equals(t.StepPath, path, StringComparison.OrdinalIgnoreCase))
                     .Take(PresentationRoleMap.MaxRunGaugeTiles))
        {
            PresentationTiles.Add(tile);
        }

        HasPresentationTiles = PresentationTiles.Count > 0;
    }

    private bool StepHasSampleContext(HierarchyStepViewModel step)
        => _stepsWithSamples.Contains(step.Path);

    private void RecordAttempts(OpenTapRunSummary summary)
    {
        foreach (var step in summary.Steps)
        {
            var path = string.IsNullOrWhiteSpace(step.StepPath) ? step.StepType : step.StepPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (!_attemptLedger.TryGetValue(path, out var ledger))
            {
                ledger = new StepAttemptSummary
                {
                    StepPath = path,
                    StepName = step.StepType,
                };
                _attemptLedger[path] = ledger;
            }

            var attempt = new StepResultRecord
            {
                StepId = step.StepId,
                StepType = step.StepType,
                StepPath = path,
                AttemptNumber = ledger.AttemptCount + 1,
                Passed = step.Passed,
                Message = step.Message,
                StartedAt = step.StartedAt,
                CompletedAt = step.CompletedAt,
            };
            ledger.Attempts.Add(attempt);
            ledger.AttemptCount++;
            if (step.Passed)
            {
                ledger.PassedCount++;
            }
            else
            {
                ledger.FailedCount++;
            }

            ledger.LatestPassed = step.Passed;
            ledger.LatestMessage = step.Message;
            ledger.StepName = step.StepType;
        }
    }

    private List<StepResultRecord> BuildRolledUpSteps()
        => _attemptLedger.Values
            .Select(l => l.Attempts.LastOrDefault())
            .Where(a => a is not null)
            .Cast<StepResultRecord>()
            .OrderBy(a => a.StepPath)
            .ToList();

    private void ApplyAttemptTexts()
    {
        foreach (var root in _fullHierarchy)
        {
            ApplyAttemptTexts(root);
        }
    }

    private void ApplyAttemptTexts(HierarchyStepViewModel node)
    {
        if (_attemptLedger.TryGetValue(node.Path, out var ledger))
        {
            node.AttemptsText = ledger.Display;
        }

        foreach (var child in node.Children)
        {
            ApplyAttemptTexts(child);
        }
    }

    private void ClearAttemptTexts()
    {
        foreach (var root in _fullHierarchy)
        {
            ClearAttemptTexts(root);
        }

        AttemptHistoryLines.Clear();
    }

    private static void ClearAttemptTexts(HierarchyStepViewModel node)
    {
        node.AttemptsText = string.Empty;
        foreach (var child in node.Children)
        {
            ClearAttemptTexts(child);
        }
    }

    public void ContinueOperatorAttention() => ContinueOperator();

    private void ContinueOperator()
    {
        var request = _openTap.PendingInteraction;
        if (request is not null && InteractionFields.Count > 0)
        {
            foreach (var field in InteractionFields.Where(f => f.Required))
            {
                if (field.IsBoolean)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(field.Value))
                {
                    InteractionValidationError = $"{field.Label} is required.";
                    Status = InteractionValidationError;
                    return;
                }

                if (field.Kind == OperatorInteractionFieldKind.Number
                    && !double.TryParse(
                        field.Value.Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out _))
                {
                    InteractionValidationError = $"{field.Label} must be a number.";
                    Status = InteractionValidationError;
                    return;
                }
            }
        }

        InteractionValidationError = null;
        var values = InteractionFields.ToDictionary(f => f.Id, f => f.ToResponseValue(), StringComparer.OrdinalIgnoreCase);
        var response = request is null
            ? null
            : OperatorInteractionResponse.Continue(request.Id, values);

        lock (_progressSync)
        {
            _pendingAwaitingOperator = false;
            _pendingOperatorPrompt = null;
            _pendingInteractionRequest = null;
        }

        _openTap.Resume(response);
        _runControl.Resume();
        ClearInteractionUi();
        IsAwaitingOperator = false;
        OperatorPromptMessage = null;
        Status = "Continuing…";
        // Interaction card collapse changes hero height; re-anchor the step list after layout.
        ScheduleScrollToCurrentStep();
    }

    private void ScheduleScrollToCurrentStep()
    {
        void Scroll()
        {
            JumpToCurrent();
            if (SelectedStep is null && !string.IsNullOrWhiteSpace(CurrentStepPath))
            {
                return;
            }

            RequestScrollToSelectedStep?.Invoke(this, EventArgs.Empty);
        }

        if (UiScheduler is not null)
        {
            UiScheduler(Scroll);
            return;
        }

        Scroll();
    }

    private void ApplyInteractionUi(OperatorInteractionRequest? request, string? fallbackMessage)
    {
        InteractionValidationError = null;
        InteractionTitle = request?.Title ?? "Operator attention";
        OperatorPromptMessage = request?.Message ?? fallbackMessage;
        InteractionFields.Clear();
        if (request?.Fields is { Count: > 0 })
        {
            foreach (var field in request.Fields)
            {
                InteractionFields.Add(new InteractionFieldViewModel(field));
            }
        }

        HasInteractionFields = InteractionFields.Count > 0;
    }

    private void ClearInteractionUi()
    {
        InteractionFields.Clear();
        HasInteractionFields = false;
        InteractionTitle = "Operator attention";
        InteractionValidationError = null;
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

        RollupParentStatuses();
        RefreshHero();
        if (DetailStep is not null && SelectedStep is not null
            && ReferenceEquals(DetailStep, SelectedStep))
        {
            DetailChipText = StatusChip.FromStatus(SelectedStep.StatusText, SelectedStep.Verdict);
            DetailPrimaryLine = !string.IsNullOrWhiteSpace(SelectedStep.KeyValue)
                ? SelectedStep.KeyValue!
                : SelectedStep.StatusText;
        }
    }

    private void ApplyResultsFromSummary(OpenTapRunSummary summary)
    {
        var flat = FlattenHierarchy(_fullHierarchy).ToList();
        foreach (var step in summary.Steps)
        {
            var vm = FindStepVm(flat, step.StepId, step.StepPath, step.StepType);
            if (vm is null)
            {
                continue;
            }

            string status;
            if (step.Passed)
            {
                status = string.IsNullOrWhiteSpace(step.Message)
                    || string.Equals(step.Message, "NotSet", StringComparison.OrdinalIgnoreCase)
                    || StatusChip.FromStatus(step.Message) == "Pending"
                    ? "Pass"
                    : step.Message!;
                if (StatusChip.FromStatus(status) == "Pending")
                {
                    status = "Pass";
                }
            }
            else if (!string.IsNullOrWhiteSpace(step.Message))
            {
                status = step.Message;
            }
            else
            {
                status = "Fail";
            }

            vm.StatusText = status;
            vm.Verdict = status;
            vm.Node.StatusText = status;
            vm.Node.Verdict = status;
            vm.ChipText = StatusChip.FromStatus(status);
        }

        RollupParentStatuses();
        RefreshHero();
        RefreshSuiteSummary();
        MaybeAutoFocusFail();
    }

    private static HierarchyStepViewModel? FindStepVm(
        IReadOnlyList<HierarchyStepViewModel> flat,
        string? stepId,
        string? stepPath,
        string? stepName)
    {
        if (!string.IsNullOrWhiteSpace(stepPath))
        {
            var byPath = flat.FirstOrDefault(s =>
                string.Equals(s.Path, stepPath, StringComparison.OrdinalIgnoreCase));
            if (byPath is not null)
            {
                return byPath;
            }
        }

        if (!string.IsNullOrWhiteSpace(stepId))
        {
            var byId = flat.FirstOrDefault(s =>
                string.Equals(s.Id, stepId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(stepName))
        {
            return flat.FirstOrDefault(s =>
                string.Equals(s.Name, stepName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void ApplyPendingStepLive(
        string? stepId,
        string? stepPath,
        string? statusText,
        string? verdict,
        string? keyValue)
    {
        if (string.IsNullOrWhiteSpace(stepId)
            && string.IsNullOrWhiteSpace(stepPath)
            && string.IsNullOrWhiteSpace(statusText)
            && string.IsNullOrWhiteSpace(verdict)
            && keyValue is null)
        {
            return;
        }

        var vm = FindStepVm(FlattenHierarchy(_fullHierarchy).ToList(), stepId, stepPath, stepName: null);
        if (vm is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            vm.StatusText = statusText;
            vm.Node.StatusText = statusText;
        }

        if (!string.IsNullOrWhiteSpace(verdict))
        {
            vm.Verdict = verdict;
            vm.Node.Verdict = verdict;
        }

        if (keyValue is not null)
        {
            vm.KeyValue = keyValue;
            vm.Extra = keyValue;
            vm.Node.KeyValue = keyValue;
        }

        vm.ChipText = StatusChip.FromStatus(vm.StatusText, vm.Verdict);
        if (!string.IsNullOrWhiteSpace(stepPath))
        {
            CurrentStepPath = stepPath;
        }

        if (!string.IsNullOrWhiteSpace(vm.Name))
        {
            CurrentStepName = vm.Name;
        }

        RollupParentStatuses();
        RefreshHero();
        if (string.Equals(vm.ChipText, "Fail", StringComparison.Ordinal))
        {
            MaybeAutoFocusFail();
        }
    }

    private async Task RunOnUiAsync(Action action)
    {
        if (UiScheduler is not null)
        {
            UiScheduler(action);
            return;
        }

        try
        {
            var dispatcher = Dispatcher.UIThread;
            if (dispatcher.CheckAccess() || Avalonia.Application.Current is null)
            {
                action();
                return;
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcher.Post(
                () =>
                {
                    try
                    {
                        action();
                        tcs.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                },
                DispatcherPriority.Normal);
            await tcs.Task.ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            action();
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

        await RunOnUiAsync(DrainUiFlush).ConfigureAwait(false);
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
                string? sampleStepPath;
                bool force;
                bool awaiting;
                string? prompt;
                OperatorInteractionRequest? interactionRequest;
                List<string>? details;
                string? stepId;
                string? stepPath;
                string? stepName;
                string? statusText;
                string? verdict;
                string? keyValue;
                string? iterationText;
                lock (_progressSync)
                {
                    status = _pendingStatus;
                    percent = _pendingPercent;
                    sample = _pendingSample;
                    sampleStepPath = _pendingSampleStepPath;
                    force = _pendingForceFlush;
                    awaiting = _pendingAwaitingOperator;
                    prompt = _pendingOperatorPrompt;
                    interactionRequest = _pendingInteractionRequest;
                    details = DequeueDetailBatch_NoLock(MaxDetailLinesPerFlush);
                    stepId = _pendingStepId;
                    stepPath = _pendingStepPath;
                    stepName = _pendingStepName;
                    statusText = _pendingStatusText;
                    verdict = _pendingVerdict;
                    keyValue = _pendingKeyValue;
                    iterationText = _pendingIterationText;
                    _pendingForceFlush = false;
                    _pendingAwaitingOperator = false;
                    _pendingOperatorPrompt = null;
                    _pendingInteractionRequest = null;
                    _pendingStepId = null;
                    _pendingStepPath = null;
                    _pendingStepName = null;
                    _pendingStatusText = null;
                    _pendingVerdict = null;
                    _pendingKeyValue = null;
                    _pendingIterationText = null;
                    _pendingSampleStepPath = null;
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
                            _pendingInteractionRequest = interactionRequest;
                        }

                        _pendingStepId ??= stepId;
                        _pendingStepPath ??= stepPath;
                        _pendingStepName ??= stepName;
                        _pendingStatusText ??= statusText;
                        _pendingVerdict ??= verdict;
                        if (keyValue is not null)
                        {
                            _pendingKeyValue ??= keyValue;
                        }

                        if (iterationText is not null)
                        {
                            _pendingIterationText ??= iterationText;
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
                    ApplyInteractionUi(interactionRequest ?? _openTap.PendingInteraction, prompt);
                    // Growing operator card shrinks the step list; keep the awaiting step visible.
                    ScheduleScrollToCurrentStep();
                }

                if (details is not null)
                {
                    AppendDetailLines(details, MaxDetailLinesPerFlush);
                }

                SyncHierarchyLive();
                if (!string.IsNullOrWhiteSpace(stepName))
                {
                    CurrentStepName = stepName;
                }

                if (!string.IsNullOrWhiteSpace(stepPath))
                {
                    CurrentStepPath = stepPath;
                }

                ApplyPendingStepLive(stepId, stepPath, statusText, verdict, keyValue);
                if (iterationText is not null)
                {
                    IterationText = iterationText;
                }

                RefreshHero();

                if (sample is not null)
                {
                    if (PresentationRoleMap.IsTimeseriesPlotSample(sample))
                    {
                        HasPlotData = true;
                        if (!string.IsNullOrWhiteSpace(sampleStepPath))
                        {
                            _stepsWithSamples.Add(sampleStepPath);
                        }

                        var key = sample.EffectiveMetricKey;
                        PlotLegendText = key;
                        PlotTitle = key;
                        PlotYLabel = string.IsNullOrWhiteSpace(sample.Unit) ? "Value" : sample.Unit!;
                        AppendSampleToPlot(sample.Value);
                        PlotYs = _plotPublish;
                        PlotDataChanged?.Invoke(this, EventArgs.Empty);
                        PlotUiFlushCount++;
                        RefreshPlotVisibility();
                    }

                    if (PresentationRoleMap.IsRunGaugeSample(sample))
                    {
                        PresentationRoleMap.UpsertRunGauge(_gaugeTiles, sample, sampleStepPath ?? stepPath);
                        RefreshPresentationTiles();
                    }
                }
                else if (force)
                {
                    PlotUiFlushCount++;
                }

                lock (_progressSync)
                {
                    // Leftover detail lines are flushed on a later UI frame (see finally).
                    if (_pendingForceFlush || _pendingSample is not null || _pendingAwaitingOperator)
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

    private List<string>? DequeueDetailBatch_NoLock(int maxCount)
    {
        if (_pendingDetails.Count == 0 || maxCount <= 0)
        {
            return null;
        }

        var batch = new List<string>(Math.Min(maxCount, _pendingDetails.Count));
        while (batch.Count < maxCount && _pendingDetails.Count > 0)
        {
            batch.Add(_pendingDetails.Dequeue());
        }

        return batch;
    }

    /// Appends up to maxToAdd detail lines, dropping oldest when over DetailCap. Returns how many were added.
    public int AppendDetailLines(IReadOnlyList<string> lines, int maxToAdd)
    {
        var added = 0;
        foreach (var line in lines)
        {
            if (added >= maxToAdd)
            {
                break;
            }

            if (DetailLines.Count >= DetailCap)
            {
                DetailLines.RemoveAt(0);
            }

            DetailLines.Add(line);
            added++;
        }

        return added;
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
        SessionBlocked = !_session.CanRun;
        ShowSessionForm = NeedsDutConfirm || IsStalePrompt || SessionBlocked;
        var program = _session.ProgramDisplayName ?? "(none)";
        if (_session.CanRun)
        {
            SessionSummary = $"DUT {_session.DutSerial} | Tech {_session.OperatorName ?? "—"} | {program}";
            return;
        }

        if (_session.State == OperatorSessionState.Stale)
        {
            SessionSummary = $"DUT {_session.DutSerial} (re-confirm) | {program}";
            return;
        }

        SessionSummary = $"Session blocked — confirm DUT + technician | {program}";
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
