using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Run board coordinator: owns the child panels, the shared run status, the hero line and the UI flush pump.
public partial class RunTestViewModel : ReactiveObject, IRunBoardHost
{
    private readonly IOpenTapSession _openTap;
    private readonly OperatorSession _session;
    private readonly IRunControl _runControl;
    private readonly AppSettings _settings;
    private readonly ThrottledOpenTapProgress _progress;
    private readonly IStorageHealthService? _storageHealth;

    public RunTestViewModel(
        IOpenTapSession openTap,
        OperatorSession session,
        IRunControl runControl,
        IReportService reportService,
        IRunStore runStore,
        AppSettings settings,
        ISettingsStore? settingsStore = null,
        IDutHistoryService? dutHistory = null,
        BuildInfo? buildInfo = null,
        IStorageHealthService? storageHealth = null)
    {
        _openTap = openTap;
        _session = session;
        _runControl = runControl;
        _settings = settings;
        _storageHealth = storageHealth;
        _progress = new ThrottledOpenTapProgress(IngestProgress);
        Status = "Confirm DUT, then Run.";
        IsEngineerDebugMode = settings.IsEngineerDebugMode;
        if (settingsStore is not null)
        {
            settingsStore.AppSettingsSaved += (_, _) =>
            {
                IsEngineerDebugMode = settingsStore.AppSettings.IsEngineerDebugMode;
            };
        }

        // Panels reference each other, so the wiring uses lambdas; every capture resolves only
        // after this constructor has assigned all of them.
        StepDetail = new StepDetailViewModel(() => OpenSelectedDetail(revealDetail: true));
        Interaction = new InteractionHostViewModel();
        Live = new LivePresentationViewModel();
        ProgramSelection = new ProgramSelectionViewModel(
            status => Status = status,
            () => IsEngineerDebugMode,
            () => LoadSelectedProgramAsync(),
            () => SessionPanel!.RefreshSessionSummary());
        SessionPanel = new OperatorSessionPanelViewModel(
            session,
            settings,
            status => Status = status,
            () => ProgramSelection.SelectedProgram,
            ClearSessionAttempts);
        StationOverrides = new StationOverridesViewModel(
            openTap,
            settings,
            settingsStore,
            status => Status = status,
            () => IsEngineerDebugMode,
            () => IsRunning,
            () => StepTree!.SelectedStep,
            () => ProgramSelection.SelectedProgram,
            summary => StepDetail.ConditionSummary = summary);
        StepTree = new StepTreeViewModel(
            () => _openTap.StepTree,
            path => Run!.FindAttempt(path),
            () => OpenSelectedDetail(revealDetail: true),
            RefreshHero,
            () => CurrentStepPath);
        Run = new RunExecutionViewModel(
            this,
            openTap,
            session,
            runControl,
            reportService,
            runStore,
            settings,
            dutHistory,
            buildInfo ?? BuildInfo.FromAssembly(typeof(RunTestViewModel).Assembly),
            _progress,
            ProgramSelection,
            SessionPanel,
            StationOverrides,
            StepTree,
            StepDetail,
            Interaction,
            Live,
            storageHealth);

        ContinueOperatorCommand = ReactiveCommand.Create(ContinueOperator);
        OpenLastRunResultsCommand = ReactiveCommand.Create(
            () => NavigateToResultsRequested?.Invoke(this, EventArgs.Empty));
        InspectPlanCommand = ReactiveCommand.Create(
            () => NavigateToInspectRequested?.Invoke(this, EventArgs.Empty));
        DismissStorageBannerCommand = ReactiveCommand.Create(() =>
        {
            StorageBannerDismissed = true;
            HasStorageBanner = false;
        });
        DismissBannerCommand = ReactiveCommand.Create(() =>
        {
            HasBanner = false;
            BannerMessage = string.Empty;
        });

        SubscribeToChildren();
        Observe(ProgramSelection.RefreshProgramsAsync());
        RefreshStorageHealth();
    }

    public StepDetailViewModel StepDetail { get; }
    public InteractionHostViewModel Interaction { get; }
    public LivePresentationViewModel Live { get; }
    public ProgramSelectionViewModel ProgramSelection { get; }
    public OperatorSessionPanelViewModel SessionPanel { get; }
    public StationOverridesViewModel StationOverrides { get; }
    public StepTreeViewModel StepTree { get; }
    public RunExecutionViewModel Run { get; }

    public OperatorSession Session => _session;

    /// True when neither a run is in progress nor the session is blocking the start.
    public bool CanStartRun => !IsRunning && !SessionPanel.SessionBlocked;

    public event EventHandler? NavigateToResultsRequested;
    public event EventHandler? NavigateToInspectRequested;

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ContinueOperatorCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenLastRunResultsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> InspectPlanCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> DismissStorageBannerCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> DismissBannerCommand { get; }

    [Reactive] private string _status = string.Empty;
    [Reactive] private bool _isRunning;
    [Reactive] private double _overallPercent;
    [Reactive] private string? _lastRunId;
    [Reactive] private string _historyBanner = string.Empty;
    [Reactive] private bool _isEngineerDebugMode;
    [Reactive] private string _currentStepName = string.Empty;
    [Reactive] private string _currentStepPath = string.Empty;
    [Reactive] private string _heroLabel = "SELECTED:";
    [Reactive] private string _heroStepName = string.Empty;
    [Reactive] private string _heroChipText = "Pending";
    [Reactive] private string _heroStatusLine = string.Empty;
    [Reactive] private string _iterationText = string.Empty;
    [Reactive] private bool _hasStorageBanner;
    [Reactive] private bool _storageBannerIsCritical;
    [Reactive] private string _storageBannerMessage = string.Empty;
    [Reactive] private bool _storageBannerDismissed;
    [Reactive] private bool _hasBanner;
    [Reactive] private RunBannerSeverity _bannerSeverity;
    [Reactive] private string _bannerMessage = string.Empty;

    /// Refresh free-space banner (call after Settings changes or before Run).
    public void RefreshStorageHealth()
    {
        if (_storageHealth is null)
        {
            HasStorageBanner = false;
            StorageBannerIsCritical = false;
            StorageBannerMessage = string.Empty;
            return;
        }

        var snap = _storageHealth.GetDataVolumeHealth();
        StorageBannerIsCritical = snap.Level == StorageHealthLevel.Critical;
        if (snap.Level == StorageHealthLevel.Ok)
        {
            HasStorageBanner = false;
            StorageBannerMessage = string.Empty;
            StorageBannerDismissed = false;
            return;
        }

        StorageBannerMessage = snap.Message;
        HasStorageBanner = StorageBannerIsCritical || !StorageBannerDismissed;
    }

    /// Reveals the selected step in the bottom tray (double-tap / keyboard entry point from the view).
    public void OpenSelectedStepDetail() => OpenSelectedDetail(revealDetail: true);

    /// Selects a step the Inspect page asked to open on the Run board.
    public void ApplySelectionFromInspect(string? stepPath) => StepTree.ApplySelectionFromInspect(stepPath);

    /// Continue entry point for the window-level transport bar.
    public void ContinueOperatorAttention() => ContinueOperator();

    private void SubscribeToChildren()
    {
        ProgramSelection.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(ProgramSelectionViewModel.SelectedProgram)
                || ProgramSelection.SelectedProgram is null)
            {
                return;
            }

            Observe(LoadSelectedProgramAsync());
            SessionPanel.RefreshRequirementFlags();
        };

        SessionPanel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(OperatorSessionPanelViewModel.SessionBlocked))
            {
                this.RaisePropertyChanged(nameof(CanStartRun));
            }
        };

        StepTree.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(StepTreeViewModel.SelectedStep)
                || StepTree.SelectedStep is not { } step)
            {
                return;
            }

            StationOverrides.DebugStepEnabled = step.Enabled;
            OpenSelectedDetail(revealDetail: false);
            StationOverrides.RefreshParameterFields();
            Live.RefreshPlotVisibility(step);
            Live.RefreshPresentationTiles(step.Path);
            RefreshHero();
        };

        Interaction.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InteractionHostViewModel.IsAwaitingOperator))
            {
                RefreshHero();
            }
        };

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IsEngineerDebugMode))
            {
                StationOverrides.RefreshParameterFields();
            }
            else if (args.PropertyName is nameof(IsRunning) or nameof(Status))
            {
                RefreshHero();
                if (args.PropertyName == nameof(IsRunning))
                {
                    this.RaisePropertyChanged(nameof(CanStartRun));
                }
            }
        };
    }

    private void ClearSessionAttempts()
    {
        Run.ClearAttempts();
        StepTree.ClearAttemptTexts();
        StepDetail.AttemptHistoryLines.Clear();
    }

    private async Task LoadSelectedProgramAsync(string? preserveStagePath = null, string? preserveStepPath = null)
    {
        if (ProgramSelection.SelectedProgram is not { } program)
        {
            return;
        }

        SessionPanel.ApplyIdleStaleCheck();
        _session.SelectProgram(program.Id, program.Path, program.DisplayName, program.DutFamily);

        var alreadyLoaded = string.Equals(_openTap.LoadedPlanPath, program.Path, StringComparison.OrdinalIgnoreCase);
        if (!alreadyLoaded)
        {
            await LoadProgramEntryAsync(program).ConfigureAwait(false);
            StepTree.RebuildFromHost(preserveStagePath, preserveStepPath);
        }
        else if (StepTree.Hierarchy.Count == 0 && StepTree.FullHierarchy.Count == 0)
        {
            StepTree.RebuildFromHost(preserveStagePath, preserveStepPath);
        }
        else if (!string.IsNullOrWhiteSpace(preserveStepPath))
        {
            // Keep Hierarchy instances; only re-resolve selection by path.
            StepTree.ResolveSelectedStep(preserveStepPath);
        }
        else if (!string.IsNullOrWhiteSpace(preserveStagePath))
        {
            StepTree.RestoreSelection(preserveStagePath, preserveStepPath);
        }

        StationOverrides.RefreshStationSlotSummary();
        StationOverrides.ApplySavedParameterOverrides();
        StationOverrides.RefreshParameterFields();
        SessionPanel.RefreshSessionSummary();
    }

    private Task LoadProgramEntryAsync(ProgramItemViewModel program)
        => program.LoadKind switch
        {
            ProgramLoadKind.FactorySample => _openTap.LoadSampleProgramAsync(),
            ProgramLoadKind.FactoryBoardDemo => _openTap.LoadBoardDemoProgramAsync(),
            ProgramLoadKind.FactorySweepDemo => _openTap.LoadSweepDemoProgramAsync(),
            _ => _openTap.LoadPlanAsync(program.Path),
        };

    private void OpenSelectedDetail(bool revealDetail = false)
    {
        if (StepTree.SelectedStep is not { } step)
        {
            return;
        }

        StepDetail.Show(
            step,
            StationOverrides.ParameterFields,
            Run.FindAttempt(step.Path),
            ResolveConditionSummary(step),
            revealDetail);
        Live.RefreshPlotVisibility(step);
        Live.RefreshPresentationTiles(step.Path);
        RefreshHero();
    }

    private string? ResolveConditionSummary(HierarchyStepViewModel step)
        => IsEngineerDebugMode
           && _openTap.TryGetStepConditionSummary(step.Path, out var summary)
           && !string.IsNullOrWhiteSpace(summary)
            ? summary
            : null;

    private void RefreshHero()
    {
        HeroLabel = IsRunning ? "CURRENT:" : "SELECTED:";
        if (IsRunning && !string.IsNullOrWhiteSpace(CurrentStepName))
        {
            HeroStepName = CurrentStepName;
            var live = StepHierarchy.Find(
                StepHierarchy.Flatten(StepTree.FullHierarchy).ToList(),
                stepId: null,
                CurrentStepPath,
                CurrentStepName);
            var fallbackChip = Interaction.IsAwaitingOperator ? "Awaiting" : "Running";
            HeroChipText = live is null
                ? fallbackChip
                : StatusChip.FromStatus(live.StatusText, live.Verdict);
        }
        else if (StepTree.SelectedStep is { } selected)
        {
            HeroStepName = selected.Name;
            HeroChipText = StatusChip.FromStatus(selected.StatusText, selected.Verdict);
        }
        else
        {
            HeroStepName = string.Empty;
            HeroChipText = "Pending";
        }

        if (Interaction.IsAwaitingOperator)
        {
            // Prompt lives in the operator card; keep hero free of duplicate prose.
            HeroStatusLine = string.Empty;
            if (HeroChipText is not "Fail")
            {
                HeroChipText = "Awaiting";
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(IterationText))
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

    private void SyncHierarchyLive()
    {
        StepTree.SyncFromNodes();
        RefreshHero();
        if (StepTree.SelectedStep is { } selected
            && StepDetail.DetailStep is { } shown
            && ReferenceEquals(shown, selected))
        {
            StepDetail.SyncLive(selected);
        }
    }

    private void ContinueOperator()
    {
        var request = _openTap.PendingInteraction;
        if (!Interaction.TryCollectResponse(request, out var values))
        {
            Status = Interaction.InteractionValidationError!;
            return;
        }

        var response = request is null
            ? null
            : OperatorInteractionResponse.Continue(request.Id, values);

        ClearPendingOperatorState();
        _session.TouchActivity();
        _openTap.Resume(response);
        _runControl.Resume();
        Interaction.Clear();
        Interaction.IsAwaitingOperator = false;
        Interaction.OperatorPromptMessage = null;
        Status = "Continuing…";
        // Interaction card collapse changes hero height; re-anchor the step list after layout.
        ScheduleScrollToCurrentStep();
    }

    private void ScheduleScrollToCurrentStep()
    {
        void Scroll()
        {
            StepTree.JumpToCurrent();
            if (StepTree.SelectedStep is null && !string.IsNullOrWhiteSpace(CurrentStepPath))
            {
                return;
            }

            StepTree.RaiseRequestScroll();
        }

        if (UiScheduler is not null)
        {
            UiScheduler(Scroll);
            return;
        }

        Scroll();
    }

    internal void Observe(Task task)
        => task.ContinueWith(
            t =>
            {
                if (t.Exception?.GetBaseException() is { } ex)
                {
                    void SetError()
                    {
                        Status = $"Error: {ex.Message}";
                        SetBanner(RunBannerSeverity.Error, $"Error: {ex.Message}");
                    }

                    if (UiScheduler is not null)
                    {
                        UiScheduler(SetError);
                    }
                    else
                    {
                        PostToUi(SetError);
                    }
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    /// Sets a sticky in-panel error/warning banner; does not overwrite Status (kept for transient progress).
    public void SetBanner(RunBannerSeverity severity, string message)
    {
        BannerSeverity = severity;
        BannerMessage = message;
        HasBanner = true;
    }

    Task IRunBoardHost.RunOnUiAsync(Action action) => RunOnUiAsync(action);

    Task IRunBoardHost.WaitForPendingFlushesAsync() => WaitForPendingFlushesAsync();

    void IRunBoardHost.ForceUiFlush() => ForceUiFlush();

    void IRunBoardHost.ResetPumpForRun() => ResetPumpForRun();

    Task IRunBoardHost.LoadSelectedProgramAsync(string? preserveStagePath, string? preserveStepPath)
        => LoadSelectedProgramAsync(preserveStagePath, preserveStepPath);

    void IRunBoardHost.SyncHierarchyLive() => SyncHierarchyLive();

    void IRunBoardHost.OpenSelectedDetail(bool revealDetail) => OpenSelectedDetail(revealDetail);

    void IRunBoardHost.RefreshHero() => RefreshHero();

    private sealed class ThrottledOpenTapProgress(Action<OpenTapProgress> ingest) : IProgress<OpenTapProgress>
    {
        public void Report(OpenTapProgress value) => ingest(value);
    }
}
