using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class HardwareView : UserControl
{
    public HardwareView() => InitializeComponent();
    private AuthoringWorkspaceViewModel? Vm => DataContext as AuthoringWorkspaceViewModel;
    // ItemsSource refreshes and inactive task controls may temporarily clear selection.
    // Only an actual non-null choice writes back; model-driven clears still flow one way.
    private void OnHardwareEditTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsEffectivelyVisible: true, SelectedItem: AuthoringInstrumentAdapter adapter } && Vm is { } vm)
            vm.HardwareEditType = adapter;
    }

    private void OnLoadBinding(object? sender, RoutedEventArgs e) => TryRun(() => Vm?.LoadHardwareEditor());
    private async void OnReviewBinding(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner) await owner.ConfirmHardwareEditAsync();
    }
    private void OnOpenEnvironment(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner)
            owner.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 3;
    }
    private void OnShowAddInstrument(object? sender, RoutedEventArgs e)
    {
        var section = this.FindControl<Expander>("AddInstrumentSection")!;
        section.IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var input = this.GetVisualDescendants().OfType<TextBox>().First(control =>
                Avalonia.Automation.AutomationProperties.GetName(control) == "Add instrument slot");
            input.BringIntoView();
            input.Focus();
        });
    }
    private void OnDeferBinding(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner)
            owner.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0;
    }
    private void OnAddInstrumentSlot(object? sender, RoutedEventArgs e) => TryRun(() => Vm?.AddInstrumentSlot());
    private void OnOpenDefinitions(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OpenDefinitions("HardwareTemplateSection");
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
