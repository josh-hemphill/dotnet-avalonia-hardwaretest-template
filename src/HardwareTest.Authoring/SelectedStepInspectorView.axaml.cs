using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class SelectedStepInspectorView : UserControl
{
    public SelectedStepInspectorView() => InitializeComponent();

    public bool FocusFinding(HardwareTest.OpenTap.Host.PlanContractTarget target)
    {
        if (DataContext is not AuthoringWorkspaceViewModel vm || vm.SelectedProgram?.PlanId != target.ProgramId
            || vm.SelectedSequence?.NodeId != target.NodeId) return false;
        var section = target.Section switch
        {
            "Advanced" => AdvancedExpander,
            "Configure" => ConfigureExpander,
            _ => null,
        };
        if (section is null) return false;
        section.IsExpanded = true;
        if (target.Field is null)
        {
            section.BringIntoView();
            return true;
        }
        if (target.Field == "Threshold" && vm.ShowThreshold)
        {
            ThresholdBox.BringIntoView();
            return ThresholdBox.Focus();
        }
        if (target.Field == "LimitLow" && vm.ShowBandLimits)
        {
            LimitLowBox.BringIntoView();
            return LimitLowBox.Focus();
        }
        if (target.Field == "ChannelKey" && vm.HasMetricPresentation)
        {
            ChannelKeyBox.BringIntoView();
            var editors = ChannelKeyBox.GetVisualDescendants().OfType<TextBox>().Where(editor => editor.IsEffectivelyVisible).ToArray();
            return editors.Length == 1 && editors[0].Focus();
        }
        return false;
    }

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

    private void OnFormulaThreshold(object? sender, RoutedEventArgs e)
    {
        ConfigureExpander.IsExpanded = true;
        ThresholdBox.Focus();
        ThresholdBox.BringIntoView();
    }

    private void OnFormulaCaretChanged(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnFormulaCaretChanged(sender, e);

    private void OnFormulaChip(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnFormulaChip(sender, e);

    private void OnImportTransferFunction(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnImportTransferFunction(sender, e);
}
