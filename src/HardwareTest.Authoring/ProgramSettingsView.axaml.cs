using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class ProgramSettingsView : UserControl
{
    public ProgramSettingsView() => InitializeComponent();

    private AuthoringWorkspaceViewModel? Vm => DataContext as AuthoringWorkspaceViewModel;

    private void OnToggleRequiredField(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: AuthoringCatalogToggle row } box)
        {
            TryRun(() => Vm?.SetRequiredFieldIncluded(row.Id, box.IsChecked == true));
        }
    }

    private void OnAddRequiredField(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.AddRequiredField());

    private void OnRemoveRequiredField(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AuthoringCatalogToggle row })
        {
            TryRun(() => Vm?.RemoveRequiredField(row.Id));
        }
    }

    private void OnToggleReportKind(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: AuthoringCatalogToggle row } box)
        {
            TryRun(() => Vm?.SetReportKindIncluded(row.Id, box.IsChecked == true));
        }
    }

    private void OnAddReportKind(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.AddReportKind());

    private void OnRemoveReportKind(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AuthoringCatalogToggle row })
        {
            TryRun(() => Vm?.RemoveReportKind(row.Id));
        }
    }

    private void OnAddProgramKind(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.AddProgramKind());

    private void OnRemoveProgramKind(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.RemoveProgramKindFromCatalog());

    private void OnAddInstrumentSlot(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.AddInstrumentSlot());

    private void OnRemoveInstrumentSlot(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.RemoveSelectedInstrumentSlot());

    private void TryRun(Action action)
    {
        if (Vm is null)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            Vm.ReportError(ex.Message);
        }
    }
}
