namespace HardwareTest.Authoring.UI.Tests;

internal static class PreviewUiActions
{
    internal static async Task WaitForPreviewAsync(this AuthoringUiFixture fixture)
    {
        await fixture.ViewModel.WaitForPreviewAsync().WaitAsync(TimeSpan.FromSeconds(20));
        // The presentation publishes on the dispatcher; widget construction is
        // deliberately coalesced into a subsequent visible-pane render callback.
        AuthoringUiFixture.Drain();
    }
}
