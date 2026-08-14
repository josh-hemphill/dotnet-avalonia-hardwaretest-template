using Avalonia.Threading;
using Serilog;

namespace HardwareTest.UiThreading;

/// Marshals work onto the Avalonia UI thread; drops the action when no dispatcher is available.
internal static class UiDispatch
{
    /// Posts <paramref name="action"/> to the UI thread (or runs inline when already on it).
    /// Prefer a view-model <paramref name="scheduler"/> test seam when provided.
    public static void Post(Action action, Action<Action>? scheduler = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (scheduler is not null)
        {
            scheduler(action);
            return;
        }

        // Unit-test / headless host without a started Avalonia app — run inline.
        if (Avalonia.Application.Current is null)
        {
            action();
            return;
        }

        try
        {
            var dispatcher = Dispatcher.UIThread;
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Post(action, DispatcherPriority.Normal);
            }
        }
        catch (Exception ex)
        {
            // Do NOT run action() on the current (background) thread under a live app —
            // that would mutate UI-bound state off the UI thread. Log and no-op instead.
            Log.Warning(
                ex,
                "UiDispatch.Post dropped a UI update because the dispatcher was unavailable");
        }
    }

    /// Awaits <paramref name="action"/> on the UI thread (or runs inline via scheduler / CheckAccess).
    public static Task RunAsync(Action action, Action<Action>? scheduler = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (scheduler is not null)
        {
            scheduler(action);
            return Task.CompletedTask;
        }

        // Unit-test / headless host without a started Avalonia app — run inline so
        // ViewModel tests that omit UiScheduler still observe state changes, and so we
        // never InvokeAsync onto a dispatcher that has no message pump (hang).
        if (Avalonia.Application.Current is null)
        {
            action();
            return Task.CompletedTask;
        }

        try
        {
            var dispatcher = Dispatcher.UIThread;
            if (dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).GetTask();
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "UiDispatch.RunAsync dropped a UI update because the dispatcher was unavailable");
            return Task.CompletedTask;
        }
    }
}
