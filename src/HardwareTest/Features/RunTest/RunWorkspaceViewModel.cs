using ReactiveUI;

namespace HardwareTest.Features.RunTest;

/// Owns Steps / Details / Chart selection and overlay precedence (preparation, interaction).
public sealed class RunWorkspaceViewModel : ReactiveObject
{
    private readonly Func<bool> _sessionBlocked;
    private readonly Func<bool> _awaitingOperator;
    private readonly Func<bool> _hasChartData;
    private readonly Func<bool> _hasStepSelection;
    private readonly Action<bool> _setDetailVisible;

    public RunWorkspaceViewModel(
        Func<bool> sessionBlocked,
        Func<bool> awaitingOperator,
        Func<bool> hasChartData,
        Func<bool> hasStepSelection,
        Action<bool>? setDetailVisible = null)
    {
        _sessionBlocked = sessionBlocked;
        _awaitingOperator = awaitingOperator;
        _hasChartData = hasChartData;
        _hasStepSelection = hasStepSelection;
        _setDetailVisible = setDetailVisible ?? (_ => { });

        OpenStepsCommand = ReactiveCommand.Create(OpenSteps);
        OpenDetailsCommand = ReactiveCommand.Create(OpenDetails);
        OpenChartCommand = ReactiveCommand.Create(OpenChart);
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenStepsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenDetailsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenChartCommand { get; }

    public RunWorkspace Selected { get; private set; } = RunWorkspace.Steps;

    public bool ShowPreparation => _sessionBlocked();
    public bool ShowInteraction => !ShowPreparation && _awaitingOperator();
    public bool ShowModeSwitcher => !ShowPreparation && !ShowInteraction;
    public bool ShowSteps => ShowWorkspace(RunWorkspace.Steps);
    public bool ShowDetails => ShowWorkspace(RunWorkspace.Details);
    public bool ShowChart => ShowWorkspace(RunWorkspace.Chart);
    public bool CanOpenDetails => _hasStepSelection();
    public bool CanOpenChart => _hasChartData() || Selected.Equals(RunWorkspace.Chart);
    public bool CanReturnToSteps => ShowDetails || ShowChart;
    public bool IsStepsSelected => Selected.Equals(RunWorkspace.Steps);
    public bool IsDetailsSelected => Selected.Equals(RunWorkspace.Details);
    public bool IsChartSelected => Selected.Equals(RunWorkspace.Chart);

    /// Recomputes overlay visibility after session, interaction, or chart-data changes.
    public void Refresh()
    {
        this.RaisePropertyChanged(nameof(ShowPreparation));
        this.RaisePropertyChanged(nameof(ShowInteraction));
        this.RaisePropertyChanged(nameof(ShowModeSwitcher));
        this.RaisePropertyChanged(nameof(ShowSteps));
        this.RaisePropertyChanged(nameof(ShowDetails));
        this.RaisePropertyChanged(nameof(ShowChart));
        this.RaisePropertyChanged(nameof(CanOpenDetails));
        this.RaisePropertyChanged(nameof(CanOpenChart));
        this.RaisePropertyChanged(nameof(CanReturnToSteps));
        this.RaisePropertyChanged(nameof(IsStepsSelected));
        this.RaisePropertyChanged(nameof(IsDetailsSelected));
        this.RaisePropertyChanged(nameof(IsChartSelected));
    }

    /// Returns to the step list (program load / Esc / Back).
    public void OpenSteps() => SetSelected(RunWorkspace.Steps);

    /// Opens Details when a step is selected.
    public void OpenDetails()
    {
        if (!_hasStepSelection())
        {
            return;
        }

        SetSelected(RunWorkspace.Details);
    }

    /// Opens Chart when timeseries data exists or Chart is already selected.
    public void OpenChart()
    {
        if (!_hasChartData() && !Selected.Equals(RunWorkspace.Chart))
        {
            return;
        }

        SetSelected(RunWorkspace.Chart);
    }

    /// Program change always lands on Steps.
    public void ResetToSteps() => OpenSteps();

    private bool ShowWorkspace(RunWorkspace workspace)
        => !ShowPreparation && !ShowInteraction && Selected.Equals(workspace);

    private void SetSelected(RunWorkspace workspace)
    {
        Selected = workspace;
        _setDetailVisible(workspace.Equals(RunWorkspace.Details));
        this.RaisePropertyChanged(nameof(Selected));
        Refresh();
    }
}
