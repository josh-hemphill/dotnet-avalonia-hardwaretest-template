using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class SelectedStepInspectorView : UserControl
{
    public SelectedStepInspectorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveRecipeSelection();
        AttachedToVisualTree += (_, _) => ObserveRecipeSelection();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            foreach (var (expander, name) in new[] { (ConfigureExpander, "Configure selected step"), (IdentityExpander, "Step identity"), (AdvancedExpander, "Advanced step settings") })
            {
                var header = expander.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().FirstOrDefault(button => button.Name == "ExpanderHeader");
                if (header is not null) AutomationProperties.SetName(header, name);
            }
        }, DispatcherPriority.Loaded);
        DetachedFromVisualTree += (_, _) => StopObservingRecipeSelection();
        SizeChanged += (_, _) => ArrangeDensity();
        var menu = (Flyout)InspectorActionsButton.Flyout!;
        menu.Opened += (_, _) => ((ScrollViewer)menu.Content!).FontSize = FontSize;
        ((Control)menu.Content!).AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is Button button && button.Classes.Contains("authoringAction")) menu.Hide();
        });
        ((Control)menu.Content!).AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Handled) return;
            (TopLevel.GetTopLevel(this) as MainWindow)?.OnExpertKeyDown(this, e);
            if (e.Handled) menu.Hide();
        });
    }

    private void ArrangeDensity()
    {
        var compact = Bounds.Height > 0 && Bounds.Height < 300;
        InspectorBody.Spacing = compact ? 10 : 16;
        InspectorBreadcrumbText.IsVisible = !compact;
    }

    private AuthoringWorkspaceViewModel? Vm => DataContext as AuthoringWorkspaceViewModel;
    private AuthoringWorkspaceViewModel? _recipeOwner;
    private bool _recipeSelectionQueued;
    private bool _referenceRevealQueued;
    private Control? _referenceRevealTarget;

    private void OnReferenceInputChanged(object? sender, TextChangedEventArgs e) => QueueReferenceReveal(sender as Control);
    private void OnReferenceFieldFocused(object? sender, FocusChangedEventArgs e) => QueueReferenceReveal(sender as Control);
    private void OnReferenceComparisonChanged(object? sender, SelectionChangedEventArgs e) => QueueReferenceReveal(sender as Control);
    private void QueueReferenceReveal(Control? target)
    {
        if (target?.IsFocused != true) return;
        _referenceRevealTarget = target;
        if (_referenceRevealQueued) return;
        _referenceRevealQueued = true;
        Dispatcher.Post(() =>
        {
            _referenceRevealQueued = false;
            var focused = _referenceRevealTarget;
            _referenceRevealTarget = null;
            if (TopLevel.GetTopLevel(this) is null || focused?.IsFocused != true) return;
            if (ReferenceEquals(focused, ReferenceKeyBox)) ReferenceKeyField.BringIntoView();
            else if (ReferenceEquals(focused, ReferenceComparisonBox)) ReferenceComparisonField.BringIntoView();
            else if (ReferenceEquals(focused, ReferenceToleranceBox)) ReferenceToleranceField.BringIntoView();
            else if (ReferenceEquals(focused, ReferenceRebindButton)) ReferenceTargetField.BringIntoView();
            else if (ReferenceEquals(focused, ReferenceCriterionSelector)) ReferenceCriterionSelector.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void ObserveRecipeSelection()
    {
        StopObservingRecipeSelection();
        _recipeOwner = Vm;
        if (_recipeOwner is not null) _recipeOwner.PropertyChanged += OnRecipePropertyChanged;
        QueueRecipeSelection();
        QueueReferenceSelection();
    }

    private void StopObservingRecipeSelection()
    {
        if (_recipeOwner is not null) _recipeOwner.PropertyChanged -= OnRecipePropertyChanged;
        _recipeOwner = null;
    }

    private void OnRecipePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AuthoringWorkspaceViewModel.SelectedMetricFunction)
            or nameof(AuthoringWorkspaceViewModel.MetricFunctionChoices)) QueueRecipeSelection();
        if (e.PropertyName is nameof(AuthoringWorkspaceViewModel.ReferenceCriterionOptions)
            or nameof(AuthoringWorkspaceViewModel.SelectedReferenceCriterion)) QueueReferenceSelection();
    }

    private bool _referenceSelectionQueued;
    private void QueueReferenceSelection()
    {
        if (_referenceSelectionQueued) return;
        _referenceSelectionQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _referenceSelectionQueued = false;
            if (Vm is not { } vm || TopLevel.GetTopLevel(this) is null) return;
            var current = ReferenceCriterionSelector.Items.OfType<AuthoringReferenceCriterionOption>()
                .FirstOrDefault(option => option.RuleId == vm.SelectedReferenceCriterion?.RuleId);
            if (current is not null && !ReferenceEquals(ReferenceCriterionSelector.SelectedItem, current))
                ReferenceCriterionSelector.SetCurrentValue(ComboBox.SelectedItemProperty, current);
        }, DispatcherPriority.Loaded);
    }

    private void QueueRecipeSelection()
    {
        if (_recipeSelectionQueued) return;
        _recipeSelectionQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _recipeSelectionQueued = false;
            if (_recipeOwner is not { } vm || !ReferenceEquals(vm, Vm) || TopLevel.GetTopLevel(this) is null) return;
            var recipe = this.GetVisualDescendants().OfType<ComboBox>().SingleOrDefault(control =>
                AutomationProperties.GetName(control) == AuthoringInspectorCopy.MeasureRecipeLabel);
            // Changing source families replaces ItemsSource. Avalonia can clear SelectedItem
            // after its bound value was published; replay only the current list's exact choice.
            if (recipe is null || !ReferenceEquals(recipe.ItemsSource, vm.MetricFunctionChoices)) return;
            var selected = vm.SelectedMetricFunction;
            if (selected is not null && recipe.Items.Contains(selected) && !ReferenceEquals(recipe.SelectedItem, selected))
                recipe.SetCurrentValue(ComboBox.SelectedItemProperty, selected);
        }, DispatcherPriority.Loaded);
    }

    private void OnRenameSequence(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        vm.SequenceRename = StepNameBox.Text ?? "";
        vm.RenameSelectedSequence();
    }
    private void OnDuplicateSequence(object? sender, RoutedEventArgs e) => Vm?.DuplicateSelectedSequence();
    private void OnMoveSequenceUp(object? sender, RoutedEventArgs e) => Vm?.MoveSelectedSequence(-1);
    private void OnMoveSequenceDown(object? sender, RoutedEventArgs e) => Vm?.MoveSelectedSequence(1);
    private void OnRemoveSequence(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnRemoveSequence(sender, e);

    public bool FocusFinding(HardwareTest.OpenTap.Host.PlanContractTarget target)
    {
        if (DataContext is not AuthoringWorkspaceViewModel vm || vm.SelectedProgram?.PlanId != target.ProgramId
            || vm.SelectedSequence?.NodeId != target.NodeId) return false;
        var section = target.Section switch
        {
            "Advanced" => AdvancedExpander,
            "Configure" => ConfigureExpander,
            "OperatorDisplay" => OperatorDisplayExpander,
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
        if (AuthoringReferenceRules.BaseField(target.Field) == "ReferenceMeasurementUnit" && vm.HasMetricPresentation)
        {
            if (AuthoringReferenceRules.FieldRuleId(target.Field) is { } unitRuleId) vm.SelectReferenceCriterion(unitRuleId);
            MeasurementUnitField.BringIntoView();
            var editor = YUnitBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(control => control.IsEffectivelyVisible);
            return editor?.Focus() ?? YUnitBox.Focus();
        }
        if (vm.HasMetricPresentation && vm.RequireGoldenReference)
        {
            if (AuthoringReferenceRules.FieldRuleId(target.Field) is { } ruleId) vm.SelectReferenceCriterion(ruleId);
            Control? reference = AuthoringReferenceRules.BaseField(target.Field) switch
            {
                "ReferenceKey" => ReferenceKeyBox,
                "ReferenceComparison" => ReferenceComparisonBox,
                "ReferenceTolerance" => ReferenceToleranceBox,
                "ReferenceTarget" => ReferenceRebindButton,
                "ReferenceRuleIdentity" => this.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(control =>
                    AutomationProperties.GetName(control) == "Require golden reference"),
                _ => null,
            };
            if (reference is not null)
            {
                if (ReferenceEquals(reference, ReferenceToleranceBox)) ReferenceToleranceField.BringIntoView();
                else if (ReferenceEquals(reference, ReferenceKeyBox)) ReferenceKeyField.BringIntoView();
                else if (ReferenceEquals(reference, ReferenceComparisonBox)) ReferenceComparisonField.BringIntoView();
                else if (ReferenceEquals(reference, ReferenceRebindButton)) ReferenceTargetField.BringIntoView();
                else reference.BringIntoView();
                return reference.Focus();
            }
        }
        var setting = ConfigureExpander.GetVisualDescendants().OfType<Control>().FirstOrDefault(control =>
            control.IsEffectivelyVisible && control.IsEffectivelyEnabled
            && control.DataContext is AuthoringSettingRow row && row.NodeId == target.NodeId && row.Key == target.Field
            && Avalonia.Automation.AutomationProperties.GetName(control) == target.Field
            && control is TextBox or ComboBox or CheckBox);
        if (setting is not null) { setting.BringIntoView(); return setting.Focus(); }
        return false;
    }

    private void OnToggleSection(object? sender, RoutedEventArgs e) => Vm?.ToggleSelectedSection();
    private void OnRebindReference(object? sender, RoutedEventArgs e) => Vm?.RebindReferenceToMeasurement();
    private void OnAddReferenceCriterion(object? sender, RoutedEventArgs e) => Vm?.AddReferenceCriterion();
    private void OnRemoveReferenceCriterion(object? sender, RoutedEventArgs e) => Vm?.RemoveSelectedReferenceCriterion();
    private void OnMoveToSection(object? sender, RoutedEventArgs e) => Vm?.MoveSelectedToSection();
    internal void FocusName() { IdentityExpander.IsExpanded = true; StepNameBox.BringIntoView(); StepNameBox.Focus(); }
    private void OnFocusName(object? sender, RoutedEventArgs e)
        => Avalonia.Threading.Dispatcher.UIThread.Post(FocusName);
    internal void FocusPrimaryEditor()
    {
        ConfigureExpander.IsExpanded = true;
        var field = ConfigureExpander.GetVisualDescendants().OfType<Control>().FirstOrDefault(control =>
            control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control is TextBox or ComboBox or CheckBox or AutoCompleteBox);
        field?.BringIntoView();
        field?.Focus();
    }
    internal void FocusSectionDestination()
    {
        InspectorActionsButton.Flyout!.ShowAt(InspectorActionsButton);
        SectionDestinationBox.BringIntoView();
        SectionDestinationBox.Focus();
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
