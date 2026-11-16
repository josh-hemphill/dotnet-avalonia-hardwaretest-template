using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class BenchExportWorkflowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Saved_custom_program_prepares_a_checked_pair_through_keyboard_and_keeps_receipt_and_actions_reachable(
        bool dark, int width, int height, int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var app = Application.Current!;
        var originalTheme = app.RequestedThemeVariant;
        try
        {
            var owner = fixture.Show(width, height); owner.FontSize = font;
            Assert.Equal((double)width, owner.ClientSize.Width);
            Assert.True(owner.Bounds.Width <= width);
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            fixture.OpenRememberedWorkspace();
            var planId = PrepareSavedCustomProgram(fixture);
            var source = SnapshotSource(fixture, planId);
            fixture.NavigateTask(4);
            var trigger = fixture.Control<Button>("Prepare selected program for bench");
            trigger.BringIntoView(); AuthoringUiFixture.Drain();
            ResponsiveShellTests.Inside(trigger, owner);
            Assert.True(trigger.Focus());
            Space(owner);
            var dialog = Assert.IsType<BenchExportWindow>(Assert.Single(owner.OwnedWindows));
            var destination = fixture.Control<TextBox>("Bench files destination folder", dialog);
            var status = fixture.Control<TextBlock>("Bench file preparation status", dialog);
            var prepare = fixture.Control<Button>("Prepare bench copy", dialog);
            var close = fixture.Control<Button>("Close bench file preparation", dialog);
            Assert.True(destination.IsFocused);
            Enter(dialog);
            Assert.True(destination.IsFocused);
            Assert.Contains("destination", status.Text, StringComparison.OrdinalIgnoreCase);
            Capture(dialog, "bench-handoff-missing-destination", dark, width, height, font);
            ResponsiveShellTests.Inside(destination, dialog);
            ResponsiveShellTests.Inside(status, dialog);
            ResponsiveShellTests.Inside(prepare, dialog);
            ResponsiveShellTests.Inside(close, dialog);

            var transferRoot = Path.Combine(fixture.WorkspaceRoot, "bench-transfer");
            Type(dialog, destination, transferRoot);
            Enter(dialog);
            var receiptBox = fixture.Control<TextBox>("Prepared bench program receipt", dialog);
            await Until(() => prepare.IsEnabled && receiptBox.IsEffectivelyVisible);
            Assert.Contains("prepared", status.Text, StringComparison.OrdinalIgnoreCase);
            var targetPlan = Path.Combine(transferRoot, planId, planId + ".TapPlan");
            var receipt = Assert.IsType<BenchProgramReceipt>(BenchProgramHandoff.ValidateProgram(targetPlan));
            Assert.Equal(planId, receipt.PlanId);
            Assert.Equal(fixture.ViewModel.SelectedDocument!.Revision, receipt.SourceRevision);
            Assert.Equal(source[SourcePlan(fixture, planId)], File.ReadAllBytes(targetPlan));
            Assert.Equal(source[PlanCompiler.SidecarPath(SourcePlan(fixture, planId))],
                File.ReadAllBytes(PlanCompiler.SidecarPath(targetPlan)));
            var discovered = Assert.Single(ProgramCatalog.Enumerate([transferRoot]), entry => entry.Id == planId);
            Assert.Equal(Path.GetFullPath(targetPlan), Path.GetFullPath(discovered.Path));
            Assert.True(receiptBox.IsReadOnly);
            Assert.Equal(BenchProgramHandoff.Describe(receipt), receiptBox.Text);
            Assert.Contains(receipt.PlanSha256, receiptBox.Text, StringComparison.Ordinal);
            Assert.Contains(receipt.SidecarSha256, receiptBox.Text, StringComparison.Ordinal);

            // No programmatic receipt focus: it must be reachable through natural Tab navigation.
            for (var index = 0; !receiptBox.IsFocused && index < 10; index++)
            {
                dialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                AuthoringUiFixture.Drain();
            }
            Assert.True(receiptBox.IsFocused);
            dialog.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
            dialog.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.None, null);
            AuthoringUiFixture.Drain();
            Assert.Equal(receiptBox.Text, await dialog.Clipboard!.TryGetTextAsync());
            ResponsiveShellTests.Inside(receiptBox, dialog);
            ResponsiveShellTests.Inside(prepare, dialog);
            ResponsiveShellTests.Inside(close, dialog);
            Capture(dialog, "bench-handoff-prepared-receipt", dark, width, height, font);
            foreach (var pair in source) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            await Until(() => owner.OwnedWindows.Count == 0 && trigger.IsFocused);
            Assert.Equal(planId, fixture.ViewModel.SelectedProgram!.PlanId);
            Assert.False(fixture.ViewModel.HasUnsavedChanges);
        }
        finally { app.RequestedThemeVariant = originalTheme; }
    }

    [AvaloniaFact]
    public void Unsaved_revision_refuses_publication_and_retains_the_destination_and_saved_pair()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var owner = fixture.Show(900, 600); owner.FontSize = 20;
        Assert.Equal(900d, owner.ClientSize.Width);
        Assert.True(owner.Bounds.Width <= 900);
        fixture.OpenRememberedWorkspace();
        var planId = PrepareSavedCustomProgram(fixture);
        var source = SnapshotSource(fixture, planId);
        fixture.ViewModel.DisplayName = "A proposed title that has not been compiled";
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        fixture.NavigateTask(4);
        var trigger = fixture.Control<Button>("Prepare selected program for bench");
        AuthoringUiFixture.Click(trigger);
        var dialog = Assert.IsType<BenchExportWindow>(Assert.Single(owner.OwnedWindows));
        var destination = fixture.Control<TextBox>("Bench files destination folder", dialog);
        var transferRoot = Path.Combine(fixture.WorkspaceRoot, "must-not-publish-unsaved");
        Type(dialog, destination, transferRoot);
        Enter(dialog);
        var status = fixture.Control<TextBlock>("Bench file preparation status", dialog);
        Assert.Contains("Save and compile", status.Text, StringComparison.Ordinal);
        Assert.Equal(transferRoot, destination.Text);
        Assert.False(Directory.Exists(transferRoot));
        Assert.False(fixture.Control<TextBox>("Prepared bench program receipt", dialog).IsVisible);
        foreach (var pair in source) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        Capture(dialog, "bench-handoff-unsaved-block", false, 900, 600, 20);
        ResponsiveShellTests.Inside(fixture.Control<Button>("Prepare bench copy", dialog), dialog);
        ResponsiveShellTests.Inside(fixture.Control<Button>("Close bench file preparation", dialog), dialog);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.True(trigger.IsFocused);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public void Selection_change_while_dialog_is_open_cannot_publish_another_program_under_the_old_receipt()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var owner = fixture.Show(); fixture.OpenRememberedWorkspace();
        var planId = PrepareSavedCustomProgram(fixture);
        var source = SnapshotSource(fixture, planId);
        fixture.NavigateTask(4);
        var trigger = fixture.Control<Button>("Prepare selected program for bench");
        AuthoringUiFixture.Click(trigger);
        var dialog = Assert.IsType<BenchExportWindow>(Assert.Single(owner.OwnedWindows));
        var destination = fixture.Control<TextBox>("Bench files destination folder", dialog);
        var transferRoot = Path.Combine(fixture.WorkspaceRoot, "must-not-publish-stale-owner");
        Type(dialog, destination, transferRoot);
        // Simulate an external owner/session update while a modal interaction is pending.
        fixture.ViewModel.SelectProgram("sample");
        Enter(dialog);
        Assert.Equal("sample", fixture.ViewModel.SelectedProgram!.PlanId);
        Assert.Contains("selected revision changed", fixture.Control<TextBlock>("Bench file preparation status", dialog).Text, StringComparison.Ordinal);
        Assert.Contains("reopen", fixture.Control<TextBlock>("Bench file preparation status", dialog).Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(transferRoot));
        Assert.False(fixture.Control<TextBox>("Prepared bench program receipt", dialog).IsVisible);
        Assert.Equal(transferRoot, destination.Text);
        foreach (var pair in source) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.Empty(owner.OwnedWindows);
        Assert.True(trigger.IsFocused);
    }

    private static string PrepareSavedCustomProgram(AuthoringUiFixture fixture)
    {
        var id = "bench-ui-" + Guid.NewGuid().ToString("N");
        fixture.ViewModel.CreateDemoProgram(id);
        fixture.ViewModel.DisplayName = "Bench handoff acceptance";
        fixture.ViewModel.Apply();
        var saved = fixture.ViewModel.SaveAll();
        Assert.True(saved.Succeeded, string.Join(Environment.NewLine, saved.Failures.Select(failure => failure.Message)));
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        return id;
    }

    private static string SourcePlan(AuthoringUiFixture fixture, string id)
        => Assert.Single(fixture.ViewModel.Workspace!.TapPlanPaths, path => Path.GetFileNameWithoutExtension(path) == id);

    private static Dictionary<string, byte[]> SnapshotSource(AuthoringUiFixture fixture, string id)
    {
        var plan = SourcePlan(fixture, id);
        return new[] { plan, PlanCompiler.SidecarPath(plan), new AuthoringDocumentStore(fixture.WorkspaceRoot).GetDocumentPath(id) }
            .ToDictionary(path => path, File.ReadAllBytes);
    }

    private static void Type(Window dialog, TextBox box, string text)
    {
        box.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(box.Focus()); box.SelectAll(); dialog.KeyTextInput(text); AuthoringUiFixture.Drain();
        Assert.Equal(text, box.Text);
    }

    private static void Enter(Window window)
    {
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        AuthoringUiFixture.Drain();
    }

    private static void Space(Window window)
    {
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition()) { AuthoringUiFixture.Drain(); await Task.Delay(10, timeout.Token); }
        AuthoringUiFixture.Drain();
    }

    private static void Capture(Window window, string state, bool dark, int width, int height, int font)
    {
        if (Environment.GetEnvironmentVariable("HT_CAPTURE_UI") != "1") return;
        AuthoringUiFixture.Drain();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")
            ?? Path.Combine(Path.GetTempPath(), "hardwaretest-cross-app-ui");
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, $"{state}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"),
            Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
