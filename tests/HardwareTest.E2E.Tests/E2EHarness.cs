using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HardwareTest;
using HardwareTest.Features;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using Xunit;
using Unit = ReactiveUI.Primitives.RxVoid;

namespace HardwareTest.E2E.Tests;

/// Serializes Avalonia + OpenTAP E2E so PerTest isolation does not rebuild the compositor off-thread.
[CollectionDefinition("E2E", DisableParallelization = true)]
public sealed class E2ECollection;

/// OpenTAP worker + Avalonia dispatcher are process-global; close on the UI thread.
internal sealed class ShownMainWindow : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ServiceProvider _services;
    private int _disposed;

    public ShownMainWindow(MainWindow window, ServiceProvider services)
    {
        Window = window;
        _dispatcher = window.Dispatcher;
        _services = services;
        // PerTest isolation resets Avalonia's global dispatcher between cases. A
        // System.Timers callback from the outgoing session must use its own UI
        // dispatcher, not become the first owner of the next global UIThread.
        Main.RunTest.SessionPanel.UiScheduler = ScheduleSessionUpdate;
    }

    public MainWindow Window { get; }

    public MainWindowViewModel Main => E2EHarness.MainVm(Window);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) E2EHarness.Close(Window, _services, _dispatcher);
    }

    private void ScheduleSessionUpdate(Action action)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.Post(() =>
        {
            if (Volatile.Read(ref _disposed) == 0) action();
        });
    }
}

internal static class E2EHarness
{
    public static App RequireApp()
        => Application.Current as App
           ?? throw new InvalidOperationException("Avalonia Application is not initialized.");

    public static ShownMainWindow ShowMainWindow()
    {
        var app = RequireApp();
        var window = app.Services.GetRequiredService<MainWindow>();
        window.Show();
        WaitForStartup(MainVm(window));
        return new ShownMainWindow(window, app.Services);
    }

    public static void Close(Window window, ServiceProvider services, Dispatcher dispatcher)
    {
        void CloseCore()
        {
            try
            {
                if (window.DataContext is MainWindowViewModel main)
                {
                    // Prevent an outgoing debounce from starting a save after its app
                    // scope is gone. Live Settings tests keep the production dispatcher.
                    services.GetRequiredService<SettingsViewModel>().UiScheduler = _ => { };
                    if (!main.RunTest.IsRunning && !main.RunControl.IsRunning)
                    {
                        // State changes synchronously stop the idle timer and invalidate
                        // pending badge capture. Saved evidence is already asserted by
                        // the test before this teardown-only reset.
                        main.RunTest.Session.ChangeSession();
                    }
                }

                window.Close();
                dispatcher.RunJobs();
            }
            finally
            {
                // Every isolated headless app owns a fresh provider and OpenTAP
                // worker. Closing a Window does not dispose that application scope.
                // Release its exact provider before the next PerTest app starts.
                services.Dispose();
            }
        }

        if (dispatcher.CheckAccess())
        {
            CloseCore();
            return;
        }

        dispatcher.Invoke(CloseCore);
    }

    /// Pumps the dispatcher until deferred OpenTAP warm-up finishes (or warms explicitly if needed).
    public static void WaitForStartup(MainWindowViewModel main)
    {
        var limit = TimeSpan.FromMinutes(2);
        var sw = Stopwatch.StartNew();
        while (main.IsStartingUp && sw.Elapsed < limit)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }

        if (main.IsStartingUp)
        {
            throw new TimeoutException($"Startup overlay did not clear ({main.StartupStatus}).");
        }

        if (main.RunTest.ProgramSelection.Programs.Count > 0)
        {
            return;
        }

        // Headless hosts that skipped deferred startup still need a catalog for Run/Inspect smoke.
        main.RunTest.WarmProgramsAsync().GetAwaiter().GetResult();
    }

    public static MainWindowViewModel MainVm(MainWindow window)
        => (MainWindowViewModel)(window.DataContext
            ?? throw new InvalidOperationException("MainWindow has no DataContext."));

    public static Task ExecuteAsync(this ReactiveCommand<Unit, Unit> command)
        => command.Execute(Unit.Default).ToTask();

    /// Pumps the dispatcher so OpenTAP completions can run, then yields without leaving AvaloniaFact's UI context.
    public static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan? timeout = null, string? failureMessage = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(60);
        var sw = Stopwatch.StartNew();
        while (!predicate())
        {
            if (sw.Elapsed > limit)
            {
                throw new TimeoutException(failureMessage ?? "Condition was not met before timeout.");
            }

            await PumpUiAsync();
            await Task.Delay(50);
        }

        await PumpUiAsync();
    }

    public static async Task PumpUiAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.RunJobs();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => Dispatcher.UIThread.RunJobs());
    }

    public static RunTestViewModel RunTestVm(MainWindowViewModel main)
        => main.RunTest;

    public static ResultsViewModel ResultsVm(MainWindowViewModel main)
        => main.Results;

    public static ReportPreviewViewModel ReportPreviewVm(MainWindowViewModel main)
        => main.ReportPreview;
}
