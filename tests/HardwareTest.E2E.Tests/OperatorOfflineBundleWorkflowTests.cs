using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Features.Settings;
using HardwareTest.OpenTap.Host;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorOfflineBundleWorkflowTests
{
    [AvaloniaFact]
    public async Task Closing_a_replaced_owners_inspection_does_not_move_focus_into_the_new_owners_form()
    {
        var app = E2EHarness.RequireApp(); using var shown = E2EHarness.ShowMainWindow();
        shown.Main.NavigateToPageId("Settings"); await E2EHarness.PumpUiAsync();
        var window = shown.Window;
        var previous = Assert.IsType<SettingsViewModel>(shown.Main.CurrentPage);
        var folder = Path.Combine(Path.GetTempPath(), "offline-owner-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var view = Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
        SettingsViewModel? replacement = null;
        try
        {
            previous.OfflineBundleInspector = (_, _) => Task.FromResult(new OfflineBundleInspection("Owned UI fixture", "Injected inspection details."));
            previous.OfflineBundlePath = folder; await previous.CheckOfflineBundleCommand.ExecuteAsync();
            Named<Expander>(window, "Offline bench bundle").IsExpanded = true; Settle(window);
            Enter(window, Named<Button>(window, "View full offline bundle inspection"));
            await E2EHarness.WaitUntilAsync(() => window.OwnedWindows.Any(child => child.Title == "Offline bench bundle inspection"));
            replacement = new SettingsViewModel(app.SettingsStore, app.Services.GetRequiredService<IOpenTapHostCatalog>());
            var check = Named<Button>(window, "Check offline bundle");
            void ReplacementFocused(object? sender, EventArgs args) { check.Focus(); }
            view.DataContextChanged += ReplacementFocused;
            view.DataContext = replacement;
            view.DataContextChanged -= ReplacementFocused;
            await E2EHarness.PumpUiAsync(); Settle(window);
            Assert.Empty(window.OwnedWindows);
            Assert.True(check.IsFocused);
            Assert.False(Named<TextBox>(window, "Offline bundle folder").IsFocused);
        }
        finally
        {
            replacement?.StopOfflineBundleInspection(); view.DataContext = previous;
            previous.StopOfflineBundleInspection(); Directory.Delete(folder, true);
        }
    }

    // This matrix exercises actual rendered UI and keyboard/clipboard workflows with an owned
    // inspection-result seam. Host tests and the separate process proof validate real bundles.
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Read_only_bundle_inspection_recovers_validates_handoffs_and_revokes_stale_results(bool dark, int width, int height, int font)
    {
        var app = E2EHarness.RequireApp(); var oldTheme = app.RequestedThemeVariant;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        using var shown = E2EHarness.ShowMainWindow();
        var window = shown.Window; window.Width = width; window.Height = height; window.FontSize = font;
        var main = shown.Main; main.IsNavPaneOpen = false;
        var typography = new List<(string Name, double Box, double Presenter)>();
        var root = Path.Combine(Path.GetTempPath(), "offline-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        SettingsViewModel? vm = null;
        try
        {
            var selected = main.RunTest.ProgramSelection.SelectedProgram;
            main.NavigateToPageId("Settings"); await E2EHarness.PumpUiAsync();
            vm = Assert.IsType<SettingsViewModel>(main.CurrentPage);
            vm.OfflineBundleInspector = (_, _) => Task.FromResult(new OfflineBundleInspection(
                "Program UI workflow fixture · Linux/x64\n2 pinned packages: Fixture instrument 2.0.0; Basic 1.0.0",
                "UI workflow fixture (injected inspection result)\nTarget: Linux/x64\nPinned: Fixture instrument 2.0.0; Basic 1.0.0\nProgram checks: complete\nInstallation has not been performed."));
            var section = Named<Expander>(window, "Offline bench bundle");
            Assert.False(section.IsExpanded); section.IsExpanded = true; Settle(window);
            var check = Named<Button>(window, "Check offline bundle");
            Enter(window, check); await E2EHarness.PumpUiAsync(); Settle(window);
            var path = Named<TextBox>(window, "Offline bundle folder");
            RecordOfflineTextFont(typography, path);
            Assert.True(path.IsFocused); Assert.Contains("absolute", vm.OfflineBundleStatus);
            OperatorWorkflowUiTests.Inside(path, window);
            OperatorWorkflowUiTests.Inside(Named<TextBlock>(window, "Offline bundle inspection feedback"), window);
            Capture(window, "offline-bundle-path-required", dark, width, height, font);
            Type(window, path, root); Enter(window, check);
            await E2EHarness.WaitUntilAsync(() => vm.HasOfflineBundleInspection && !vm.OfflineBundleIsChecking);
            Assert.Contains("2 pinned packages", vm.OfflineBundleSummary);
            Assert.Contains("Nothing has been installed", vm.OfflineBundleStatus);
            var viewDetails = Named<Button>(window, "View full offline bundle inspection");
            Enter(window, viewDetails);
            await E2EHarness.WaitUntilAsync(() => window.OwnedWindows.Any(child => child.Title == "Offline bench bundle inspection"));
            var details = window.OwnedWindows.Single(child => child.Title == "Offline bench bundle inspection"); Settle(details);
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, details.ActualThemeVariant);
            var full = Named<TextBox>(details, "Full offline bundle inspection");
            RecordOfflineTextFont(typography, full);
            Assert.True(full.IsReadOnly); Assert.True(full.IsFocused);
            Assert.Contains("UI workflow fixture (injected inspection result)", full.Text);
            details.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            details.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            details.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            details.KeyRelease(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            await E2EHarness.PumpUiAsync();
            Assert.NotNull(details.Clipboard);
            Assert.Equal(vm.OfflineBundleDetails, await details.Clipboard!.TryGetTextAsync());
            OperatorWorkflowUiTests.Inside(Named<Button>(details, "Close offline bundle inspection"), details);
            Capture(details, "offline-bundle-full-inspection", dark, width, height, font);
            details.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await E2EHarness.PumpUiAsync(); Assert.False(details.IsVisible); Assert.True(viewDetails.IsFocused);

            var instructions = Named<Expander>(window, "Offline bundle installation instructions");
            instructions.IsExpanded = true; Settle(window);
            var copy = Named<Button>(window, "Copy offline bundle installation command");
            Enter(window, copy); await E2EHarness.PumpUiAsync(); Settle(window);
            var destination = Named<TextBox>(window, "New operator copy folder");
            RecordOfflineTextFont(typography, destination);
            Assert.True(destination.IsFocused); Assert.Contains("absolute", vm.OfflineBundleCommandStatus);
            OperatorWorkflowUiTests.Inside(destination, window);
            OperatorWorkflowUiTests.Inside(Named<TextBlock>(window, "Offline bundle command feedback"), window);
            Capture(window, "offline-bundle-new-folders-required", dark, width, height, font);
            Type(window, destination, root + " new app's copy");
            var data = Named<TextBox>(window, "Fresh operator data folder");
            RecordOfflineTextFont(typography, data);
            Type(window, data, root); Enter(window, copy);
            await E2EHarness.PumpUiAsync(); Settle(window);
            Assert.True(data.IsFocused); Assert.Contains("already exists", vm.OfflineBundleCommandStatus);
            Type(window, data, root + " fresh data's folder"); Enter(window, copy);
            await E2EHarness.WaitUntilAsync(() => vm.OfflineBundleCommandStatus.StartsWith("Copied installation command", StringComparison.Ordinal));
            var command = await window.Clipboard!.TryGetTextAsync();
            Assert.Contains("--install-offline-bundle", command); Assert.Contains("--destination", command); Assert.Contains("--data-directory", command);
            Assert.False(Directory.Exists(vm.OfflineBundleDestination)); Assert.False(Directory.Exists(vm.OfflineBundleDataDirectory));
            Named<TextBlock>(window, "Offline bundle command feedback").BringIntoView(); Settle(window);
            OperatorWorkflowUiTests.Inside(Named<TextBlock>(window, "Offline bundle command feedback"), window);
            Capture(window, "offline-bundle-command-copied", dark, width, height, font);
            Type(window, path, root + " changed");
            Assert.False(vm.HasOfflineBundleInspection); Assert.Empty(vm.OfflineBundleInstallationCommand);
            Assert.Contains("Check this folder", vm.OfflineBundleStatus);
            Assert.Same(selected, main.RunTest.ProgramSelection.SelectedProgram); Assert.False(main.RunTest.IsRunning);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Copy offline bundle installation command");
            Assert.True(typography.All(value => Math.Abs(value.Box - font) < 0.01 && Math.Abs(value.Presenter - font) < 0.01),
                $"Window.FontSize={window.FontSize:G17}px; expected all offline text inputs and realized presenters to equal {font}px. "
                + string.Join("; ", typography.Select(value => $"{value.Name}: TextBox={value.Box:G17}px, PART_TextPresenter={value.Presenter:G17}px")));
        }
        finally
        {
            foreach (var child in window.OwnedWindows.ToArray()) child.Close();
            vm?.StopOfflineBundleInspection(); Directory.Delete(root, true); app.RequestedThemeVariant = oldTheme;
        }
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Real_watcher_revocation_recovers_the_owned_copy_flow_without_stealing_unrelated_settings_focus(bool dark, bool ownsFlow)
    {
        var app = E2EHarness.RequireApp(); var oldTheme = app.RequestedThemeVariant;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        using var shown = E2EHarness.ShowMainWindow(); var window = shown.Window;
        window.Width = 900; window.Height = 600; window.FontSize = 20; shown.Main.IsNavPaneOpen = false;
        var root = Path.Combine(Path.GetTempPath(), "offline-focus-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        SettingsViewModel? vm = null;
        try
        {
            shown.Main.NavigateToPageId("Settings"); await E2EHarness.PumpUiAsync();
            vm = Assert.IsType<SettingsViewModel>(shown.Main.CurrentPage);
            // This is a UI inspection-result seam; the real filesystem watcher and focus route remain active.
            vm.OfflineBundleInspector = (_, _) => Task.FromResult(new OfflineBundleInspection("Owned focus fixture", "UI focus fixture; no installation performed."));
            Named<Expander>(window, "Offline bench bundle").IsExpanded = true;
            vm.OfflineBundlePath = root; await vm.CheckOfflineBundleCommand.ExecuteAsync(); Settle(window);
            Assert.True(vm.HasOfflineBundleInspection);
            Named<Expander>(window, "Offline bundle installation instructions").IsExpanded = true; Settle(window);
            Type(window, Named<TextBox>(window, "New operator copy folder"), root + " new app");
            Type(window, Named<TextBox>(window, "Fresh operator data folder"), root + " fresh data");
            Enter(window, Named<Button>(window, "Copy offline bundle installation command"));
            await E2EHarness.WaitUntilAsync(() => vm.OfflineBundleCommandStatus.StartsWith("Copied installation command", StringComparison.Ordinal));
            Settle(window);
            ComboBox? other = null;
            if (ownsFlow)
            {
                // Native async command completion can leave no focused control. The last user flow still owns recovery.
                window.FocusManager!.Focus(null); Settle(window);
                Assert.Null(window.FocusManager.GetFocusedElement());
            }
            else
            {
                other = Named<ComboBox>(window, "Theme"); other.BringIntoView(); Settle(window); Assert.True(other.Focus());
            }
            File.WriteAllText(Path.Combine(root, "source-mutated.txt"), "real watcher invalidation");
            await E2EHarness.WaitUntilAsync(() => !vm.HasOfflineBundleInspection && vm.OfflineBundleStatus.Contains("Bundle files changed", StringComparison.Ordinal));
            await E2EHarness.PumpUiAsync(); Settle(window);
            Assert.Empty(vm.OfflineBundleInstallationCommand);
            var path = Named<TextBox>(window, "Offline bundle folder");
            if (ownsFlow)
            {
                Assert.True(path.IsFocused);
                OperatorWorkflowUiTests.Inside(path, window);
                OperatorWorkflowUiTests.Inside(Named<TextBlock>(window, "Offline bundle inspection feedback"), window);
            }
            else
            {
                Assert.True(other!.IsFocused); Assert.False(path.IsFocused);
                OperatorWorkflowUiTests.Inside(other, window);
            }
            Capture(window, ownsFlow ? "offline-bundle-source-change-recovery" : "offline-bundle-source-change-unrelated-focus", dark, 900, 600, 20);
        }
        finally { vm?.StopOfflineBundleInspection(); Directory.Delete(root, true); app.RequestedThemeVariant = oldTheme; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Watcher_recovery_yields_to_outer_navigation_focus_moved_after_revocation_before_layout(bool dark)
    {
        var app = E2EHarness.RequireApp(); var oldTheme = app.RequestedThemeVariant;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        using var shown = E2EHarness.ShowMainWindow(); var window = shown.Window;
        window.Width = 900; window.Height = 600; window.FontSize = 20; shown.Main.IsNavPaneOpen = false;
        var root = Path.Combine(Path.GetTempPath(), "offline-outer-focus-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        SettingsViewModel? vm = null;
        System.ComponentModel.PropertyChangedEventHandler? invalidated = null;
        try
        {
            shown.Main.NavigateToPageId("Settings"); await E2EHarness.PumpUiAsync();
            vm = Assert.IsType<SettingsViewModel>(shown.Main.CurrentPage);
            vm.OfflineBundleInspector = (_, _) => Task.FromResult(new OfflineBundleInspection("Outer focus UI fixture", "Injected UI result; real watcher remains active."));
            Named<Expander>(window, "Offline bench bundle").IsExpanded = true;
            vm.OfflineBundlePath = root; await vm.CheckOfflineBundleCommand.ExecuteAsync(); Settle(window);
            Named<Expander>(window, "Offline bundle installation instructions").IsExpanded = true; Settle(window);
            Type(window, Named<TextBox>(window, "New operator copy folder"), root + " new app");
            Type(window, Named<TextBox>(window, "Fresh operator data folder"), root + " fresh data");
            var copy = Named<Button>(window, "Copy offline bundle installation command"); Enter(window, copy);
            await E2EHarness.WaitUntilAsync(() => copy.IsEnabled && vm.OfflineBundleCommandStatus.StartsWith("Copied installation command", StringComparison.Ordinal));
            Settle(window); window.FocusManager!.Focus(null); Assert.True(copy.Focus());
            var outer = Named<Button>(window, "Toggle navigation");
            Assert.DoesNotContain(outer.GetVisualAncestors(), ancestor => ancestor is SettingsView);
            var scheduled = false; var moveRan = false; var moved = false;
            invalidated = (_, args) =>
            {
                if (scheduled || args.PropertyName != nameof(SettingsViewModel.HasOfflineBundleInspection) || vm.HasOfflineBundleInspection) return;
                scheduled = true;
                // The pre-hide event already queued Loaded recovery. A new shell focus request
                // runs first at Normal and never bubbles through Settings' local activity tracker.
                Dispatcher.UIThread.Post(() => { moved = outer.Focus(); moveRan = true; }, DispatcherPriority.Normal);
            };
            vm.PropertyChanged += invalidated;
            File.WriteAllText(Path.Combine(root, "source-mutated.txt"), "real watcher race");
            await E2EHarness.WaitUntilAsync(() => !vm.HasOfflineBundleInspection && moveRan);
            await E2EHarness.PumpUiAsync(); Settle(window);
            Assert.True(scheduled); Assert.True(moved); Assert.True(outer.IsFocused);
            Assert.False(Named<TextBox>(window, "Offline bundle folder").IsFocused);
            Assert.Empty(vm.OfflineBundleInstallationCommand);
            Assert.Contains("Bundle files changed", vm.OfflineBundleStatus, StringComparison.Ordinal);
        }
        finally
        {
            if (vm is not null && invalidated is not null) vm.PropertyChanged -= invalidated;
            vm?.StopOfflineBundleInspection(); Directory.Delete(root, true); app.RequestedThemeVariant = oldTheme;
        }
    }

    private static void RecordOfflineTextFont(List<(string Name, double Box, double Presenter)> typography, TextBox textBox)
    {
        var presenters = textBox.GetVisualDescendants().OfType<TextPresenter>().Where(presenter => presenter.Name == "PART_TextPresenter").ToArray();
        Assert.True(presenters.Length == 1,
            $"{AutomationProperties.GetName(textBox)} must have one realized PART_TextPresenter; found {presenters.Length}.");
        typography.Add((AutomationProperties.GetName(textBox) ?? textBox.Name ?? "TextBox", textBox.FontSize, presenters[0].FontSize));
    }

    private static T Named<T>(Control root, string name) where T : Control => Assert.Single(root.GetVisualDescendants().OfType<T>(),
        control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);
    private static void Enter(Window window, Button button)
    {
        button.BringIntoView(); Settle(window); Assert.True(button.Focus());
        OperatorWorkflowUiTests.Inside(button, window); Assert.True(button.Bounds.Height >= 40);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Settle(window);
    }
    private static void Type(Window window, TextBox input, string value)
    { input.BringIntoView(); Settle(window); Assert.True(input.Focus()); input.SelectAll(); window.KeyTextInput(value); Settle(window); Assert.Equal(value, input.Text); }
    private static void Settle(Window window)
    { Dispatcher.UIThread.RunJobs(); using (var frame = window.CaptureRenderedFrame()) Assert.NotNull(frame); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string state, bool dark, int width, int height, int font)
    {
        if (!OperatorGraphicalReviewCapture.CaptureEnabled) return;
        Settle(window); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY") ?? Path.Combine(AppContext.BaseDirectory, "operator-persona-review");
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, $"{state}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
