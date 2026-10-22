using Avalonia.Controls;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private async void OnPlanSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: AuthoringProgramRow row } picker ||
            string.Equals(row.PlanId, _viewModel.SelectedProgram?.PlanId, StringComparison.OrdinalIgnoreCase)) return;
        await SelectProgramAsync(row.PlanId);
        // Failed or rejected transitions must keep the picker aligned with the
        // draft still shown in the task workspace.
        picker.SelectedItem = _viewModel.SelectedProgramRow;
    }

    internal async Task SelectProgramAsync(string planId)
    {
        if (_transitionInFlight || _destructiveInFlight || _ownerClosed || !IsVisible || _viewModel.LoadingBusy) return;
        var current = OwnerContext();
        _transitionInFlight = true;
        try
        {
            CommitFocusedEditor();
            await _viewModel.SelectProgramAsync(planId, current);
        }
        catch (Exception error) { if (current()) _viewModel.ReportError(error.Message); }
        finally { _transitionInFlight = false; }
    }
}
