using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class HardwareView : UserControl
{
    public HardwareView() => InitializeComponent();
    private AuthoringWorkspaceViewModel? Vm => DataContext as AuthoringWorkspaceViewModel;
    private void OnLoadBinding(object? sender, RoutedEventArgs e) => TryRun(() => Vm?.LoadHardwareEditor());
    private async void OnReviewBinding(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner) await owner.ConfirmHardwareEditAsync();
    }
    private void OnAddInstrumentSlot(object? sender, RoutedEventArgs e) => TryRun(() => Vm?.AddInstrumentSlot());
    private void OnOpenDefinitions(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OpenDefinitions("HardwareTemplateSection");
    private void OnOpenEnvironment(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner) owner.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 3;
    }
    private async void OnRemoveInstrumentSlot(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner) await owner.ConfirmInstrumentRemovalAsync();
    }
    private void TryRun(Action action)
    {
        try { action(); }
        catch (Exception error) { Vm?.ReportError(error.Message); }
    }
}
