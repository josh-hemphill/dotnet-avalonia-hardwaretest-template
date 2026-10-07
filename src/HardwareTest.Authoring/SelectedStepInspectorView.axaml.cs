using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class SelectedStepInspectorView : UserControl
{
    public SelectedStepInspectorView() => InitializeComponent();

    private void OnToggleCleanupSlot(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnToggleCleanupSlot(sender, e);

    private void OnMetricSettingLostFocus(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnMetricSettingLostFocus(sender, e);

    private void OnMetricSettingBoolChanged(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnMetricSettingBoolChanged(sender, e);

    private void OnMetricSettingChoiceChanged(object? sender, SelectionChangedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnMetricSettingChoiceChanged(sender, e);

    private void OnMetricSettingNumberChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnMetricSettingNumberChanged(sender, e);

    private void OnFormulaCaretChanged(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnFormulaCaretChanged(sender, e);

    private void OnFormulaChip(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnFormulaChip(sender, e);

    private void OnImportTransferFunction(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnImportTransferFunction(sender, e);
}
