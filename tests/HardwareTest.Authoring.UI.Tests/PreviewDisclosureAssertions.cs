using Avalonia.Controls;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

internal static class PreviewDisclosureAssertions
{
    public static void CheckCompactBoard(AuthoringUiFixture fixture)
    {
        var preview = fixture.Window!.FindControl<ContentControl>("SeparatePreview")!.Content as WorkspacePreviewView;
        Assert.NotNull(preview);
        var source = preview.FindControl<Expander>("SourceDetailsDisclosure")!;
        var recordings = preview.FindControl<Expander>("RecordingsDisclosure")!;
        Assert.False(source.IsExpanded);
        Assert.False(recordings.IsExpanded);
        Assert.Equal(fixture.ViewModel.HasRecordings, recordings.IsEffectivelyVisible);
        var region = preview.FindControl<Grid>("BoardRegion")!;
        var layout = preview.FindControl<Grid>("PreviewLayout")!;
        Assert.True(region.Bounds.Height >= layout.Bounds.Height * 0.55 - 1,
            $"The board must own most of Preview: board={region.Bounds.Height}, route={layout.Bounds.Height}");
        ResponsiveShellTests.Inside(fixture.Control<OperatorPreviewPane>("Operator preview chrome"), fixture.Window);
        ResponsiveShellTests.Inside(fixture.Control<Button>("Import recording"), fixture.Window);
    }
}
