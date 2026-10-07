using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class SelectedStepInspectorView : UserControl
{
    public SelectedStepInspectorView() => InitializeComponent();

    private void OnToggleCleanupSlot(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnToggleCleanupSlot(sender, e);

    private void OnMetricSettingTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: AuthoringSettingRow row } box
            && row.IsNumber && SettingWindow(sender) is not null && DataContext is AuthoringWorkspaceViewModel vm
            && !string.Equals(row.Value, box.Text, StringComparison.Ordinal))
            vm.SetMetricSetting(row.Key, box.Text ?? string.Empty);
    }

    private void OnMetricSettingLostFocus(object? sender, RoutedEventArgs e)
        => SettingWindow(sender)?.OnMetricSettingLostFocus(sender, e);

    private void OnMetricSettingBoolChanged(object? sender, RoutedEventArgs e)
        => SettingWindow(sender)?.OnMetricSettingBoolChanged(sender, e);

    private void OnMetricSettingChoiceChanged(object? sender, SelectionChangedEventArgs e)
        => SettingWindow(sender)?.OnMetricSettingChoiceChanged(sender, e);

    private void OnMetricSettingNumberChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        => SettingWindow(sender)?.OnMetricSettingNumberChanged(sender, e);

    private MainWindow? SettingWindow(object? sender)
        => sender is Control { DataContext: AuthoringSettingRow row }
           && DataContext is AuthoringWorkspaceViewModel vm
           && row.NodeId == vm.SelectedSequence?.NodeId
            ? TopLevel.GetTopLevel(this) as MainWindow : null;

    private void OnFormulaCaretChanged(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnFormulaCaretChanged(sender, e);

    private void OnFormulaChip(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnFormulaChip(sender, e);

    private void OnImportTransferFunction(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnImportTransferFunction(sender, e);
}
