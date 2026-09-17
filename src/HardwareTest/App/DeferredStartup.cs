namespace HardwareTest;

/// Quiet-cancel wrapper for fire-and-forget deferred startup.
internal static class DeferredStartup
{
    /// Runs <paramref name="work"/> then always <paramref name="onComplete"/>.
    /// Shutdown cancel is quiet; other faults go to <paramref name="onUnexpected"/>.
    public static async Task RunProtectedAsync(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken,
        Func<Task> onComplete,
        Func<Exception, Task>? onUnexpected = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(onComplete);

        try
        {
            await work(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Operator closed the window during overlay startup.
        }
        catch (Exception ex)
        {
            if (onUnexpected is not null)
            {
                await onUnexpected(ex).ConfigureAwait(false);
            }
        }
        finally
        {
            await onComplete().ConfigureAwait(false);
        }
    }
}
