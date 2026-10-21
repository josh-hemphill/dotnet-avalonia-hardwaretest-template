using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Widgets.MeasurementPlot;
using HardwareTest.Widgets.Presentation;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class ResponsivePreviewTests
{
    public static IEnumerable<object[]> PreviewCases()
    {
        foreach (var (width, height, font, scale) in new[] { (960, 600, 14, 1d), (1280, 800, 14, 1d), (960, 600, 20, 1.5), (1280, 800, 20, 1.5) })
            foreach (var role in new[] { "timeseries", "scalar", "timing" })
                foreach (var busy in new[] { false, true })
                    yield return [width, height, font, scale, role, busy];
    }

    [AvaloniaTheory]
    [MemberData(nameof(PreviewCases))]
    public async Task Existing_preview_variants_and_recordings_fit_their_real_viewport_with_operation_progress(
        int width, int height, int fontSize, double scaling, string role, bool busy)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        window.FontSize = fontSize;
        window.SetRenderScaling(scaling);
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.StopRecovery();
        Assert.Equal(new Size(width, height), window.ClientSize);
        Assert.Equal(scaling, window.RenderScaling);
        fixture.ViewModel.SelectSequence(fixture.ViewModel.SequenceItems.ToList().FindIndex(row => row.Label == "Acquire VDC"));
        fixture.ViewModel.DisplayRole = role;
        Task? operation = null;
        if (busy)
        {
            fixture.ViewModel.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(
                Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")),
                action => Dispatcher.UIThread.Post(action));
            File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-wait"), "");
            operation = fixture.ViewModel.RunOperationAsync(AuthoringOperationKind.Bootstrap);
            await Until(() => File.Exists(Path.Combine(fixture.WorkspaceRoot, "fixture-child.json")));
            Assert.True(fixture.ViewModel.OperationBusy);
            ResponsiveShellTests.Inside(fixture.Control<Button>("Cancel authoring operation"), window);
        }
        try
        {
            var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
            tabs.SelectedIndex = 5;
            AuthoringUiFixture.Drain();
            CheckPreview();
        }
        finally
        {
            if (operation is not null)
            {
                fixture.ViewModel.CancelOperation();
                try { await operation; }
                catch (OperationCanceledException) { }
                AuthoringUiFixture.Drain();
                Assert.False(fixture.ViewModel.OperationBusy);
            }
        }

        void CheckPreview()
        {
            var scroll = fixture.Control<ScrollViewer>("Operator board viewport");
            Assert.Empty(scroll.GetVisualAncestors().OfType<ScrollViewer>());
            scroll.Offset = default;
            AuthoringUiFixture.Drain();
            ResponsiveShellTests.Inside(scroll, window);
            var viewport = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollContentPresenter>(),
                presenter => ReferenceEquals(presenter.Content, scroll.Content));
            ResponsiveShellTests.Inside(viewport, window);
            Assert.True(viewport.Bounds.Height >= 80);
            Assert.True(scroll.ClipToBounds);
            Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            var preview = fixture.Control<OperatorPreviewPane>("Operator preview chrome");
            var selectedTile = preview.GetVisualDescendants().OfType<BoardPreviewTileView>().First(tile => Avalonia.Automation.AutomationProperties.GetName(tile) == "Board tile VDC");
            Control tile = role switch
            {
                "timeseries" => Assert.Single(selectedTile.GetVisualDescendants().OfType<MeasurementPlotView>()),
                "scalar" => Assert.Single(selectedTile.GetVisualDescendants().OfType<MetricGaugeView>()),
                _ => Assert.Single(selectedTile.GetVisualDescendants().OfType<TimingStripView>()),
            };
            tile.BringIntoView();
            AuthoringUiFixture.Drain();
            ResponsiveShellTests.Inside(tile, window);
            var recordings = fixture.Control<ListBox>("Recordings");
            recordings.BringIntoView();
            AuthoringUiFixture.Drain();
            ResponsiveShellTests.Inside(recordings, window);
            Assert.True(scroll.Focus());
            ResponsiveShellTests.Inside(scroll, window);
            if (width == 960 && fontSize == 20 && busy && role == "timeseries")
            {
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                Assert.True(scroll.Offset.Y > 0);
                // The board owns its wheel input; the recording picker is a sibling.
                var center = preview.TranslatePoint(new Point(8, 8), window);
                Assert.NotNull(center);
                window.MouseWheel(center.Value, new Vector(0, 1000), RawInputModifiers.None);
                AuthoringUiFixture.Drain();
                Assert.Equal(0, scroll.Offset.Y);
                ResponsiveShellTests.Inside(recordings, window);
            }
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Preview operation did not reach the waiting child.");
            await Task.Delay(20);
            AuthoringUiFixture.Drain();
        }
        AuthoringUiFixture.Drain();
    }
}
