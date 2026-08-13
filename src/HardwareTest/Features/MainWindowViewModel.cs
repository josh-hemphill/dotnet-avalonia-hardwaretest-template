using System;
using System.Collections.ObjectModel;
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
    private readonly IOpenTapSession _openTap;

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
        IOpenTapSession openTap)
    {
        _settingsStore = settingsStore;
        _runControl = runControl;
        _openTap = openTap;
        RunTest = runTest;
        Inspect = inspect;
        Results = results;
        NavigationItems =
        [
            new NavItem { Id = "Home", Title = "Home", ViewModel = home, Symbol = FASymbol.Home },
            new NavItem { Id = "RunTest", Title = "Run", ViewModel = runTest, Symbol = FASymbol.Play },
            new NavItem { Id = "Inspect", Title = "Inspect", ViewModel = inspect, Symbol = FASymbol.Document },
            new NavItem { Id = "Results", Title = "Results", ViewModel = results, Symbol = FASymbol.List },
            new NavItem { Id = "ReportPreview", Title = "Report Preview", ViewModel = reportPreview, Symbol = FASymbol.Document },
            new NavItem { Id = "Instruments", Title = "Instruments", ViewModel = instruments, Symbol = FASymbol.Repair },
            new NavItem { Id = "Settings", Title = "Settings", ViewModel = settings, Symbol = FASymbol.Settings },
        ];

        var selectedId = settingsStore.UiState.SelectedPageId;
        SelectedItem = NavigationItems.FirstOrDefault(i => i.Id == selectedId) ?? NavigationItems[0];
        CurrentPage = SelectedItem.ViewModel;
        if (SelectedItem.Id == "Results")
        {
            _ = Results.LoadRunsAsync();
        }
        else if (SelectedItem.Id == "Inspect")
        {
            Inspect.Refresh();
        }

        PauseResumeCommand = ReactiveCommand.Create(PauseResume);
        PauseCommand = ReactiveCommand.Create(Pause);
        ResumeCommand = ReactiveCommand.Create(Resume);
        SafetyStopCommand = ReactiveCommand.Create(SafetyStop);

        runTest.NavigateToResultsRequested += (_, _) =>
        {
            NavigateToPageId("Results");
            _ = Results.OpenRunByIdAsync(runTest.LastRunId);
        };
        runTest.NavigateToInspectRequested += (_, _) => NavigateToPageId("Inspect");
        inspect.OpenOnRunRequested += (_, stepPath) =>
        {
            runTest.ApplySelectionFromInspect(stepPath);
            NavigateToPageId("RunTest");
        };

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
            if (e.PropertyName is nameof(IOpenTapSession.IsAwaitingOperator)
                or nameof(IOpenTapSession.OperatorPromptMessage)
                or nameof(IOpenTapSession.PendingInteraction))
            {
                RaiseTransportProps();
            }
        };
    }

    public RunTestViewModel RunTest { get; }
    public InspectViewModel Inspect { get; }
    public ResultsViewModel Results { get; }
    public IRunControl RunControl => _runControl;

    public ObservableCollection<NavItem> NavigationItems { get; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> PauseResumeCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> PauseCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ResumeCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SafetyStopCommand { get; }

    public bool IsPaused => _runControl.IsPaused;
    public bool IsRunning => _runControl.IsRunning;
    public bool IsSafetyStopping => _runControl.IsSafetyStopping;
    public bool IsAwaitingOperator => _openTap.IsAwaitingOperator;

    /// Pause icon when running and not awaiting / soft-paused.
    public bool ShowPauseIcon => IsRunning && !IsPaused && !IsAwaitingOperator;
    /// Resume (play) when soft-paused and not awaiting operator input.
    public bool ShowResumeIcon => IsPaused && !IsAwaitingOperator;
    /// Continue when the host is waiting on an operator interaction.
    public bool ShowContinueIcon => IsAwaitingOperator;

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

    public string SafetyStopLabel => IsSafetyStopping ? "Cancel shutdown" : "Safety Stop";

    public string SafetyStopTip => IsSafetyStopping
        ? "Cancel the in-progress safety shutdown"
        : "Safety Stop — abort and run safe shutdown";

    public string ControlStatus
    {
        get
        {
            if (_runControl.IsSafetyStopping)
            {
                return "Safety stop…";
            }

            if (IsAwaitingOperator)
            {
                return string.IsNullOrWhiteSpace(_openTap.OperatorPromptMessage)
                    ? "Awaiting operator"
                    : _openTap.OperatorPromptMessage!;
            }

            if (_runControl.IsPaused)
            {
                return "Paused";
            }

            return _runControl.IsRunning ? "Running" : "Idle";
        }
    }

    [Reactive]
    private NavItem? _selectedItem;

    [Reactive]
    private object? _currentPage;

    /// Whether the left nav pane is expanded (false = compact / icon-only footer).
    [Reactive]
    private bool _isNavPaneOpen = true;

    public void NavigateTo(NavItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;
        CurrentPage = item.ViewModel;
        _settingsStore.UiState.SelectedPageId = item.Id;
        RunTest.SessionPanel.TouchActivity();
        if (item.Id == "Results")
        {
            _ = Results.LoadRunsAsync();
        }
        else if (item.Id == "Inspect")
        {
            Inspect.Refresh();
        }
    }

    public void NavigateToPageId(string pageId)
    {
        var item = NavigationItems.FirstOrDefault(i => i.Id == pageId);
        NavigateTo(item);
    }

    private void RaiseTransportProps()
    {
        this.RaisePropertyChanged(nameof(IsPaused));
        this.RaisePropertyChanged(nameof(IsRunning));
        this.RaisePropertyChanged(nameof(IsSafetyStopping));
        this.RaisePropertyChanged(nameof(IsAwaitingOperator));
        this.RaisePropertyChanged(nameof(ControlStatus));
        this.RaisePropertyChanged(nameof(PauseResumeLabel));
        this.RaisePropertyChanged(nameof(PauseResumeTip));
        this.RaisePropertyChanged(nameof(SafetyStopLabel));
        this.RaisePropertyChanged(nameof(SafetyStopTip));
        this.RaisePropertyChanged(nameof(ShowPauseIcon));
        this.RaisePropertyChanged(nameof(ShowResumeIcon));
        this.RaisePropertyChanged(nameof(ShowContinueIcon));
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
        if (SelectedItem?.Id != "RunTest")
        {
            NavigateToPageId("RunTest");
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
        _openTap.Abort(safetyStop: true);
    }
}
