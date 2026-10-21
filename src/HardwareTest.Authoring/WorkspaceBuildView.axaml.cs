using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class WorkspaceBuildView : UserControl
{
    public WorkspaceBuildView() => InitializeComponent();

    private void OnProgramInclusion(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: AuthoringBuildProgramLine row } box
            && DataContext is AuthoringWorkspaceViewModel vm && TopLevel.GetTopLevel(this) is MainWindow { IsVisible: true })
            vm.SetBuildProgramIncluded(row.PlanId, box.IsChecked == true);
    }

    private void OnReviewEnvironment(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner)
            owner.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 3;
    }

    private void OnPack(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnPack(sender, e);
}
