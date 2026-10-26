using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class HardwareGuidanceTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Secondary_hardware_panels_preserve_width_selection_and_unapplied_edits(bool dark)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        fixture.OpenRememberedWorkspace();
        await fixture.ViewModel.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        fixture.NavigateTask(1);
        var vm = fixture.ViewModel;
        var slots = fixture.Control<ListBox>("Instruments");
        var add = fixture.Control<Expander>("Add instrument section");
        var remove = fixture.Control<Expander>("Remove instrument section");
        var browse = window.GetVisualDescendants().OfType<Expander>().Single(item => Equals(item.Header, "Browse available types"));
        Assert.False(add.IsExpanded);
        Assert.False(remove.IsExpanded);
        Assert.False(browse.IsExpanded);
        var load = fixture.Control<Button>("Load selected binding");
        load.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(load);
        vm.HardwareEditAddress = "MOCK::PENDING";
        var original = vm.SelectedProgram;
        var selected = slots.SelectedItem;
        var width = slots.Bounds.Width;
        var panels = new[] { add, remove, browse };
        foreach (var panel in panels)
        {
            var panelWidth = panel.Bounds.Width;
            panel.IsExpanded = true; AuthoringUiFixture.Drain();
            Assert.Equal(panelWidth, panel.Bounds.Width);
            Assert.Equal(width, slots.Bounds.Width);
            panel.IsExpanded = false; AuthoringUiFixture.Drain();
            Assert.Equal(panelWidth, panel.Bounds.Width);
        }
        Assert.Same(selected, slots.SelectedItem);
        Assert.Equal("MOCK::PENDING", vm.HardwareEditAddress);
        Assert.Same(original, vm.SelectedProgram);
        var addAction = fixture.Control<Button>("Open add instrument");
        addAction.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(addAction);
        Assert.True(add.IsExpanded);
        Assert.True(fixture.Control<TextBox>("Add instrument slot").IsFocused);
        Assert.False(vm.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task Pending_environment_rescan_preserves_loaded_adapter_and_typed_values()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        await vm.WaitForEnvironmentInspectionAsync();
        fixture.NavigateTask(1); vm.LoadHardwareEditor(); AuthoringUiFixture.Drain();
        var type = fixture.Control<ComboBox>("Hardware adapter type");
        var selected = type.SelectedItem;
        Assert.NotNull(selected);
        vm.HardwareEditAddress = "MOCK::PENDING";
        vm.NewInstrumentSlot = "PENDING";
        vm.NewInstrumentVisa = "MOCK::READY";
        Assert.True(vm.CanAddInstrumentSlot);
        var inspect = vm.EnvironmentInspector;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        vm.EnvironmentInspector = (workspace, home) =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Inspection gate did not release.");
            return inspect(workspace, home);
        };
        var pending = vm.RefreshEnvironmentInspectionAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            AuthoringUiFixture.Drain();
            Assert.True(vm.EnvironmentInspectionPending);
            Assert.Same(selected, type.SelectedItem);
            Assert.Same(selected, vm.HardwareEditType);
            Assert.Equal("MOCK::PENDING", vm.HardwareEditAddress);
            Assert.False(vm.CanPack);
            Assert.False(vm.CanAddInstrumentSlot);
        }
        finally { release.Set(); await pending; }
        AuthoringUiFixture.Drain();
        Assert.Same(selected, type.SelectedItem);
        Assert.Equal("MOCK::PENDING", vm.HardwareEditAddress);
        fixture.NavigateTask(6);
        Assert.Same(selected, fixture.Control<ComboBox>("Hardware adapter type").SelectedItem);
        fixture.NavigateTask(1);
        Assert.Same(selected, fixture.Control<ComboBox>("Hardware adapter type").SelectedItem);
        Assert.Same(selected, vm.HardwareEditType);
    }

    [AvaloniaFact]
    public async Task Binding_editor_retains_values_but_requires_reload_after_slot_or_plan_changes()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        await fixture.ViewModel.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        var vm = fixture.ViewModel;
        vm.NewInstrumentSlot = "BENCH"; vm.NewInstrumentVisa = "MOCK::BENCH"; vm.AddInstrumentSlot();
        await vm.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        vm.SelectedInstrumentSlot = "DMM";
        fixture.NavigateTask(1); vm.LoadHardwareEditor();
        vm.HardwareEditAddress = "MOCK::UNAPPLIED";
        vm.SelectedInstrumentSlot = "BENCH"; AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<Button>("Review binding change").IsEnabled);
        Assert.Equal("MOCK::UNAPPLIED", fixture.Control<TextBox>("Hardware adapter address").Text);
        Assert.Contains("DMM", fixture.Control<TextBlock>("Loaded binding context").Text);
        Assert.Contains("Load the selected binding", fixture.Control<TextBlock>("Loaded binding context").Text);
        Assert.Contains("Load the selected binding", Assert.Throws<AuthoringWorkspaceException>(() => vm.PrepareHardwareEdit()).Message);
        Assert.Equal("MOCK::BENCH", vm.SelectedInstrument!.VisaAddress);
        var load = fixture.Control<Button>("Load selected binding");
        load.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(load);
        Assert.True(fixture.Control<Button>("Review binding change").IsEnabled);
        Assert.Equal("MOCK::BENCH", vm.HardwareEditAddress);
        vm.CreateDemoProgram("another-plan"); AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<Button>("Review binding change").IsEnabled);
        Assert.Contains("Load the selected binding", Assert.Throws<AuthoringWorkspaceException>(() => vm.PrepareHardwareEdit()).Message);
    }

    [AvaloniaFact]
    public async Task Selecting_a_slot_shows_only_its_binding_and_loads_it_for_review()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        await fixture.ViewModel.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        var vm = fixture.ViewModel;
        vm.NewInstrumentSlot = "BENCH"; vm.NewInstrumentVisa = "MOCK::BENCH"; vm.AddInstrumentSlot();
        await vm.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        fixture.NavigateTask(1);
        var slots = fixture.Control<ListBox>("Instruments");
        slots.SelectedItem = vm.Instruments.Single(instrument => instrument.SlotName == "BENCH");
        AuthoringUiFixture.Drain();
        var current = Assert.IsType<AuthoringHardwareRow>(Assert.Single(fixture.Control<ItemsControl>("Hardware binding table").Items));
        Assert.Equal("BENCH", current.SlotName);
        var load = fixture.Control<Button>("Load selected binding");
        load.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(load);
        Assert.Equal("MOCK::BENCH", fixture.Control<TextBox>("Hardware adapter address").Text);
        Assert.NotNull(fixture.Control<ComboBox>("Hardware adapter type").SelectedItem);
    }

    [AvaloniaFact]
    public async Task Unsupported_imported_type_is_distinguished_from_missing_package_without_changing_source()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        await fixture.ViewModel.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        var vm = fixture.ViewModel;
        const string importedType = "Vendor.CustomOscilloscope";
        vm.ReplaceSelected(vm.SelectedProgram! with { Instruments = [new("SCOPE", importedType, "TCPIP::bench::INSTR")] });
        fixture.NavigateTask(1);
        vm.LoadHardwareEditor(); AuthoringUiFixture.Drain();
        var original = vm.SelectedProgram;
        var text = fixture.Control<TextBlock>("Selected adapter guidance").Text!;
        Assert.Contains(importedType, text);
        Assert.Contains("no registered authoring adapter", text);
        Assert.Contains("Installing a package alone does not add editor support", text);
        Assert.Contains("original editor", text);
        Assert.Null(vm.HardwareEditType);
        Assert.Equal("Unsupported authoring adapter", Assert.Single(vm.SelectedHardwareRows).PackageSummary);
        Assert.Same(original, vm.SelectedProgram);
    }

    [AvaloniaFact]
    public async Task Missing_package_names_repair_and_keeps_full_paths_in_collapsed_details()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        await fixture.ViewModel.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        var vm = fixture.ViewModel;
        vm.OpenTapHomeOverride = Path.Combine(fixture.WorkspaceRoot, "missing-home");
        await fixture.ViewModel.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        fixture.NavigateTask(1); vm.LoadHardwareEditor(); AuthoringUiFixture.Drain();
        var text = fixture.Control<TextBlock>("Selected adapter guidance").Text!;
        Assert.Contains("prepare or import HardwareTest Basic", text);
        Assert.Contains("Supports Basic voltage recipes", text);
        var row = Assert.Single(vm.SelectedHardwareRows);
        Assert.Equal("HardwareTest Basic: needs preparation", row.PackageSummary);
        Assert.DoesNotContain(vm.OpenTapHomeOverride, row.PackageSummary);
        var details = fixture.Control<Expander>("Selected binding package details");
        Assert.False(details.IsExpanded);
        details.IsExpanded = true; AuthoringUiFixture.Drain();
        Assert.Contains(details.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == row.PackageStatus);
        Assert.Contains(vm.OpenTapHomeOverride, row.PackageStatus);
    }

    [AvaloniaFact]
    public async Task Binding_editor_explains_demo_and_unknown_types_and_links_to_package_recovery()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        await fixture.ViewModel.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
        tabs.SelectedIndex = 1;
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Load selected binding"));
        var help = fixture.Control<TextBlock>("Selected adapter guidance");
        Assert.Contains("Demo instrument", help.Text);
        Assert.Contains("HardwareTest Basic", help.Text);

        fixture.ViewModel.HardwareEditType = null;
        AuthoringUiFixture.Drain();
        Assert.Contains("Imported types are preserved", help.Text);
        Assert.Contains("Open Environment", help.Text);
        var environment = window.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, "Resolve types and packages in Environment"));
        environment.BringIntoView();
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(environment);
        Assert.Equal(3, tabs.SelectedIndex);
        var sourcing = fixture.Control<StackPanel>("Package sourcing guidance");
        var packageHelp = string.Join(" ", sourcing.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
        Assert.Contains(".TapPackage or .zip", packageHelp);
        Assert.Contains("match the required version", packageHelp);
        Assert.NotNull(fixture.Control<Button>("Open Instrument Components releases"));
        Assert.Contains("github.com/josh-hemphill/instrument-components", fixture.ViewModel.PackageRequirementsText);
        Assert.NotNull(fixture.Control<ItemsControl>("Environment package requirements"));
    }

    [AvaloniaFact]
    public async Task Deferring_an_address_explains_hardware_free_draft_without_mutating_program()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        await fixture.ViewModel.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1;
        AuthoringUiFixture.Drain();
        var original = fixture.ViewModel.SelectedProgram;
        AuthoringUiFixture.Click(fixture.Control<Button>("Open add instrument"));
        var guidance = fixture.Control<Expander>("Deferred instrument address guidance");
        guidance.IsExpanded = true;
        AuthoringUiFixture.Drain();
        var text = string.Join(" ", guidance.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
        Assert.Contains("Save all preserves your draft", text);
        Assert.Contains("does not create unresolved hardware slots", text);
        Assert.Same(original, fixture.ViewModel.SelectedProgram);
        var defer = fixture.Control<Button>("Defer instrument binding");
        defer.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(defer);
        Assert.Equal(0, window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        Assert.Same(original, fixture.ViewModel.SelectedProgram);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }
}
