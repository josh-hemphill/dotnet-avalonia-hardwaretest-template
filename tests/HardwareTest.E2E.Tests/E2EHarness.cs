using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia;
using HardwareTest;
using HardwareTest.Features;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace HardwareTest.E2E.Tests;

internal static class E2EHarness
{
    public static App RequireApp()
        => Application.Current as App
           ?? throw new InvalidOperationException("Avalonia Application is not initialized.");

    public static MainWindow ShowMainWindow()
    {
        var app = RequireApp();
        var window = app.Services.GetRequiredService<MainWindow>();
        window.Show();
        return window;
    }

    public static MainWindowViewModel MainVm(MainWindow window)
        => (MainWindowViewModel)(window.DataContext
            ?? throw new InvalidOperationException("MainWindow has no DataContext."));

    public static Task ExecuteAsync(this ReactiveCommand<Unit, Unit> command)
        => command.Execute(Unit.Default).ToTask();

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

            await Task.Delay(50);
        }
    }

    public static RunTestViewModel RunTestVm(MainWindowViewModel main)
        => (RunTestViewModel)main.NavigationItems.First(i => i.Id == "RunTest").ViewModel;

    public static ResultsViewModel ResultsVm(MainWindowViewModel main)
        => (ResultsViewModel)main.NavigationItems.First(i => i.Id == "Results").ViewModel;

    public static ReportPreviewViewModel ReportPreviewVm(MainWindowViewModel main)
        => (ReportPreviewViewModel)main.NavigationItems.First(i => i.Id == "ReportPreview").ViewModel;
}
