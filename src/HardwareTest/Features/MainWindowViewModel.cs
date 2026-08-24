using System;
using System.Collections.ObjectModel;
using Avalonia.Automation;
using FluentAvalonia.UI.Controls;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Home;
using HardwareTest.Features.Inspect;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;
using HardwareTest.Features.Shell;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features;

public sealed class NavItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required object ViewModel { get; init; }
    public required FASymbol Symbol { get; init; }
}

public partial class MainWindowViewModel : ReactiveObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IRunControl _runControl;
    private readonly IOpenTapRunSession _openTap;
    private readonly ISafetyController? _safety;

    public MainWindowViewModel(
        ISettingsStore settingsStore,
        HomeViewModel home,
        RunTestViewModel runTest,
        InspectViewModel inspect,
        ResultsViewModel results,
        ReportPreviewViewModel reportPreview,
        InstrumentsViewModel instruments,
        SettingsViewModel settings,
        IRunControl runControl,
        IOpenTapRunSession openTap,
        ShellNotificationViewModel? shellNotification = null,
        ISafetyController? safety = null)
    {
        _settingsStore = settingsStore;
        _runControl = runControl;
        _openTap = openTap;
        _safety = safety;
        ShellNotification = shellNotification ?? new ShellNotificationViewModel();
        RunTest = runTest;
        Inspect = inspect;
        Results = results;
        ReportPreview = reportPreview;
        Instruments = instruments;
        _allPages =
        [
            new NavItem { Id = ShellNavigationPolicy.Home, Title = "Home", ViewModel = home, Symbol = FASymbol.Home },
            new NavItem { Id = ShellNavigationPolicy.RunTest, Title = "Run", ViewModel = runTest, Symbol = FASymbol.Play },
            new NavItem { Id = ShellNavigationPolicy.Inspect, Title = "Inspect", ViewModel = inspect, Symbol = FASymbol.Document },
            new NavItem { Id = ShellNavigationPolicy.Results, Title = "Results", ViewModel = results, Symbol = FASymbol.List },
            new NavItem { Id = ShellNavigationPolicy.ReportPreview, Title = "Report Preview", ViewModel = reportPreview, Symbol = FASymbol.Document },
            new NavItem { Id = ShellNavigationPolicy.Instruments, Title = "Instruments", ViewModel = instruments, Symbol = FASymbol.Repair },
            new NavItem { Id = ShellNavigationPolicy.Settings, Title = "Settings", ViewModel = settings, Symbol = FASymbol.Settings },
        ];
        NavigationItems = [];
        ApplyNavigationPolicy();
        NavigateTo(ResolveStartupPage());

        settingsStore.AppSettingsSaved += (_, _) => ApplyNavigationPolicy();

        PauseResumeCommand = ReactiveCommand.Create(PauseResume);
        PauseCommand = ReactiveCommand.Create(Pause);
        ResumeCommand = ReactiveCommand.Create(Resume);
        SafetyStopCommand = ReactiveCommand.Create(SafetyStop);

        runTest.NavigateToResultsRequested += (_, _) =>
        {
            NavigateToPageId(ShellNavigationPolicy.Results);
            _ = Results.OpenRunByIdAsync(runTest.LastRunId);
        };
        runTest.NavigateToInspectRequested += (_, _) => NavigateToPageId(ShellNavigationPolicy.Inspect);
        runTest.NavigateToInstrumentsRequested += OnStationBindRequested;
        inspect.OpenOnRunRequested += (_, stepPath) =>
        {
            runTest.ApplySelectionFromInspect(stepPath);
            NavigateToPageId(ShellNavigationPolicy.RunTest);
        };
        inspect.NavigateToRunRequested += (_, _) => NavigateToPageId(ShellNavigationPolicy.RunTest);
        home.NavigateToPageRequested += (_, pageId) => NavigateToPageId(pageId);
        results.NavigateToRunRequested += (_, _) => NavigateToPageId(ShellNavigationPolicy.RunTest);
        instruments.NavigateToRunRequested += (_, _) => NavigateToPageId(ShellNavigationPolicy.RunTest);
        reportPreview.NavigateToResultsRequested += (_, _) => NavigateToPageId(ShellNavigationPolicy.Results);
        results.ReportOpened += OnReportOpened;

        runControl.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IRunControl.IsPaused)
                or nameof(IRunControl.IsRunning)
                or nameof(IRunControl.IsSafetyStopping))
            {
                RaiseTransportProps();
            }
        };

        openTap.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IOpenTapRunSession.IsAwaitingOperator)
                or nameof(IOpenTapRunSession.OperatorPromptMessage)
                or nameof(IOpenTapRunSession.PendingInteraction))
            {
                RaiseTransportProps();
            }
        };
    }

    public RunTestViewModel RunTest { get; }
    public InspectViewModel Inspect { get; }
    public ResultsViewModel Results { get; }
    public ReportPreviewViewModel ReportPreview { get; }
    public InstrumentsViewModel Instruments { get; }
    public ShellNotificationViewModel ShellNotification { get; }
    public IRunControl RunControl => _runControl;

    public ObservableCollection<NavItem> NavigationItems { get; }

    private readonly NavItem[] _allPages;

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> PauseResumeCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> PauseCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ResumeCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SafetyStopCommand { get; }

    public bool IsPaused => _runControl.IsPaused;
    public bool IsRunning => _runControl.IsRunning;
    public bool IsSafetyStopping => _runControl.IsSafetyStopping;
    public bool IsAwaitingOperator => _openTap.IsAwaitingOperator;

    /// Pause glyph when not soft-paused and not awaiting (including idle affordance).
    public bool ShowPauseIcon => !IsPaused && !IsAwaitingOperator;
    /// Resume (play) when soft-paused and not awaiting operator input.
    public bool ShowResumeIcon => IsPaused && !IsAwaitingOperator;
    /// Continue when the host is waiting on an operator interaction.
    public bool ShowContinueIcon => IsAwaitingOperator;

    /// Single glyph for the Pause/Resume/Continue footer button (always visible).
    public FASymbol PauseResumeSymbol
    {
        get
        {
            if (IsAwaitingOperator)
            {
                return FASymbol.Accept;
            }

            return IsPaused ? FASymbol.PlayFilled : FASymbol.PauseFilled;
        }
    }

    public string PauseResumeLabel
    {
        get
        {
            if (IsAwaitingOperator)
            {
                return "Continue";
            }

            return IsPaused ? "Resume" : "Pause";
        }
    }

    public string PauseResumeTip
    {
        get
        {
            if (IsAwaitingOperator)
            {
                return "Continue the operator prompt (same as Run board Continue)";
            }

            return IsPaused ? "Resume the run" : "Pause the run";
        }
    }

    public string SafetyStopLabel
    {
        get
        {
            if (IsAwaitingOperator) return "Cancel prompt";
            if (IsSafetyStopping) return "Cancel shutdown";
            return StopRunCopy.Label;
        }
    }

    public string SafetyStopTip
    {
        get
        {
            if (IsAwaitingOperator)
                return StopRunCopy.CancelPromptTip;
            if (IsSafetyStopping)
                return StopRunCopy.CancelShutdownTip;
            return StopRunCopy.CooperativeTip;
        }
    }

    public string ControlStatus => FormatControlStatus(includePrompt: true);

    /// Compact pane is 48px — never the full operator prompt (that lives on the Run board).
    public string CompactControlStatus => FormatControlStatus(includePrompt: false);

    private string FormatControlStatus(bool includePrompt)
    {
        if (_runControl.IsSafetyStopping)
        {
            return "Stopping…";
        }

        if (IsAwaitingOperator)
        {
            if (includePrompt && !string.IsNullOrWhiteSpace(_openTap.OperatorPromptMessage))
            {
                return _openTap.OperatorPromptMessage!;
            }

            return "Awaiting operator";
        }

        if (_runControl.IsPaused)
        {
            return "Paused";
        }

        return _runControl.IsRunning ? "Running" : "Idle";
    }

    /// Polite for idle/running/paused; assertive for Stop in progress and operator prompts.
    public AutomationLiveSetting ControlStatusLiveSetting
        => IsSafetyStopping || IsAwaitingOperator
            ? AutomationLiveSetting.Assertive
            : AutomationLiveSetting.Polite;

    [Reactive]
    private NavItem? _selectedItem;

    [Reactive]
    private object? _currentPage;

    /// Whether the left nav pane is expanded (false = compact / icon-only footer).
    [Reactive]
    private bool _isNavPaneOpen = true;

    /// True while deferred startup (OpenTAP warm / retention) is still running after first paint.
    [Reactive]
    private bool _isStartingUp;

    [Reactive]
    private string _startupStatus = string.Empty;

    /// Marks the shell as starting so the overlay shows before heavy work.
    public void BeginStartup(string status)
    {
        IsStartingUp = true;
        StartupStatus = status;
    }

    /// Clears the startup overlay after deferred work finishes.
    public void CompleteStartup()
    {
        IsStartingUp = false;
        StartupStatus = string.Empty;
        if (SelectedItem?.Id == "Settings" && CurrentPage is SettingsViewModel settings)
        {
            settings.EnsurePackagesLoaded();
        }
    }

    private bool _syncingNavSelection;

    public void NavigateTo(NavItem? item)
    {
        if (item is null || _syncingNavSelection)
        {
            return;
        }

        CurrentPage = item.ViewModel;
        _syncingNavSelection = true;
        try
        {
            SelectedItem = NavigationItems.Contains(item)
                ? item
                : NavigationItems.FirstOrDefault(i =>
                      i.Id == ShellNavigationPolicy.ContextualParentId(item.Id))
                  ?? SelectedItem;
        }
        finally
        {
            _syncingNavSelection = false;
        }
        _settingsStore.UiState.SelectedPageId = item.Id;
        RunTest.SessionPanel.TouchActivity();
        if (item.Id == ShellNavigationPolicy.Results)
        {
            _ = Results.LoadRunsAsync();
        }
        else if (item.Id == ShellNavigationPolicy.Inspect)
        {
            Inspect.Refresh();
        }
        else if (item.Id == ShellNavigationPolicy.Settings && CurrentPage is SettingsViewModel settings)
        {
            settings.EnsurePackagesLoaded();
        }
    }

    public void NavigateToPageId(string pageId)
    {
        var item = _allPages.FirstOrDefault(i => i.Id == pageId);
        NavigateTo(item);
    }

    /// Rebuilds left nav for the saved engineer-mode presentation (not authentication).
    public void ApplyNavigationPolicy()
    {
        var engineer = _settingsStore.AppSettings.IsEngineerDebugMode;
        var visible = _allPages.Where(p => ShellNavigationPolicy.IsPersistentNav(p.Id, engineer)).ToArray();
        ShellNavigationPolicy.SyncCollection(NavigationItems, visible);

        var currentId = CurrentPage is null
            ? null
            : _allPages.FirstOrDefault(p => ReferenceEquals(p.ViewModel, CurrentPage))?.Id;
        if (currentId is null)
        {
            return;
        }

        if (ShellNavigationPolicy.CanRemainOnPage(currentId, engineer))
        {
            NavigateToPageId(currentId);
            return;
        }

        NavigateTo(NavigationItems[0]);
    }

    private NavItem ResolveStartupPage()
    {
        var id = _settingsStore.UiState.SelectedPageId;
        var page = _allPages.FirstOrDefault(i => i.Id == id);
        var engineer = _settingsStore.AppSettings.IsEngineerDebugMode;
        if (page is not null && ShellNavigationPolicy.CanRemainOnPage(page.Id, engineer))
        {
            return page;
        }

        return NavigationItems[0];
    }

    private void OnStationBindRequested(object? sender, StationBindRequestedEventArgs request)
    {
        var slot = request.SlotNames.Count > 0 ? request.SlotNames[0] : null;
        Instruments.FocusProgram(request.PlanId, slot);
        NavigateToPageId(ShellNavigationPolicy.Instruments);
    }

    private void OnReportOpened(object? sender, string path)
    {
        NavigateToPageId(ShellNavigationPolicy.ReportPreview);
        _ = ReportPreview.LoadFromPathAsync(path);
    }

    private void RaiseTransportProps()
    {
        this.RaisePropertyChanged(nameof(IsPaused));
        this.RaisePropertyChanged(nameof(IsRunning));
        this.RaisePropertyChanged(nameof(IsSafetyStopping));
        this.RaisePropertyChanged(nameof(IsAwaitingOperator));
        this.RaisePropertyChanged(nameof(ControlStatus));
        this.RaisePropertyChanged(nameof(CompactControlStatus));
        this.RaisePropertyChanged(nameof(ControlStatusLiveSetting));
        this.RaisePropertyChanged(nameof(PauseResumeLabel));
        this.RaisePropertyChanged(nameof(PauseResumeTip));
        this.RaisePropertyChanged(nameof(SafetyStopLabel));
        this.RaisePropertyChanged(nameof(SafetyStopTip));
        this.RaisePropertyChanged(nameof(ShowPauseIcon));
        this.RaisePropertyChanged(nameof(ShowResumeIcon));
        this.RaisePropertyChanged(nameof(ShowContinueIcon));
        this.RaisePropertyChanged(nameof(PauseResumeSymbol));
    }

    private void PauseResume()
    {
        if (IsAwaitingOperator)
        {
            ContinueOperator();
            return;
        }

        if (_runControl.IsPaused)
        {
            Resume();
            return;
        }

        if (_runControl.IsRunning)
        {
            Pause();
        }
    }

    private void ContinueOperator()
    {
        if (SelectedItem?.Id != ShellNavigationPolicy.RunTest)
        {
            NavigateToPageId(ShellNavigationPolicy.RunTest);
        }

        RunTest.ContinueOperatorAttention();
    }

    private void Pause()
    {
        _runControl.Pause();
        _openTap.Pause();
    }

    private void Resume()
    {
        _runControl.Resume();
        _openTap.Resume();
    }

    private void SafetyStop()
    {
        if (_runControl.IsSafetyStopping)
        {
            _runControl.CancelSafetyShutdown();
            return;
        }

        if (!_runControl.IsRunning)
        {
            return;
        }

        _runControl.RequestSafetyStop();
        try
        {
            _safety?.SafeIdle();
        }
        catch
        {
            // safety outranks diagnostics; continue abort even if the adapter throws
        }

        _openTap.Abort(safetyStop: true);
    }
}
