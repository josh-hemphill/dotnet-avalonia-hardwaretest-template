using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Features.Instruments;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorCommissionProgramIdentityTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Same_name_programs_show_distinct_filter_identities_and_bind_only_the_deep_linked_program(
        bool dark, int width, int height, int font)
    {
        var app = E2EHarness.RequireApp();
        Assert.True(app.SettingsStore.AppSettings.UseMockVisa);
        using var shown = E2EHarness.ShowMainWindow();
        using var firstPlan = new OperatorReviewPlanFixture();
        using var secondPlan = new OperatorReviewPlanFixture();
        var sources = new[] { firstPlan.PlanPath, firstPlan.SidecarPath, secondPlan.PlanPath, secondPlan.SidecarPath }
            .ToDictionary(path => path, File.ReadAllBytes);
        var overrides = app.SettingsStore.AppSettings.PlanSlotOverrides
            .Select(item => (item.PlanId, item.SlotName, item.Resource)).ToArray();
        var main = shown.Main;
        var window = shown.Window;
        window.Width = width; window.Height = height; window.FontSize = font;
        main.IsNavPaneOpen = width >= 1280;
        main.NavigateToPageId("Instruments");
        var vm = Assert.IsType<InstrumentsViewModel>(main.CurrentPage);
        await vm.RefreshSlotsCommand.ExecuteAsync();
        var first = Assert.Single(vm.SlotOverrides, slot => slot.PlanId == firstPlan.Id && slot.SlotName == "DMM");
        var second = Assert.Single(vm.SlotOverrides, slot => slot.PlanId == secondPlan.Id && slot.SlotName == "DMM");
        var previousOverride = second.OverrideResource;
        var originalTheme = app.RequestedThemeVariant;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            Assert.Equal(first.PlanDisplayName, second.PlanDisplayName);
            Assert.False(vm.BindingsReadOnly);
            vm.FocusProgram(firstPlan.Id, "DMM");
            Assert.Same(first, vm.SelectedSlot);
            vm.FocusProgram(secondPlan.Id, "DMM");
            Assert.Same(second, vm.SelectedSlot);
            vm.FocusProgram(firstPlan.Id);
            Assert.Same(first, vm.SelectedSlot);
            vm.FocusProgram(secondPlan.Id);
            Assert.Same(second, vm.SelectedSlot);
            Assert.All(vm.VisibleSlots, slot => Assert.Equal(secondPlan.Id, slot.PlanId));
            Settle(window);
            var view = Assert.Single(window.GetVisualDescendants().OfType<InstrumentsView>(), control => control.IsEffectivelyVisible);
            var program = ByName<ComboBox>(view, "Commissioning program");
            Assert.Contains(secondPlan.Id, Assert.IsType<string>(program.SelectedItem), StringComparison.Ordinal);
            Assert.Contains(secondPlan.Id, AutomationProperties.GetHelpText(program), StringComparison.Ordinal);
            Assert.NotNull(AutomationProperties.GetLabeledBy(program));
            OperatorWorkflowUiTests.Inside(program, window);
            var firstLabel = Assert.Single(vm.PlanFilterOptions, label => label.Contains(firstPlan.Id, StringComparison.Ordinal));
            var secondLabel = Assert.Single(vm.PlanFilterOptions, label => label.Contains(secondPlan.Id, StringComparison.Ordinal));
            Assert.NotEqual(firstLabel, secondLabel);
            program.IsDropDownOpen = true;
            Settle(window);
            var selectedItem = Assert.IsAssignableFrom<Control>(program.ContainerFromIndex(program.SelectedIndex));
            Capture(window, "technician-duplicate-program-options", dark, width, height, font, TopLevel.GetTopLevel(selectedItem));
            program.IsDropDownOpen = false;

            await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
            Settle(window);
            var resources = ByName<ComboBox>(view, "Discovered VISA resource");
            resources.BringIntoView(); Settle(window);
            resources.SelectedItem = Assert.Single(vm.DiscoveredVisa, resource => resource.Resource == "MOCK::SCOPE1");
            Dispatcher.UIThread.RunJobs();
            var apply = ByName<Button>(view, "Apply selected resource");
            OperatorWorkflowUiTests.Inside(apply, window);
            OperatorWorkflowUiTests.Click(apply);
            Assert.Equal("MOCK::SCOPE1", second.OverrideResource);
            Assert.Empty(first.OverrideResource);
            Assert.Same(second, vm.SelectedSlot);
            Capture(window, "technician-duplicate-program-applied", dark, width, height, font);
            Assert.Equal(overrides, app.SettingsStore.AppSettings.PlanSlotOverrides
                .Select(item => (item.PlanId, item.SlotName, item.Resource)).ToArray());
            foreach (var (path, bytes) in sources) Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.False(main.RunTest.IsRunning);
        }
        finally
        {
            second.OverrideResource = previousOverride;
            app.RequestedThemeVariant = originalTheme;
        }
    }

    private static T ByName<T>(Control root, string name) where T : Control
        => Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.IsEffectivelyVisible
            && AutomationProperties.GetName(control) == name);

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()) Assert.NotNull(frame);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string state, bool dark, int width, int height, int font, TopLevel? popup = null)
    {
        Settle(window);
        if (!OperatorGraphicalReviewCapture.CaptureEnabled) return;
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")!;
        Directory.CreateDirectory(directory);
        var suffix = $"{(dark ? "dark" : "light")}-{width}x{height}-font{font}";
        using (var bitmap = window.CaptureRenderedFrame())
        {
            Assert.NotNull(bitmap);
            bitmap.Save(Path.Combine(directory, $"{state}-{suffix}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        if (popup is not null && !ReferenceEquals(popup, window))
        {
            using var bitmap = popup.CaptureRenderedFrame();
            Assert.NotNull(bitmap);
            bitmap.Save(Path.Combine(directory, $"{state}-popup-{suffix}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
