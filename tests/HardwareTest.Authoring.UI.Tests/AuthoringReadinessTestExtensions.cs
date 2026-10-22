namespace HardwareTest.Authoring.UI.Tests;

internal static class AuthoringReadinessTestExtensions
{
    /// Wait only where a test asserts published readiness. Drain stays nonblocking so
    /// tests that deliberately hold a worker can still exercise dispatcher interaction.
    internal static async Task WaitForReadinessAsync(this AuthoringUiFixture fixture)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var vm = fixture.ViewModel;
        while (true)
        {
            AuthoringUiFixture.Drain();
            await vm.WaitForEnvironmentInspectionAsync().WaitAsync(timeout.Token);
            await vm.WaitForBuildInspectionAsync(timeout.Token);
            await vm.WaitForEditingIssuesAsync(timeout.Token);
            await vm.WaitForPreviewAsync(timeout.Token);
            AuthoringUiFixture.Drain();
            // Publishing one snapshot can invalidate another. Inspect the latest idle
            // tasks after dispatch rather than retaining a completed earlier generation.
            if (!vm.EnvironmentInspectionPending && !vm.BuildInspectionBusy && !vm.EditingIssuesBusy && !vm.PreviewBusy
                && vm.WaitForEnvironmentInspectionAsync().IsCompleted && vm.WaitForBuildInspectionAsync().IsCompleted
                && vm.WaitForEditingIssuesAsync().IsCompleted && vm.WaitForPreviewAsync().IsCompleted) return;
            await Task.Delay(1, timeout.Token);
        }
    }
}
