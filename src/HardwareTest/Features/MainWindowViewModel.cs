using System;
using System.Collections.ObjectModel;
using FluentAvalonia.UI.Controls;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Home;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;
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

    public MainWindowViewModel(
        ISettingsStore settingsStore,
        HomeViewModel home,
        RunTestViewModel runTest,
        ResultsViewModel results,
        ReportPreviewViewModel reportPreview,
        InstrumentsViewModel instruments,
        SettingsViewModel settings,
        IRunControl runControl)
    {
        _settingsStore = settingsStore;
        _runControl = runControl;
        RunTest = runTest;
        NavigationItems =
        [
            new NavItem { Id = "Home", Title = "Home", ViewModel = home, Symbol = FASymbol.Home },
            new NavItem { Id = "RunTest", Title = "Run", ViewModel = runTest, Symbol = FASymbol.Play },
            new NavItem { Id = "Results", Title = "Results", ViewModel = results, Symbol = FASymbol.List },
            new NavItem { Id = "ReportPreview", Title = "Report Preview", ViewModel = reportPreview, Symbol = FASymbol.Document },
            new NavItem { Id = "Instruments", Title = "Instruments", ViewModel = instruments, Symbol = FASymbol.Repair },
            new NavItem { Id = "Settings", Title = "Settings", ViewModel = settings, Symbol = FASymbol.Settings },
        ];

        var selectedId = settingsStore.UiState.SelectedPageId;
        SelectedItem = NavigationItems.FirstOrDefault(i => i.Id == selectedId) ?? NavigationItems[0];
        CurrentPage = SelectedItem.ViewModel;

        PauseCommand = ReactiveCommand.Create(Pause);
        ResumeCommand = ReactiveCommand.Create(Resume);
        SafetyStopCommand = ReactiveCommand.Create(SafetyStop);

        runControl.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IRunControl.IsPaused)
                or nameof(IRunControl.IsRunning)
                or nameof(IRunControl.IsSafetyStopping))
            {
                this.RaisePropertyChanged(nameof(IsPaused));
                this.RaisePropertyChanged(nameof(IsRunning));
                this.RaisePropertyChanged(nameof(IsSafetyStopping));
                this.RaisePropertyChanged(nameof(ControlStatus));
            }
        };
    }

    public RunTestViewModel RunTest { get; }
    public IRunControl RunControl => _runControl;

    public ObservableCollection<NavItem> NavigationItems { get; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> PauseCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ResumeCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SafetyStopCommand { get; }

    public bool IsPaused => _runControl.IsPaused;
    public bool IsRunning => _runControl.IsRunning;
    public bool IsSafetyStopping => _runControl.IsSafetyStopping;

    public string ControlStatus
    {
        get
        {
            if (_runControl.IsSafetyStopping)
            {
                return "Safety stop…";
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

    public void NavigateTo(NavItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;
        CurrentPage = item.ViewModel;
        _settingsStore.UiState.SelectedPageId = item.Id;
    }

    public void NavigateToPageId(string pageId)
    {
        var item = NavigationItems.FirstOrDefault(i => i.Id == pageId);
        NavigateTo(item);
    }

    private void Pause() => _runControl.Pause();

    private void Resume() => _runControl.Resume();

    private void SafetyStop()
    {
        if (_runControl.IsSafetyStopping)
        {
            _runControl.CancelSafetyShutdown();
            return;
        }

        if (!_runControl.IsRunning)
        {
            // Idle: nothing to abort; status stays Idle.
            return;
        }

        _runControl.RequestSafetyStop();
    }
}
