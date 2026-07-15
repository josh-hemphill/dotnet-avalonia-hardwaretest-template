using System;
using Avalonia;
using ReactiveUI.Avalonia;
using HardwareTest.Core.Logging;
using HardwareTest.Core.Settings;
using Serilog;

namespace HardwareTest;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var settingsStore = new SettingsStore();
        settingsStore.LoadAsync().GetAwaiter().GetResult();

        var logDir = Path.Combine(settingsStore.RootDirectory, "logs");
        using var logging = LoggingBootstrap.Initialize(settingsStore.AppSettings, logDir);

        try
        {
            BuildAvaloniaApp(settingsStore)
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(new SettingsStore());

    public static AppBuilder BuildAvaloniaApp(ISettingsStore settingsStore)
        => AppBuilder.Configure(() => new App(settingsStore))
            .UsePlatformDetect()
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 256 * 1024 * 1024,
            })
            .WithInterFont()
            .UseReactiveUI(_ => { })
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
