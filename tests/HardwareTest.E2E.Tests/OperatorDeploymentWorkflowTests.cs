using System.Collections;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Instruments;
using HardwareTest.OpenTap.Host;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorDeploymentWorkflowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Deployment_owned_bindings_are_visible_read_only_and_queryable_in_the_production_view(
        bool dark, int width, int height, int font)
    {
        var app = E2EHarness.RequireApp();
        Assert.True(app.SettingsStore.AppSettings.UseMockVisa);
        using var shown = E2EHarness.ShowMainWindow();
        using var plan = new OperatorReviewPlanFixture();
        var originalPlan = File.ReadAllBytes(plan.PlanPath);
        var originalSidecar = File.ReadAllBytes(plan.SidecarPath);
        var originalStore = app.SettingsStore;
        var originalOverrides = originalStore.AppSettings.PlanSlotOverrides
            .Select(item => (item.PlanId, item.SlotName, item.Resource)).ToArray();
        var root = Path.Combine(Path.GetTempPath(), "operator-deployment-" + Guid.NewGuid().ToString("N"));
        var isolatedStore = new SettingsStore(root);
        await isolatedStore.LoadAsync(new Dictionary<string, string>(), new Dictionary<string, string>());
        isolatedStore.AppSettings.UseMockVisa = true;
        await isolatedStore.SaveAppSettingsAsync();
        var savedSettings = File.ReadAllBytes(isolatedStore.SettingsPath);
        var overlay = AppSettingsEnvironmentBinder.ReadEnvironment(new Hashtable
        {
            ["HARDWARETEST_PLAN_SLOT_OVERRIDES__0__PLAN_ID"] = plan.Id,
            ["HARDWARETEST_PLAN_SLOT_OVERRIDES__0__SLOT_NAME"] = "DMM",
            ["HARDWARETEST_PLAN_SLOT_OVERRIDES__0__RESOURCE"] = "MOCK::INSTR0",
        });
        await isolatedStore.LoadAsync(overlay, new Dictionary<string, string>());
        Assert.True(isolatedStore.AppSettings.UseMockVisa);
        var identities = new FileStationIdnStore(root);
        var bench = new BenchOperationCoordinator();
        var vm = new InstrumentsViewModel(isolatedStore, new MockVisaResourceDiscovery(),
            app.Services.GetRequiredService<IOpenTapHostCatalog>(), new MockVisaSessionFactory(new VisaSessionGate()),
            operatorSession: new OperatorSession(), bench: bench, idnStore: identities);
        var main = shown.Main;
        var window = shown.Window;
        window.Width = width; window.Height = height; window.FontSize = font;
        main.IsNavPaneOpen = width >= 1280;
        main.NavigateToPageId("Instruments");
        var originalPage = main.CurrentPage;
        var originalTheme = app.RequestedThemeVariant;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            // Production shell, InstrumentsView and SettingsStore; only this page VM
            // receives isolated indexed deployment configuration and mock-only I/O.
            // The application's DI settings store and active station session stay intact.
            main.CurrentPage = vm;
            await vm.RefreshSlotsCommand.ExecuteAsync();
            vm.FocusProgram(plan.Id, "DMM");
            var target = Assert.Single(vm.SlotOverrides, slot => slot.PlanId == plan.Id && slot.SlotName == "DMM");
            vm.SelectedSlot = target;
            await vm.RefreshVisaDiscoverCommand.ExecuteAsync();
            Settle(window);
            var view = Assert.Single(window.GetVisualDescendants().OfType<InstrumentsView>(), control => control.IsEffectivelyVisible);
            var ownership = ByName<TextBlock>(view, "Station binding ownership");
            OperatorWorkflowUiTests.Inside(ownership, window);
            Assert.Contains("Deployment controls", ownership.Text!, StringComparison.Ordinal);
            Assert.True(vm.BindingsReadOnly);
            Assert.False(vm.CanEditBindings);
            var apply = ByName<Button>(view, "Apply selected resource");
            var save = ByName<Button>(view, "Save instrument overrides");
            foreach (var button in new[] { apply, save })
            {
                Assert.False(button.IsEffectivelyEnabled);
                OperatorWorkflowUiTests.Inside(button, window);
            }
            Capture(window, "technician-deployment-bindings", dark, width, height, font);

            var details = ByName<Expander>(view, "Selected instrument slot details");
            details.IsExpanded = true;
            Settle(window);
            var manual = ByName<TextBox>(view, "Station override address");
            manual.BringIntoView(); Settle(window);
            Assert.True(manual.IsReadOnly);
            Assert.True(manual.Focus()); manual.SelectAll();
            window.KeyTextInput("FORBIDDEN-STATION-EDIT");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("MOCK::INSTR0", manual.Text);
            Assert.Equal("MOCK::INSTR0", target.OverrideResource);
            Assert.False(ByName<Button>(view, "Clear selected instrument override").IsEffectivelyEnabled);
            OperatorWorkflowUiTests.Inside(manual, window);
            Capture(window, "technician-deployment-manual-address", dark, width, height, font);

            details.IsExpanded = false;
            vm.SelectedVisa = null;
            vm.SelectedOpenTap = null;
            Settle(window);
            var query = ByName<Button>(view, "Query selected instrument identity");
            query.BringIntoView(); Settle(window);
            Assert.True(query.IsEffectivelyEnabled);
            Assert.Null(bench.Current);
            OperatorWorkflowUiTests.Inside(query, window);
            OperatorWorkflowUiTests.Click(query);
            await E2EHarness.WaitUntilAsync(() => !vm.IsBusy && target.HasLastIdn && bench.Current is null);
            Assert.Equal(plan.Id, Assert.Single(identities.List()).PlanId);
            Assert.Equal("MOCK::INSTR0", identities.List()[0].Resource);
            Assert.Null(bench.Current);
            Assert.Contains("Recorded for", vm.Status, StringComparison.Ordinal);
            Capture(window, "technician-deployment-identity", dark, width, height, font);

            // A disabled Save cannot be bypassed by invoking its command directly.
            await vm.SaveCommand.ExecuteAsync();
            Assert.Equal(vm.BindingOwnershipHint, vm.Status);
            Assert.Equal(savedSettings, File.ReadAllBytes(isolatedStore.SettingsPath));
            Assert.Equal("MOCK::INSTR0", Assert.Single(isolatedStore.AppSettings.PlanSlotOverrides).Resource);
            Assert.Equal(originalPlan, File.ReadAllBytes(plan.PlanPath));
            Assert.Equal(originalSidecar, File.ReadAllBytes(plan.SidecarPath));
            Assert.Same(originalStore, app.SettingsStore);
            Assert.Equal(originalOverrides, originalStore.AppSettings.PlanSlotOverrides
                .Select(item => (item.PlanId, item.SlotName, item.Resource)).ToArray());
            Assert.False(main.RunTest.IsRunning);
        }
        finally
        {
            main.CurrentPage = originalPage;
            app.RequestedThemeVariant = originalTheme;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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

    private static void Capture(Window window, string state, bool dark, int width, int height, int font)
    {
        Settle(window);
        if (!OperatorGraphicalReviewCapture.CaptureEnabled) return;
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")!;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(directory, $"{state}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"),
            Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
