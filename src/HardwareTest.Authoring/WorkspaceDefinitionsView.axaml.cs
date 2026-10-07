using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class WorkspaceDefinitionsView : UserControl
{
    public WorkspaceDefinitionsView() => InitializeComponent();

    private AuthoringWorkspaceViewModel? Vm => DataContext as AuthoringWorkspaceViewModel;

    private void OnAddRequiredField(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.AddWorkspaceRequiredField());

    private async void OnRemoveRequiredField(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AuthoringCatalogToggle row })
        {
            await RemoveCatalogAsync(CatalogDeletionKind.RequiredField, row.Id);
        }
    }

    private void OnAddReportKind(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.AddWorkspaceReportKind());

    private async void OnRemoveReportKind(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AuthoringCatalogToggle row })
        {
            await RemoveCatalogAsync(CatalogDeletionKind.ReportKind, row.Id);
        }
    }

    private void OnAddProgramKind(object? sender, RoutedEventArgs e)
        => TryRun(() => Vm?.AddWorkspaceProgramKind());

    private async void OnRemoveProgramKind(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AuthoringCatalogToggle row })
        {
            await RemoveCatalogAsync(CatalogDeletionKind.ProgramKind, row.Id);
        }
    }

    private void OnAddDefinition(object? sender, RoutedEventArgs e) => TryRun(() => Vm?.AddHardwareDefinition());
    private void OnLoadDefinition(object? sender, RoutedEventArgs e) => TryRun(() => Vm?.LoadHardwareDefinitionEditor());
    private void OnUpdateDefinition(object? sender, RoutedEventArgs e) => TryRun(() => Vm?.UpdateHardwareDefinition());
    private void OnIncludeDefinition(object? sender, RoutedEventArgs e) => TryRun(() => Vm?.IncludeHardwareDefinition());
    private async void OnRemoveDefinition(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner) await owner.ConfirmHardwareDefinitionRemovalAsync();
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
