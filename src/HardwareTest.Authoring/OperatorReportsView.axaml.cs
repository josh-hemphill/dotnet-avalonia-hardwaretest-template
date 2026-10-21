using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class OperatorReportsView : UserControl
{
    public OperatorReportsView() => InitializeComponent();

    private void OnAddPrompt(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not MainWindow owner || Vm is not { } vm) return;
        owner.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0;
        vm.RecipeSearch = "Operator Prompt";
        vm.SelectedRecipeId = AuthoringRecipeIds.Prompt;
        vm.InsertionPosition = "End of section";
        owner.OnOpenStepPalette(sender, e);
    }

    private void OnOpenDefinitions(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OpenDefinitions("OperatorFieldSection");

    private AuthoringWorkspaceViewModel? Vm => DataContext as AuthoringWorkspaceViewModel;
    private void OnAddRequiredField(object? sender, RoutedEventArgs e) => TryRun(() => Vm?.AddWorkspaceRequiredField());
    private void OnAddReportKind(object? sender, RoutedEventArgs e) => TryRun(() => Vm?.AddWorkspaceReportKind());

    private void OnToggleRequiredField(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: AuthoringCatalogToggle row } box)
        {
            TryRun(() => Vm?.SetRequiredFieldIncluded(row.Id, box.IsChecked == true));
        }
    }

    private void OnToggleReportKind(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: AuthoringCatalogToggle row } box)
        {
            TryRun(() => Vm?.SetReportKindIncluded(row.Id, box.IsChecked == true));
        }
    }

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
