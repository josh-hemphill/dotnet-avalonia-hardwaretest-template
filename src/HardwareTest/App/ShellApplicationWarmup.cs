using HardwareTest.Shell;

namespace HardwareTest;

/// Runs optional shell-app warmup while the startup overlay is visible.
public static class ShellApplicationWarmup
{
    /// Invokes each app's WarmAsync. A faulted app does not skip remaining apps.
    public static async Task RunAsync(
        IEnumerable<IShellApplication> applications,
        IServiceProvider services,
        CancellationToken cancellationToken,
        Action<IShellApplication, Exception>? onFault = null)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(services);

        foreach (var application in applications)
        {
            ArgumentNullException.ThrowIfNull(application);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await application.WarmAsync(services, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                try
                {
                    onFault?.Invoke(application, ex);
                }
                catch (Exception callbackEx)
                {
                    _ = callbackEx;
                }
            }
        }
    }
}
