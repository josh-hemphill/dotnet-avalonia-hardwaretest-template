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

    private async void OnRemoveRequiredField(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AuthoringCatalogToggle row })
        {
            await RemoveCatalogAsync(CatalogDeletionKind.RequiredField, row.Id);
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

    private async void OnRemoveReportKind(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AuthoringCatalogToggle row })
        {
            await RemoveCatalogAsync(CatalogDeletionKind.ReportKind, row.Id);
        }
    }

    private void OnAddProgramKind(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.AddProgramKind());

    private async void OnRemoveProgramKind(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AuthoringCatalogToggle row })
        {
            await RemoveCatalogAsync(CatalogDeletionKind.ProgramKind, row.Id);
        }
    }

    private void OnAddInstrumentSlot(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.AddInstrumentSlot());

    private async void OnRemoveInstrumentSlot(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner) await owner.ConfirmInstrumentRemovalAsync();
    }

    private Task<bool> RemoveCatalogAsync(CatalogDeletionKind kind, string target)
        => TopLevel.GetTopLevel(this) is MainWindow owner ? owner.ConfirmCatalogDeletionAsync(kind, target) : Task.FromResult(false);

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
