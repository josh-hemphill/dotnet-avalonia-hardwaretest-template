using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Features.Instruments;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorInstrumentsLayoutTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Compact_commissioning_keeps_fields_separate_and_apply_save_visible_without_rewriting_the_plan(int font)
    {
        Assert.True(E2EHarness.RequireApp().SettingsStore.AppSettings.UseMockVisa);
        using var shown = E2EHarness.ShowMainWindow();
        using var fixture = new OperatorReviewPlanFixture();
        var window = shown.Window; window.Width = 900; window.Height = 600; window.FontSize = font;
        var main = shown.Main; main.IsNavPaneOpen = false;
        main.NavigateToPageId("Instruments");
        var vm = Assert.IsType<InstrumentsViewModel>(main.CurrentPage);
        await vm.RefreshSlotsCommand.ExecuteAsync();
        vm.FocusProgram(fixture.Id);
        var fixtureSlot = Assert.Single(vm.SlotOverrides, slot =>
            string.Equals(slot.PlanId, fixture.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slot.SlotName, "DMM", StringComparison.Ordinal));
        Assert.Contains(fixtureSlot, vm.VisibleSlots);
        vm.SelectedSlot = fixtureSlot;
        await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
        await E2EHarness.PumpUiAsync();
        var view = Assert.Single(window.GetVisualDescendants().OfType<InstrumentsView>(), control => control.IsEffectivelyVisible);
        var viewport = ByName<ScrollViewer>(view, "Instrument setup viewport");
        var program = ByName<ComboBox>(view, "Commissioning program");
        var readiness = ByName<TextBlock>(view, "Instrument readiness");
        var apply = ByName<Button>(view, "Apply selected resource");
        var save = ByName<Button>(view, "Save instrument overrides");
        OperatorWorkflowUiTests.Inside(program, window);
        OperatorWorkflowUiTests.Inside(readiness, window);
        var programBounds = new Rect(program.TranslatePoint(default, window)!.Value, program.Bounds.Size);
        var readinessBounds = new Rect(readiness.TranslatePoint(default, window)!.Value, readiness.Bounds.Size);
        Assert.False(programBounds.Intersects(readinessBounds), "Program and readiness must occupy separate columns.");
        Assert.DoesNotContain(apply, viewport.GetVisualDescendants());
        Assert.DoesNotContain(save, viewport.GetVisualDescendants());
        OperatorWorkflowUiTests.Inside(apply, window);
        OperatorWorkflowUiTests.Inside(save, window);
        var sourcePlan = File.ReadAllBytes(fixture.PlanPath);
        var sourceSidecar = File.ReadAllBytes(fixture.SidecarPath);
        var slot = vm.SelectedSlot!;
        var originalOverride = slot.OverrideResource;
        try
        {
            var resources = ByName<ComboBox>(view, "Discovered VISA resource");
            resources.BringIntoView(); Dispatcher.UIThread.RunJobs();
            OperatorWorkflowUiTests.Inside(resources, window);
            var choice = vm.DiscoveredVisa.First(resource => resource.Resource.StartsWith("MOCK::", StringComparison.OrdinalIgnoreCase));
            resources.SelectedItem = choice;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(choice, vm.SelectedVisa);
            Assert.Equal(originalOverride, slot.OverrideResource);
            Assert.NotNull(AutomationProperties.GetLabeledBy(resources));
            OperatorWorkflowUiTests.Inside(apply, window);
            OperatorWorkflowUiTests.Click(apply);
            Assert.Equal(choice.Resource, slot.OverrideResource);
            Assert.Same(slot, vm.SelectedSlot);
            var target = ByName<TextBlock>(view, "Selected binding target");
            Assert.Equal(slot.PlanDisplayName, target.Text);
            OperatorWorkflowUiTests.Inside(target, window);
            var query = ByName<Button>(view, "Query selected instrument identity");
            query.BringIntoView(); Dispatcher.UIThread.RunJobs();
            OperatorWorkflowUiTests.Inside(query, window);
            OperatorWorkflowUiTests.Inside(apply, window);
            OperatorWorkflowUiTests.Inside(save, window);
            Assert.Equal(sourcePlan, File.ReadAllBytes(fixture.PlanPath));
            Assert.Equal(sourceSidecar, File.ReadAllBytes(fixture.SidecarPath));
            Assert.False(main.RunTest.IsRunning);
        }
        finally { slot.OverrideResource = originalOverride; }
    }

    private static T ByName<T>(Control root, string name) where T : Control
        => Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetName(control) == name);
}
