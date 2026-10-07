using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.Widgets.MeasurementPlot;
using HardwareTest.Widgets.Presentation;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class BoardPreviewUiTests
{
    [AvaloniaFact]
    public void Selecting_same_channel_nodes_keeps_their_own_units_limits_and_board_tiles()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        MetricNode Publisher(string name, string unit, double low, double high) => new(new MetricDraft(name, "same", PresentationRoles.Timeseries, unit,
            new LimitSpec(low, high, null), null, new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string> { ["SampleCount"] = "3", ["IntervalMs"] = "5" })));
        var first = Publisher("first scope", "V", 0, 4);
        var second = Publisher("second scope", "mV", 100, 200);
        var draft = new ProgramDraft("scoped", new ProgramSidecar { DisplayName = "Scoped" },
            [new InstrumentRef("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::INSTR0")], [],
            [new RepeatNode(2, [first]), new RepeatNode(3, [second])], new CleanupPolicy(false, "DMM"));
        new AuthoringDocumentStore(fixture.WorkspaceRoot).Save(AuthoringDocumentDto.FromDraft(draft));
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.StopRecovery();
        vm.SelectProgram("scoped");
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 5;
        foreach (var node in new[] { first, second })
        {
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == node.NodeId));
            AuthoringUiFixture.Drain();
            Assert.Equal(node.Metric.YUnit, vm.Preview.YUnit);
            Assert.Equal(node.Metric.Limits!.High, vm.Preview.LimitHigh);
            Assert.Equal(node.NodeId, vm.SelectedSequence!.NodeId);
        }
        var board = fixture.Control<OperatorPreviewPane>("Operator preview chrome");
        Assert.Equal(2, board.GetVisualDescendants().OfType<BoardPreviewTileView>().Count());
        Assert.Contains(board.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "first scope · same");
        Assert.Contains(board.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "second scope · same");
    }

    [AvaloniaTheory]
    [InlineData(960)]
    [InlineData(1280)]
    public void Actual_board_shows_acquisition_and_derived_shared_widgets_and_example_selection(int width)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, 800);
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.StopRecovery();
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 5;
        AuthoringUiFixture.Drain();
        var board = fixture.Control<OperatorPreviewPane>("Operator preview chrome");
        var tiles = board.GetVisualDescendants().OfType<BoardPreviewTileView>().ToArray();
        Assert.True(tiles.Length >= 2);
        Assert.Contains(board.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Criterion: ≥", StringComparison.Ordinal) == true);
        Assert.Contains(tiles, tile => tile.GetVisualDescendants().OfType<MeasurementPlotView>().Any(view => view.IsVisible));
        Assert.Contains(tiles, tile => tile.GetVisualDescendants().OfType<MetricGaugeView>().Any(view => view.IsVisible));
        foreach (var tile in tiles)
        {
            tile.BringIntoView();
            AuthoringUiFixture.Drain();
            Assert.True(tile.IsEffectivelyVisible);
            Assert.True(tile.Bounds.Width > 100);
            Assert.True(tile.Bounds.Height > 30);
            var visibleWidget = tile.GetVisualDescendants().OfType<Control>().FirstOrDefault(view => view is MeasurementPlotView or MetricGaugeView or TimingStripView && view.IsVisible);
            if (visibleWidget is not null) ResponsiveShellTests.Inside(visibleWidget, window);
        }
        var example = fixture.Control<Button>("Use example data");
        example.BringIntoView(); AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(example);
        Assert.Null(fixture.ViewModel.SelectedDataset);
        Assert.Contains("Example data", fixture.ViewModel.DataSourceDetails);
        Assert.True(fixture.Control<Button>("Import recording").IsEnabled);
        Assert.True(fixture.Control<Button>("Open recordings folder").IsEnabled);
    }
}
