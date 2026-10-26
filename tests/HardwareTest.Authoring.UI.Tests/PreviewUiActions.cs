namespace HardwareTest.Authoring.UI.Tests;

internal static class PreviewUiActions
{
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
