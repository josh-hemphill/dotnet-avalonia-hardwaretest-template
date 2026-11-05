using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class HardwareView : UserControl
{
    private AuthoringWorkspaceViewModel? _observed;

    public HardwareView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookEditor();
        AttachedToVisualTree += (_, _) => HookEditor();
        DetachedFromVisualTree += (_, _) =>
        {
            if (_observed is not null) _observed.PropertyChanged -= OnEditorChanged;
            _observed = null;
        };
    }
    private AuthoringWorkspaceViewModel? Vm => DataContext as AuthoringWorkspaceViewModel;

    private void HookEditor()
    {
        if (_observed is not null) _observed.PropertyChanged -= OnEditorChanged;
        _observed = Vm;
        if (_observed is not null) _observed.PropertyChanged += OnEditorChanged;
        UpdateDraftState();
    }

    private void OnEditorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(AuthoringWorkspaceViewModel.HardwareEditType)
            or nameof(AuthoringWorkspaceViewModel.HardwareEditAddress) or nameof(AuthoringWorkspaceViewModel.HardwareEditTimeout)
            or nameof(AuthoringWorkspaceViewModel.CanReviewLoadedHardwareBinding) or nameof(AuthoringWorkspaceViewModel.SelectedInstrument)
            or nameof(AuthoringWorkspaceViewModel.SelectedInstrumentSlot)) UpdateDraftState();
    }

    private void UpdateDraftState()
    {
        if (Vm is not { } vm) return;
        var selected = vm.SelectedInstrument;
        var appliedType = selected is not null && AuthoringInstrumentCatalog.TryGet(selected.TypeId, out var adapter)
            ? adapter.TypeId : selected?.TypeId;
        var matches = vm.CanReviewLoadedHardwareBinding && selected is not null
            && vm.HardwareEditType?.TypeId == appliedType
            && string.Equals(vm.HardwareEditAddress.Trim(), selected.VisaAddress, StringComparison.Ordinal)
            && string.Equals(vm.HardwareEditTimeout.Trim(), selected.Settings.GetValueOrDefault("IoTimeoutMilliseconds") ?? string.Empty, StringComparison.Ordinal);
        BindingDraftState.Text = !vm.CanReviewLoadedHardwareBinding
            ? "Load the selected binding to edit it. Your retained values have not changed the plan."
            : matches ? "Matches the applied binding."
            : "Unapplied edits. Review and confirm to update only this plan's binding.";
    }
    // ItemsSource refreshes and inactive task controls may temporarily clear selection.
    // Only an actual non-null choice writes back; model-driven clears still flow one way.
    private void OnHardwareEditTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsEffectivelyVisible: true, SelectedItem: AuthoringInstrumentAdapter adapter } && Vm is { } vm)
            vm.HardwareEditType = adapter;
    }

    private void OnLoadBinding(object? sender, RoutedEventArgs e)
    {
        TryRun(() => Vm?.LoadHardwareEditor());
        UpdateDraftState();
        var target = Vm?.HardwareEditType is null ? (Control)BindingTypeBox : BindingAddressBox;
        target.BringIntoView();
        target.Focus();
    }
    private void OnReloadBinding(object? sender, RoutedEventArgs e) => OnLoadBinding(sender, e);
    private async void OnReviewBinding(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not MainWindow owner || Vm is not { } vm) return;
        await owner.ConfirmHardwareEditAsync();
        if (!owner.IsVisible || !ReferenceEquals(DataContext, vm) || !ReviewBindingButton.IsEffectivelyVisible) return;
        UpdateDraftState();
        Control target = ReviewBindingButton;
        if (vm.Error is not null && vm.CanReviewLoadedHardwareBinding)
        {
            if (vm.HardwareEditType is null) target = BindingTypeBox;
            else if (string.IsNullOrWhiteSpace(vm.HardwareEditAddress)) target = BindingAddressBox;
            else if (!string.IsNullOrWhiteSpace(vm.HardwareEditTimeout)
                && vm.HardwareEditType.ConfigurationFields.Contains("IoTimeoutMilliseconds")
                && (!int.TryParse(vm.HardwareEditTimeout, out var timeout) || timeout <= 0)) target = BindingTimeoutBox;
        }
        target.BringIntoView();
        target.Focus();
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
