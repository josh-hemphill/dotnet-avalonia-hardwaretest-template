using System.Collections.ObjectModel;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Inspect;

public partial class InspectViewModel : ReactiveObject
{
    private readonly IOpenTapSession _openTap;
    private readonly OperatorSession? _operatorSession;

    public InspectViewModel(IOpenTapSession openTap, OperatorSession? operatorSession = null)
    {
        _openTap = openTap;
        _operatorSession = operatorSession;
        Hierarchy = [];
        RefreshCommand = ReactiveCommand.Create(Refresh);
        OpenOnRunCommand = ReactiveCommand.Create(
            () => OpenOnRunRequested?.Invoke(this, SelectedStep?.Path));
        NavigateToRunCommand = ReactiveCommand.Create(
            () => NavigateToRunRequested?.Invoke(this, EventArgs.Empty));
        Refresh();
    }

    public ObservableCollection<HierarchyStepViewModel> Hierarchy { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenOnRunCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NavigateToRunCommand { get; }

    /// Raised with the selected step's path so the Run page can focus it.
    public event EventHandler<string?>? OpenOnRunRequested;

    /// Raised when the operator requests navigation to the Run page (e.g., empty state CTA).
    public event EventHandler? NavigateToRunRequested;

    [Reactive] private HierarchyStepViewModel? _selectedStep;
    [Reactive] private string _status = "Load a program on Run, then Refresh.";

    public void Refresh()
    {
        _operatorSession?.TouchActivity();
        Hierarchy.Clear();
        foreach (var node in _openTap.StepTree)
        {
            var vm = new HierarchyStepViewModel(node);
            vm.ExpandAll();
            Hierarchy.Add(vm);
        }

        HierarchyRollup.Apply(Hierarchy);
        Status = Hierarchy.Count == 0
            ? "No plan loaded. Open Run and select a program."
            : $"Inspecting {_openTap.LoadedPlanName ?? "plan"} ({CountLeaves(Hierarchy)} leaves).";
        SelectedStep = Hierarchy.FirstOrDefault();
        this.RaisePropertyChanged(nameof(HasPlan));
    }

    public bool HasPlan => Hierarchy.Count > 0;

    private static int CountLeaves(IEnumerable<HierarchyStepViewModel> roots)
        => HierarchyRollup.EnumerateLeaves(roots).Count();
}
