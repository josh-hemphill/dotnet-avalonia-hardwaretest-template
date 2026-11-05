using System.IO.Compression;
using System.Text;
using System.Text.Json;
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

public sealed class OfflineBenchExportWorkflowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_preparation_cancellation_or_context_change_recovers_local_controls_and_preserves_inputs(bool changeContext)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var owner = fixture.Show(900, 600); owner.FontSize = 20;
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.CreateDemoProgram("pending-offline");
        Assert.True(fixture.ViewModel.SaveAll().Succeeded);
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var dialog = new BenchExportWindow(fixture.ViewModel, () => true) { Width = 860, Height = 560, FontSize = 20 };
        dialog.OfflineExporter = (options, target, current, token) =>
        {
            started.Set(); release.Wait(token);
            token.ThrowIfCancellationRequested();
            Assert.False(current());
            throw new AuthoringWorkspaceException("The selected revision changed before publication.");
        };
        var shown = dialog.ShowDialog(owner);
        var include = fixture.Control<CheckBox>("Include packages for an offline bench", dialog);
        Assert.True(include.Focus()); Space(dialog);
        var target = Path.Combine(fixture.WorkspaceRoot, "retained-transfer-input");
        Type(dialog, fixture.Control<TextBox>("Bench files destination folder", dialog), target);
        var prepare = fixture.Control<Button>("Prepare offline bundle", dialog);
        AuthoringUiFixture.Click(prepare); await Until(() => started.IsSet);
        var cancel = fixture.Control<Button>("Cancel bench file preparation", dialog);
        var close = fixture.Control<Button>("Close bench file preparation", dialog);
        Assert.False(prepare.IsEnabled); Assert.True(cancel.IsVisible);
        ResponsiveShellTests.Inside(cancel, dialog); ResponsiveShellTests.Inside(close, dialog);
        if (changeContext) { fixture.ViewModel.CreateDemoProgram("next-context"); release.Set(); }
        else { Assert.True(cancel.Focus()); Space(dialog); }
        var status = fixture.Control<TextBlock>("Bench file preparation status", dialog);
        await Until(() => prepare.IsEnabled && !cancel.IsVisible);
        Assert.Contains(changeContext ? "revision changed" : "cancelled", status.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(include.IsChecked);
        Assert.Equal(target, fixture.Control<TextBox>("Bench files destination folder", dialog).Text);
        Assert.False(Directory.Exists(target));
        ResponsiveShellTests.Inside(status, dialog); ResponsiveShellTests.Inside(prepare, dialog);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); await shown;
        Assert.False(dialog.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Missing_package_preserves_offline_inputs_and_manual_archive_retry_prepares_the_complete_bundle(
        bool dark, int width, int height, int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var app = Application.Current!;
        var priorTheme = app.RequestedThemeVariant;
        try
        {
            var files = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
            new OpenTapHomeBootstrapper().Bootstrap(files, new() { Offline = true });
            files.Manifest.Dependencies.Add(new() { Package = "Offline GUI manual package", Version = "^1.0.0" });
            File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "authoring.json"),
                JsonSerializer.Serialize(files.Manifest, AuthoringJsonContext.Default.AuthoringManifest));
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var owner = fixture.Show(width, height); owner.FontSize = font;
            fixture.OpenRememberedWorkspace();
            var vm = fixture.ViewModel;
            var id = "offline-ui-" + Guid.NewGuid().ToString("N");
            vm.CreateDemoProgram(id); vm.DisplayName = "Offline delivery review"; vm.Apply();
            var save = vm.SaveAll(); Assert.True(save.Succeeded);
            await fixture.WaitForReadinessAsync();
            fixture.NavigateTask(4);
            var trigger = fixture.Control<Button>("Prepare selected program for bench");
            trigger.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(trigger);
            var dialog = Assert.IsType<BenchExportWindow>(Assert.Single(owner.OwnedWindows));
            Assert.Equal(app.RequestedThemeVariant, dialog.ActualThemeVariant);
            var include = fixture.Control<CheckBox>("Include packages for an offline bench", dialog);
            include.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(include.Focus()); Space(dialog);
            Assert.True(include.IsChecked);
            var platform = fixture.Control<ComboBox>("Offline bench platform", dialog);
            Assert.Equal(OfflineBenchBundle.CurrentOS == "Windows" ? "Windows x64" : "Linux x64", platform.SelectedItem);
            var source = Path.Combine(fixture.WorkspaceRoot, "manual-packages"); Directory.CreateDirectory(source);
            var target = Path.Combine(fixture.WorkspaceRoot, "transfer");
            Type(dialog, fixture.Control<TextBox>("Offline package source folder", dialog), source);
            Type(dialog, fixture.Control<TextBox>("Bench files destination folder", dialog), target);
            var prepare = fixture.Control<Button>("Prepare offline bundle", dialog);
            var close = fixture.Control<Button>("Close bench file preparation", dialog);
            var status = fixture.Control<TextBlock>("Bench file preparation status", dialog);
            ResponsiveShellTests.Inside(prepare, dialog); ResponsiveShellTests.Inside(close, dialog);
            AuthoringUiFixture.Click(prepare);
            await Until(() => prepare.IsEnabled);
            Assert.Contains("Offline GUI manual package", status.Text, StringComparison.Ordinal);
            Assert.True(include.IsChecked); Assert.Equal(source, fixture.Control<TextBox>("Offline package source folder", dialog).Text);
            Assert.Equal(target, fixture.Control<TextBox>("Bench files destination folder", dialog).Text);
            ResponsiveShellTests.Inside(status, dialog);
            Assert.DoesNotContain(Directory.EnumerateDirectories(target), path => !Path.GetFileName(path).StartsWith('.'));
            Capture(dialog, "offline-missing-package", dark, width, height, font);

            using (var archive = ZipFile.Open(Path.Combine(source, "manual.TapPackage"), ZipArchiveMode.Create))
            {
                Entry(archive, "Packages/Offline GUI manual package/package.xml",
                    """<Package Name="Offline GUI manual package" Version="1.0.0" OS="Windows,Linux" Architecture="AnyCPU"><Files><File Path="offline-ui.dat" /></Files></Package>""");
                Entry(archive, "offline-ui.dat", "Manual archive retry payload.");
            }
            AuthoringUiFixture.Click(prepare);
            await Until(() => prepare.IsEnabled);
            Assert.StartsWith("Offline bundle prepared", status.Text, StringComparison.Ordinal);
            var bundle = Assert.Single(Directory.EnumerateDirectories(target), path => !Path.GetFileName(path).StartsWith('.'));
            var manifest = OfflineBenchBundle.Validate(bundle, OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture);
            Assert.Equal(id, manifest.PlanId);
            Assert.Contains(manifest.Packages, package => package.Name == "Offline GUI manual package");
            var details = fixture.Control<TextBox>("Prepared offline bundle details", dialog);
            Assert.True(details.IsReadOnly); Assert.Contains("1.0.0", details.Text);
            ResponsiveShellTests.Inside(status, dialog);
            details.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(details.Focus());
            dialog.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
            dialog.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.None, null);
            AuthoringUiFixture.Drain(); Assert.Equal(details.Text, await dialog.Clipboard!.TryGetTextAsync());
            ResponsiveShellTests.Inside(prepare, dialog); ResponsiveShellTests.Inside(close, dialog);
            Capture(dialog, "offline-complete-bundle", dark, width, height, font);
            var originalPlatform = platform.SelectedIndex;
            platform.SelectedIndex = originalPlatform == 0 ? 1 : 0;
            AuthoringUiFixture.Drain();
            Assert.False(details.IsVisible); Assert.Contains("options changed", status.Text, StringComparison.OrdinalIgnoreCase);
            Type(dialog, fixture.Control<TextBox>("Bench files destination folder", dialog), target + "-changed");
            Assert.DoesNotContain("prepared at", status.Text, StringComparison.OrdinalIgnoreCase);
            platform.SelectedIndex = originalPlatform;
            Type(dialog, fixture.Control<TextBox>("Bench files destination folder", dialog), target);
            AuthoringUiFixture.Click(prepare);
            await Until(() => prepare.IsEnabled);
            Assert.StartsWith("Offline bundle prepared", status.Text, StringComparison.Ordinal);
            Assert.True(details.IsVisible);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Until(() => owner.OwnedWindows.Count == 0 && trigger.IsFocused);
        }
        finally { app.RequestedThemeVariant = priorTheme; }
    }

    private static void Entry(ZipArchive archive, string path, string contents)
    {
        using var output = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false)); output.Write(contents);
    }

    private static void Type(Window window, TextBox field, string value)
    {
        field.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(field.Focus()); field.SelectAll();
        window.KeyTextInput(value); AuthoringUiFixture.Drain(); Assert.Equal(value, field.Text);
    }

    private static void Space(Window window)
    {
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); AuthoringUiFixture.Drain();
    }

    private static async Task Until(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!ready()) { AuthoringUiFixture.Drain(); await Task.Delay(10, timeout.Token); }
        AuthoringUiFixture.Drain();
    }

    private static void Capture(Window window, string name, bool dark, int width, int height, int font)
    {
        if (Environment.GetEnvironmentVariable("HT_CAPTURE_UI") != "1") return;
        AuthoringUiFixture.Drain(); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var root = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY") ?? Path.GetTempPath(); Directory.CreateDirectory(root);
        frame.Save(Path.Combine(root, $"{name}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
