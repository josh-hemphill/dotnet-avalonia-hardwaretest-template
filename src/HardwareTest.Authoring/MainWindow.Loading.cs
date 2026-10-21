namespace HardwareTest.Authoring;

public partial class MainWindow
{
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
