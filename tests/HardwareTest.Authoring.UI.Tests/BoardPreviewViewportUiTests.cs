using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class BoardPreviewViewportUiTests
{
    [AvaloniaTheory]
    [InlineData(960, 600, 14, 1)]
    [InlineData(1280, 800, 14, 1)]
    [InlineData(960, 600, 20, 1.5)]
    [InlineData(1280, 800, 20, 1.5)]
    public async Task Long_recording_provenance_keeps_source_controls_and_board_viewport_accessible(int width, int height, int fontSize, double scale)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        window.FontSize = fontSize;
        window.SetRenderScaling(scale);
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.StopRecovery();
        var recording = new TestRunRecord
        {
            PlanId = vm.SelectedProgram!.PlanId,
            DutSerial = string.Join(" ", Enumerable.Repeat("Long translated device identification and recorded provenance", 50)),
            Samples = [new StoredSample { MetricKey = "VDC", Value = 1, ElapsedMs = 0 }, new StoredSample { MetricKey = "VDC", Value = 2, ElapsedMs = 5 }]
        };
        var path = Path.Combine(fixture.WorkspaceRoot, "long-provenance.json");
        File.WriteAllText(path, JsonSerializer.Serialize(recording, AppJsonContext.Default.TestRunRecord));
        vm.ImportRecording(path, "long-provenance");
        await fixture.WaitForPreviewAsync();
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 5;
        AuthoringUiFixture.Drain();
        var board = fixture.Control<OperatorPreviewPane>("Operator preview chrome");
        ResponsiveShellTests.Inside(board, window);
        PreviewDisclosureAssertions.CheckCompactBoard(fixture);
        fixture.Control<Expander>("Preview data source details").IsExpanded = true;
        AuthoringUiFixture.Drain();
        var sourceViewport = fixture.Control<ScrollViewer>("Data source details viewport");
        Assert.True(sourceViewport.Extent.Height > sourceViewport.Viewport.Height);
        fixture.Control<Expander>("Preview data source details").IsExpanded = false;
        fixture.Control<Expander>("Choose a recording").IsExpanded = true;
        AuthoringUiFixture.Drain();
        var recordings = fixture.Control<ListBox>("Recordings");
        recordings.BringIntoView();
        AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(recordings, window);
        fixture.Control<Expander>("Choose a recording").IsExpanded = false;
        AuthoringUiFixture.Drain();
        var examples = fixture.Control<Button>("Use example data");
        examples.BringIntoView();
        AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(examples, window);
        AuthoringUiFixture.Click(examples);
        Assert.Null(vm.SelectedDataset);
        Assert.Equal("Example data (simulated)", vm.PreviewDataSourceLabel);
        PreviewDisclosureAssertions.CheckCompactBoard(fixture);
        ResponsiveShellTests.Inside(board, window);
    }
}
