using Avalonia.Controls;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

internal static class PreviewDisclosureAssertions
{
    public static void CheckCompactBoard(AuthoringUiFixture fixture)
    {
        var window = fixture.Window ?? throw new InvalidOperationException("Show the authoring window first.");
        var preview = window.FindControl<ContentControl>("SeparatePreview")!.Content as WorkspacePreviewView;
        Assert.NotNull(preview);
        Assert.DoesNotContain(window.OwnedWindows, child => child.Title is "Preview data source" or "Choose a recording");
        var source = fixture.Control<Button>("Preview data source details");
        var recordings = fixture.Control<Button>("Choose a recording");
        Assert.True(source.IsEffectivelyVisible);
        Assert.Equal(fixture.ViewModel.HasRecordings, recordings.IsEffectivelyVisible);
        ResponsiveShellTests.Inside(source, window);
        if (fixture.ViewModel.HasRecordings) ResponsiveShellTests.Inside(recordings, window);
        var region = preview.FindControl<Grid>("BoardRegion")!;
        var layout = preview.FindControl<Grid>("PreviewLayout")!;
        Assert.True(region.Bounds.Height >= layout.Bounds.Height * 0.55 - 1,
            $"The board must own most of Preview: board={region.Bounds.Height}, route={layout.Bounds.Height}");
        ResponsiveShellTests.Inside(fixture.Control<OperatorPreviewPane>("Operator preview chrome"), window);
        ResponsiveShellTests.Inside(fixture.Control<Button>("Import recording"), window);
    }

    public static void CheckSourceDetailsDialog(AuthoringUiFixture fixture, string provenance)
    {
        var board = fixture.Control<OperatorPreviewPane>("Operator preview chrome");
        var boardHeight = board.Bounds.Height;
        var dialog = PreviewUiActions.OpenSourceDetails(fixture);
        var text = fixture.Control<TextBox>("Full preview data source", dialog);
        Assert.True(text.IsReadOnly);
        Assert.True(text.AcceptsReturn);
        Assert.Equal(fixture.ViewModel.DataSourceDetails, text.Text);
        Assert.Contains(provenance, text.Text!);
        text.SelectAll();
        Assert.Contains(provenance, text.SelectedText);
        ResponsiveShellTests.Inside(text, dialog);
        Assert.Contains(text.GetVisualDescendants().OfType<ScrollViewer>(), viewport => viewport.Extent.Height > viewport.Viewport.Height);
        Assert.Equal(boardHeight, board.Bounds.Height, 1);
        AuthoringUiFixture.Click(fixture.Control<Button>("Close preview data source", dialog));
        Assert.Empty(fixture.Window!.OwnedWindows);
        Assert.Equal(boardHeight, board.Bounds.Height, 1);
    }

    public static void CheckRecordingPicker(AuthoringUiFixture fixture)
    {
        var original = fixture.ViewModel.SelectedDataset;
        var board = fixture.Control<OperatorPreviewPane>("Operator preview chrome");
        var boardHeight = board.Bounds.Height;
        var dialog = PreviewUiActions.OpenRecordingPicker(fixture);
        var recordings = fixture.Control<ListBox>("Recordings", dialog);
        Assert.Equal(fixture.ViewModel.SelectedDatasetIndex, recordings.SelectedIndex);
        recordings.BringIntoView();
        AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(recordings, dialog);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel recording selection", dialog));
        Assert.Same(original, fixture.ViewModel.SelectedDataset);
        Assert.Equal(boardHeight, board.Bounds.Height, 1);
    }
}
