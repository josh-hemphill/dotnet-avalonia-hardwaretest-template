using System;
using Avalonia;
using ReactiveUI.Avalonia;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Logging;
using HardwareTest.Core.Settings;
using HardwareTest.Crash;
using HardwareTest.OpenTap.Host;
using Serilog;

namespace HardwareTest;

static class Program
{
    internal static string? SimulateCrashMode { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Stage 1: resolve DataDirectory + LogMinimumLevel from env + command line only
        // (before logging / settings.json). Stage 2 re-applies overlays after the file load.
        var parsed = ConfigurationArgs.Parse(args);
        if (parsed.PrintVersion)
        {
            var versionInfo = OpenTapBuildInfo.Attach(BuildInfo.FromEntryAssembly());
            Console.Out.WriteLine(versionInfo.InformationalVersion);
            return 0;
        }

        var stage1 = ConfigurationBootstrap.ResolveStage1(parsed);
        Directory.CreateDirectory(stage1.RootDirectory);

        var store = new SettingsStore(
            stage1.RootDirectory,
            settingsFilePath: parsed.SettingsPath);

        var bootstrapSettings = new AppSettings
        {
            DataDirectory = stage1.RootDirectory,
            LogMinimumLevel = stage1.LogMinimumLevel,
        };
        var logDir = Path.Combine(stage1.RootDirectory, "logs");
        using var logging = LoggingBootstrap.Initialize(bootstrapSettings, logDir);

        CrashHandler.InstallProcessHooks();

        var buildInfo = OpenTapBuildInfo.Attach(BuildInfo.FromEntryAssembly());
        Log.Information(
            "HardwareTest {InformationalVersion} commit={Commit} runtime={Runtime} rid={Rid} opentap={OpenTap}",
            buildInfo.InformationalVersion,
            buildInfo.CommitSha,
            buildInfo.RuntimeVersion,
            buildInfo.RuntimeIdentifier,
            buildInfo.OpenTapEngineVersion ?? "n/a");

        store.LoadAsync(
            AppSettingsEnvironmentBinder.ReadEnvironment(),
            parsed.Overlays,
            warn: message => Log.Warning("{ConfigWarning}", message)).GetAwaiter().GetResult();

        CrashHandler.Configure(store, buildInfo);
        LogStageDelta(stage1, store);

        if (parsed.PrintConfig)
        {
            Console.Out.Write(ConfigurationBootstrap.FormatPrintConfig(store));
            return 0;
        }

#if DEBUG
        SimulateCrashMode = parsed.SimulateCrash;
        if (!string.IsNullOrWhiteSpace(parsed.SimulateCrash))
        {
            var mode = parsed.SimulateCrash!;
            var isFatal = string.Equals(mode, "fatal", StringComparison.OrdinalIgnoreCase);
            var dir = CrashHandler.Capture(
                new InvalidOperationException($"Simulated crash (--simulate-crash {mode})"),
                isFatal: isFatal,
                source: "simulate-crash");
            var message = dir is null
                ? "simulate-crash: dossier write failed (see logs)."
                : $"simulate-crash: wrote dossier to{Environment.NewLine}  {dir}";
            Console.Out.WriteLine(message);
            Log.Information("{SimulateCrashMessage}", message);
            if (isFatal)
            {
                return 1;
            }

            Console.Out.WriteLine("simulate-crash: launching UI — open Home for the recovery banner.");
        }
#else
        if (!string.IsNullOrWhiteSpace(parsed.SimulateCrash))
        {
            Log.Warning("--simulate-crash is ignored outside DEBUG builds");
        }
#endif

        try
        {
            BuildAvaloniaApp(store)
                .StartWithClassicDesktopLifetime(parsed.PassthroughArgs.ToArray());
            return 0;
        }
        catch (Exception ex)
        {
            CrashHandler.Capture(ex, isFatal: true, source: "Program.Main");
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
            .UseReactiveUI(CrashHandler.ConfigureReactiveUi)
#if DEBUG
            .WithDeveloperTools()
#endif
            ;

    private static void LogStageDelta(ConfigurationBootstrap.Stage1Result stage1, SettingsStore store)
    {
        foreach (var row in store.Provenance)
        {
            if (row.Source == SettingSource.Default)
            {
                continue;
            }

            Log.Debug(
                "Config {Key}={Value} source={Source} detail={Detail}",
                row.Key,
                row.EffectiveValue,
                row.Source,
                row.SourceDetail);
        }

        var stage2Level = store.AppSettings.LogMinimumLevel;
        if (!string.Equals(stage1.LogMinimumLevel, stage2Level, StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug(
                "Stage1→Stage2 log level delta: {Stage1} → {Stage2} (file/env/cli after settings.json)",
                stage1.LogMinimumLevel,
                stage2Level);
        }
    }
}
