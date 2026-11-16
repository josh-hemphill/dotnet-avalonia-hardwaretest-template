using Avalonia.Controls;

namespace HardwareTest.Authoring.UI.Tests;

internal static class PreviewUiActions
{
    internal static Window OpenSourceDetails(AuthoringUiFixture fixture)
    {
        AuthoringUiFixture.Click(fixture.Control<Button>("Preview data source details"));
        var dialog = Xunit.Assert.Single(fixture.Window!.OwnedWindows);
        Xunit.Assert.Equal("Preview data source", dialog.Title);
        return dialog;
    }

    internal static Window OpenRecordingPicker(AuthoringUiFixture fixture)
    {
        AuthoringUiFixture.Click(fixture.Control<Button>("Choose a recording"));
        var dialog = Xunit.Assert.Single(fixture.Window!.OwnedWindows);
        Xunit.Assert.Equal("Choose a recording", dialog.Title);
        return dialog;
    }

    internal static void SelectRecording(AuthoringUiFixture fixture, int index)
    {
        var original = fixture.ViewModel.SelectedDatasetIndex;
        var dialog = OpenRecordingPicker(fixture);
        fixture.Control<ListBox>("Recordings", dialog).SelectedIndex = index;
        AuthoringUiFixture.Drain();
        Xunit.Assert.Equal(original, fixture.ViewModel.SelectedDatasetIndex);
        AuthoringUiFixture.Click(fixture.Control<Button>("Use recording", dialog));
        Xunit.Assert.Empty(fixture.Window!.OwnedWindows);
        Xunit.Assert.Equal(index, fixture.ViewModel.SelectedDatasetIndex);
    }

    internal static void CheckRecordingSelection(AuthoringUiFixture fixture, int expected)
    {
        Xunit.Assert.Equal(expected, fixture.ViewModel.SelectedDatasetIndex);
        var dialog = OpenRecordingPicker(fixture);
        Xunit.Assert.Equal(expected, fixture.Control<ListBox>("Recordings", dialog).SelectedIndex);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel recording selection", dialog));
        Xunit.Assert.Empty(fixture.Window!.OwnedWindows);
        Xunit.Assert.Equal(expected, fixture.ViewModel.SelectedDatasetIndex);
    }

    internal static async Task WaitForPreviewAsync(this AuthoringUiFixture fixture)
    {
        // A completed preview can be invalidated by a later environment/build result.
        // Presentation assertions need the complete readiness fixed point; tests that
        // deliberately hold workers await the view model's individual pipeline directly.
        await fixture.WaitForReadinessAsync();
        // The presentation publishes on the dispatcher; widget construction is
        // deliberately coalesced into a subsequent visible-pane render callback.
        AuthoringUiFixture.Drain();
    }
}
