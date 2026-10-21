using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class ProgramsRailView : UserControl
{
    public ProgramsRailView() => InitializeComponent();

    private void OnCreateProgram(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnCreateProgram(sender, e);

    private void OnRemoveProgram(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnRemoveProgram(sender, e);

    private void OnOpenLastWorkspace(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnOpenLastWorkspace(sender, e);

    private void OnProgramsKeyDown(object? sender, KeyEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnProgramsKeyDown(sender, e);

    private async void OnProgramSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: AuthoringProgramRow row } list
            && DataContext is AuthoringWorkspaceViewModel vm
            && !string.Equals(row.PlanId, vm.SelectedProgram?.PlanId, StringComparison.OrdinalIgnoreCase)
            && TopLevel.GetTopLevel(this) is MainWindow window)
        {
            await window.SelectProgramAsync(row.PlanId);
            // A rejected or failed transition keeps the highlighted row consistent
            // with the draft that remains in the editor.
            if (ReferenceEquals(DataContext, vm)) list.SelectedItem = vm.SelectedProgramRow;
        }
    }
}
