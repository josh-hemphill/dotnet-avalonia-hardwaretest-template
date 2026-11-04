using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Features.Presentation;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.Widgets.MeasurementPlot;
using HardwareTest.Widgets.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorResultsLayoutTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Compact_results_use_full_width_and_close_reopens_the_same_selected_run_without_changing_evidence(int font)
    {
        using var shown = E2EHarness.ShowMainWindow();
        var window = shown.Window; window.Width = 900; window.Height = 600; window.FontSize = font;
        var main = shown.Main; main.IsNavPaneOpen = false;
        main.NavigateToPageId("Results");
        var vm = main.Results;
        await vm.RefreshCommand.ExecuteAsync();
        var view = window.GetVisualDescendants().OfType<ResultsView>().Single(control => control.IsEffectivelyVisible);
        var layout = view.FindControl<Grid>("ResultsLayout")!;
        var list = view.FindControl<ListBox>("RunResultsList")!;
        Assert.Equal(0, layout.ColumnDefinitions[1].Width.Value);
        var store = E2EHarness.RequireApp().Services.GetRequiredService<IRunStore>();
        var record = new TestRunRecord
        {
            RunId = "layout-" + Guid.NewGuid().ToString("N"),
            PlanId = "operator-layout",
            PlanName = "Controller acceptance",
            DutSerial = "DUT-UI-42",
            DutPartNumber = "PART-42",
            OperatorName = "Review operator",
            SessionId = "SESSION-UI-42",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 5, Unit = "V" }]
        };
        await store.SaveAsync(record);
        var storedPath = Path.Combine(store.GetRunDirectory(record.RunId), "run.json");
        var bytes = File.ReadAllBytes(storedPath);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SearchText = record.RunId;
        await E2EHarness.PumpUiAsync();
        var summary = Assert.Single(vm.Runs);
        vm.SelectedRun = summary;
        await E2EHarness.WaitUntilAsync(() => vm.ShowDetail);
        Assert.False(view.FindControl<DockPanel>("ResultsListPane")!.IsEffectivelyVisible);
        var detail = view.FindControl<Border>("ResultsDetailPane")!;
        Assert.Equal(0, Grid.GetColumn(detail));
        Inside(detail, window);
        var close = view.FindControl<Button>("ResultsCloseButton")!;
        Inside(close, window);
        OperatorWorkflowUiTests.Click(close);
        Assert.False(vm.ShowDetail);
        Assert.Same(summary, vm.SelectedRun);
        var focused = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
        Assert.True(ReferenceEquals(focused, list) || focused.GetVisualAncestors().Contains(list));
        Assert.True(list.Bounds.Width > 650);
        var row = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(0));
        var rowName = ControlAutomationPeer.CreatePeerForElement(row).GetName();
        Assert.Contains(summary.PlanName, rowName);
        Assert.Contains(summary.DutSerial!, rowName);
        Assert.Contains(summary.Result.ToString(), rowName);
        Inside(row, window);
        var click = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(click, MouseButton.Left); window.MouseUp(click, MouseButton.Left);
        await E2EHarness.WaitUntilAsync(() => vm.ShowDetail);
        Assert.Equal(record.RunId, vm.OpenedRun!.RunId);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await E2EHarness.PumpUiAsync();
        Assert.False(vm.ShowDetail);
        Assert.True(list.Focus());
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await E2EHarness.WaitUntilAsync(() => vm.ShowDetail);
        OperatorWorkflowUiTests.Click(close);
        var search = view.FindControl<TextBox>("ResultsSearchBox")!;
        Assert.NotNull(AutomationProperties.GetLabeledBy(search));
        search.Focus(); search.SelectAll(); window.KeyTextInput("no-matching-run-identifier");
        await E2EHarness.WaitUntilAsync(() => vm.Runs.Count == 0);
        Assert.Equal("No matching runs", vm.EmptyResultsTitle);
        var clear = ByName<Button>(view, "Clear unmatched results search and filters");
        Inside(clear, window);
        OperatorWorkflowUiTests.Click(clear);
        Assert.Contains(vm.Runs, run => run.RunId == record.RunId);
        Assert.False(vm.HasActiveFilters);
        var filters = ByName<Expander>(view, "Result filters"); filters.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        foreach (var name in new[] { "result", "plan", "dut", "operator", "date" })
        {
            var field = ByName<ComboBox>(view, "Filter results by " + name);
            Assert.NotNull(AutomationProperties.GetLabeledBy(field));
            Inside(field, window);
        }
        Assert.Equal(bytes, File.ReadAllBytes(storedPath));
        Assert.False(main.RunTest.IsRunning);
    }

    [AvaloniaTheory]
    [InlineData(900, 600, 20, 120)]
    [InlineData(1280, 800, 14, 300)]
    public async Task Dedicated_chart_keeps_metric_options_and_a_real_plot_inside_the_workspace(int width, int height, int font, int minimumPlotHeight)
    {
        using var shown = E2EHarness.ShowMainWindow();
        var window = shown.Window; window.Width = width; window.Height = height; window.FontSize = font;
        var main = shown.Main; main.IsNavPaneOpen = width >= 1280;
        main.NavigateToPageId("RunTest");
        var run = main.RunTest;
        run.SessionPanel.DutSerialInput = "CHART-UI-42"; run.SessionPanel.DutPartInput = "PART-42";
        run.SessionPanel.DutRevisionInput = "B"; run.SessionPanel.OperatorInput = "Chart operator";
        await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(run.SessionPanel.SessionBlocked);
        run.Live.ApplySample(new MeasurementSampleEvent("VDC", 0, 5, DateTimeOffset.UtcNow, DisplayRole: "timeseries", Unit: "V"), "Review/Voltage", null, null);
        run.Live.ApplySample(new MeasurementSampleEvent("VDC", 1, 5.1, DateTimeOffset.UtcNow.AddSeconds(1), DisplayRole: "timeseries", Unit: "V"), "Review/Voltage", null, null);
        run.Workspace.Refresh(); run.Workspace.OpenChart();
        await E2EHarness.PumpUiAsync();
        var chart = window.GetVisualDescendants().OfType<RunChartWorkspaceView>().Single(control => control.IsEffectivelyVisible);
        Inside(ByName<ComboBox>(chart, "Metric"), window);
        Inside(ByName<Button>(chart, "Back to steps"), window);
        var options = ByName<Button>(chart, "Chart options"); Inside(options, window);
        var plot = chart.FindControl<MeasurementPlotView>("Plot")!;
        Inside(plot, window);
        Assert.True(plot.Bounds.Height >= minimumPlotHeight, $"Chart plot height {plot.Bounds.Height} must remain useful.");
        Assert.True(plot.Bounds.Width >= 300);
        var popup = Assert.IsType<Flyout>(options.Flyout); popup.ShowAt(options);
        Dispatcher.UIThread.RunJobs();
        var menu = Assert.IsType<ScrollViewer>(popup.Content);
        Assert.True(ByName<ComboBox>(menu, "Sample window").IsEffectivelyVisible);
        Assert.True(ByName<Button>(menu, "Reset chart view").IsEffectivelyVisible);
        popup.Hide();
        Assert.Equal(2, run.Live.PlotYsLength);
        Assert.False(run.IsRunning);
    }

    [AvaloniaFact]
    public void Saved_chart_does_not_capture_page_wheel_until_interaction_is_requested()
    {
        var tile = new PresentationTileViewModel("VDC", PresentationTileKind.Timeseries, "timeseries", "V", "Review/Voltage");
        tile.SetSeries([1, 2, 3]);
        var host = new ResultsChartHost { DataContext = tile };
        var content = new StackPanel { Children = { host, new Border { Height = 1000 } } };
        var scroll = new ScrollViewer { Content = content };
        var window = new Window { Width = 700, Height = 500, Content = scroll };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var plot = host.GetVisualDescendants().OfType<MeasurementPlotView>().Single();
            var interaction = ByName<CheckBox>(host, "Interact with saved results chart");
            Assert.False(plot.IsHitTestVisible);
            var center = plot.TranslatePoint(new Point(plot.Bounds.Width / 2, plot.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(center, new Vector(0, -1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y > 0);
            interaction.IsChecked = true; Dispatcher.UIThread.RunJobs();
            Assert.True(plot.IsHitTestVisible);
            plot.SetCursor(1);
            Assert.NotNull(plot.CursorX);
            interaction.IsChecked = false; Dispatcher.UIThread.RunJobs();
            Assert.False(plot.IsHitTestVisible);
            Assert.Null(plot.CursorX);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    private static T ByName<T>(Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    private static void Inside(Control control, Window window) => OperatorWorkflowUiTests.Inside(control, window);
}
