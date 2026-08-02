using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HardwareTest.Core.Crash;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Crash;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace HardwareTest;

public partial class App : Application
{
    private readonly ISettingsStore _settingsStore;
    private ServiceProvider? _services;

    /// Optional factory used by the parameterless ctor (headless / designer hooks).
    public static Func<ISettingsStore>? SettingsStoreFactory { get; set; }

    public App()
        : this(SettingsStoreFactory?.Invoke() ?? new SettingsStore())
    {
    }

    public App(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public ISettingsStore SettingsStore => _settingsStore;

    public ServiceProvider Services =>
        _services ?? throw new InvalidOperationException("DI container is not built yet.");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeApplier.Apply(_settingsStore.AppSettings);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _services = Composition.Build(_settingsStore);
        ThemeApplier.Apply(_settingsStore.AppSettings);

        var buildInfo = _services.GetRequiredService<BuildInfo>();
        var openTap = _services.GetRequiredService<IOpenTapSession>();
        var runControl = _services.GetRequiredService<IRunControl>();
        var session = _services.GetRequiredService<OperatorSession>();
        var runTest = _services.GetRequiredService<RunTestViewModel>();

        CrashHandler.Configure(
            _settingsStore,
            buildInfo,
            safeStop: () =>
            {
                try
                {
                    runControl.RequestSafetyStop();
                    openTap.Abort(safetyStop: true);
                    return SafeStopOutcome.Confirmed;
                }
                catch
                {
                    return SafeStopOutcome.Failed;
                }
            },
            sessionSnapshot: () =>
            (
                runTest.LastRunId,
                session.ProgramId,
                session.DutSerial,
                session.OperatorName,
                !string.IsNullOrWhiteSpace(session.DutSerial),
                session.ProgramId,
                _settingsStore.AppSettings.IsEngineerDebugMode
            ));
        CrashHandler.InstallUiHooks();

        try
        {
            var crashRoot = string.IsNullOrWhiteSpace(_settingsStore.AppSettings.CrashDirectory)
                ? Path.Combine(_settingsStore.RootDirectory, "crashes")
                : _settingsStore.AppSettings.CrashDirectory;
            var dossierId = DanglingRunReconciler.TryCorrelateNewestDossierId(crashRoot, TimeSpan.FromHours(24));
            var reconciler = new DanglingRunReconciler(_services.GetRequiredService<IRunStore>());
            // Must not block the Avalonia UI thread on async I/O (SyncContext deadlock).
            var n = Task.Run(() => reconciler.ReconcileAsync(dossierId)).GetAwaiter().GetResult();
            if (n > 0)
            {
                Log.Information("Reconciled {Count} dangling run(s) on startup", n);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Dangling run reconciliation failed");
        }

        try
        {
            var retention = _services.GetRequiredService<HardwareTest.Core.Storage.IRunRetentionService>();
            var pruned = retention.Prune();
            if (pruned.DeletedCount > 0)
            {
                Log.Information("Run retention removed {Count} folder(s)", pruned.DeletedCount);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Run retention pass failed");
        }

        try
        {
            var health = _services.GetRequiredService<HardwareTest.Core.Storage.IStorageHealthService>()
                .GetDataVolumeHealth();
            if (health.Level != HardwareTest.Core.Storage.StorageHealthLevel.Ok)
            {
                Log.Warning("Storage health {Level}: {Message}", health.Level, health.Message);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Storage health snapshot failed");
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += async (_, _) =>
            {
                try
                {
                    await _settingsStore.SaveUiStateAsync();
                    await _settingsStore.SaveAppSettingsAsync();
                }
                catch
                {
                    // best effort on shutdown
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
