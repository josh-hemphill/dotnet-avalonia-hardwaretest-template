using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HardwareTest.Widgets.MeasurementPlot;
using HardwareTest.Widgets.Presentation;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class HistoricalRecordingPreviewWorkflowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 900, 600, 20)]
    public async Task Historical_v4_import_and_keyboard_selection_show_real_samples_and_formula_without_upgrading_evidence(
        bool dark, int width, int height, int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        // The supplied plan's MeanGte step takes its own acquisition. Author a
        // genuine mean(VDC) calculation for this recording, which contains VDC only.
        var planPath = Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan");
        var compiler = new PlanCompiler();
        var draft = compiler.Load(planPath) with { Measure = [] };
        draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Acquire);
        draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Formula);
        compiler.Save(draft, planPath);
        var app = Application.Current!;
        var oldTheme = app.RequestedThemeVariant;
        try
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var window = fixture.Show(width, height);
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.FontSize = font;
            Assert.Equal((double)width, window.ClientSize.Width);
            fixture.OpenRememberedWorkspace();
            await fixture.WaitForReadinessAsync();
            fixture.NavigateTask(5);

            // Use the genuine historical fixture, preserving its schema and source bytes.
            var source = Path.Combine(FindRepoRoot(), "tests", "fixtures", "authoring", "recordings", "sample", "mean-vdc", "run.json");
            var original = File.ReadAllBytes(source);
            var picker = new FixedRecordingPicker(source);
            var preview = new WorkspacePreviewView(picker) { DataContext = fixture.ViewModel };
            window.FindControl<ContentControl>("SeparatePreview")!.Content = preview;
            AuthoringUiFixture.Drain();
            Assert.True(await preview.ImportRecordingAsync());
            Assert.Same(window, picker.Owner);
            await fixture.WaitForPreviewAsync();
            var vm = fixture.ViewModel;
            var imported = Assert.IsType<RunDataset>(vm.SelectedDataset);
            Assert.Equal(4, imported.Run.StoredSchemaVersion);
            Assert.True(imported.Run.IsSchemaReadOnly);
            Assert.Equal("mean-vdc-1 · read-only", Assert.Single(vm.DatasetItems));
            Assert.Equal(original, File.ReadAllBytes(imported.Path));

            // Re-select through the actual dialog: browsing alone cannot change the source.
            AuthoringUiFixture.Click(fixture.Control<Button>("Use example data"));
            await fixture.WaitForPreviewAsync();
            Assert.Null(vm.SelectedDataset);
            var dialog = PreviewUiActions.OpenRecordingPicker(fixture);
            var list = fixture.Control<ListBox>("Recordings", dialog);
            Assert.True(list.IsFocused);
            Press(dialog, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal(0, list.SelectedIndex);
            Assert.Equal("mean-vdc-1 · read-only", list.SelectedItem);
            Assert.Null(vm.SelectedDataset);
            var identity = fixture.Control<TextBox>("Selected recording details", dialog);
            Assert.True(identity.IsReadOnly);
            Assert.Contains("mean-vdc-1", identity.Text);
            Assert.Contains("Samples: 2", identity.Text);
            var use = fixture.Control<Button>("Use recording", dialog);
            Assert.True(use.Focus());
            Press(dialog, Key.Enter, PhysicalKey.Enter);
            Assert.Empty(window.OwnedWindows);
            await fixture.WaitForPreviewAsync();

            Assert.Equal("mean-vdc-1", vm.PreviewSourceName);
            Assert.True(vm.SelectedDataset!.Run.IsSchemaReadOnly);
            Assert.Equal(new[] { 1d, 3d }, Assert.Single(vm.BoardTiles, tile => tile.Preview.ChannelKey == "VDC").Preview.CannedSamples);
            Assert.Equal(2d, Assert.Single(vm.BoardTiles, tile => tile.Preview.ChannelKey == "VDC.mean").Preview.CannedValue);
            var acquisition = fixture.Control<BoardPreviewTileView>("Board tile VDC");
            Assert.Contains(acquisition.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("2 samples", StringComparison.Ordinal) == true);
            var plot = Assert.Single(acquisition.GetVisualDescendants().OfType<MeasurementPlotView>(), control => control.IsEffectivelyVisible);
            plot.BringIntoView(); AuthoringUiFixture.Drain();
            ResponsiveShellTests.Inside(plot, window);
            PlotCursorChangedEventArgs? readout = null;
            plot.CursorChanged += (_, value) => readout = value;
            // These readouts come from the actual plot's copied data, not the VM alone.
            plot.SetCursor(0);
            Assert.Equal(1d, readout!.Y!.Value);
            plot.SetCursor(0.005);
            Assert.Equal(3d, readout!.Y!.Value);
            plot.ClearCursor();
            Capture(window, "historical-v4-recorded-chart", dark, width, height, font);

            var calculation = fixture.Control<BoardPreviewTileView>("Board tile VDC.mean");
            var gauge = Assert.Single(calculation.GetVisualDescendants().OfType<MetricGaugeView>(), control => control.IsEffectivelyVisible);
            gauge.BringIntoView(); AuthoringUiFixture.Drain();
            ResponsiveShellTests.Inside(gauge, window);
            Assert.Equal(2d, Assert.IsType<MetricGaugeModel>(gauge.DataContext).Value);
            Capture(window, "historical-v4-recorded-formula", dark, width, height, font);

            Assert.False(vm.HasRecordedReferences);
            Assert.False(fixture.Control<Button>("Review recorded references").IsEffectivelyVisible);
            dialog = PreviewUiActions.OpenSourceDetails(fixture);
            var details = fixture.Control<TextBox>("Full preview data source", dialog);
            Assert.True(details.IsReadOnly);
            Assert.Contains("Historical schema 4 recording", details.Text);
            Assert.Contains("read-only sample preview", details.Text);
            Assert.Contains("golden-reference qualification requires a current recording", details.Text);
            ResponsiveShellTests.Inside(details, dialog);
            Capture(dialog, "historical-v4-readonly-disclosure", dark, width, height, font);
            Press(dialog, Key.Escape, PhysicalKey.Escape);
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(original, File.ReadAllBytes(source));
            Assert.Equal(original, File.ReadAllBytes(imported.Path));
        }
        finally { app.RequestedThemeVariant = oldTheme; }
    }

    private static void Press(Window window, Key key, PhysicalKey physical)
    {
        var owner = window.Owner;
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        var receiver = window.IsVisible ? window : owner;
        if (receiver?.IsVisible == true) receiver.KeyRelease(key, RawInputModifiers.None, physical, null);
        AuthoringUiFixture.Drain();
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "tests", "fixtures", "authoring", "recordings", "sample", "mean-vdc", "run.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("Historical recording fixture not found.");
    }

    private static void Capture(Window window, string name, bool dark, int width, int height, int font)
    {
        Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, window.ActualThemeVariant);
        if (Environment.GetEnvironmentVariable("HT_CAPTURE_UI") != "1") return;
        var root = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(root)) return;
        Directory.CreateDirectory(root);
        AuthoringUiFixture.Drain();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(root, $"{name}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private sealed class FixedRecordingPicker(string path) : IAuthoringRecordingPicker
    {
        public TopLevel? Owner { get; private set; }
        public Task<string?> PickAsync(TopLevel owner)
        {
            Owner = owner;
            return Task.FromResult<string?>(path);
        }
    }
}
