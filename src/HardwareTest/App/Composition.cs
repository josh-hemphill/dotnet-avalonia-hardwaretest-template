using HardwareTest.Core;
using HardwareTest.Core.Crash;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
using HardwareTest.Features;
using HardwareTest.Features.Home;
using HardwareTest.Features.Inspect;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;
using HardwareTest.Features.Shell;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Host.Worker;
using HardwareTest.Shell;
using HardwareTest.ShellApps.Notes;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace HardwareTest;

public static class Composition
{
    /// Explicit DI registration — no assembly scanning (AoT-safe host shell).
    public static ServiceProvider Build(ISettingsStore settingsStore)
    {
        var services = new ServiceCollection();
        var buildInfo = OpenTapBuildInfo.Attach(BuildInfo.FromEntryAssembly());
        services.AddSingleton(buildInfo);
        services.AddHardwareTestCore(settingsStore);
        services.AddSingleton<IShellHost>(_ => new ShellHost(settingsStore));
        var launched = ShellApplicationLoader.Load(
            AppContext.BaseDirectory,
            settingsStore.RootDirectory,
            onError: (path, ex) => Log.Warning(ex, "Failed to load shell app package {Path}", path));
        services.AddShellApplications([new NotesApplication(), .. launched]);
        services.AddSingleton(sp =>
            CrashDossierWriter.FromSettings(settingsStore.AppSettings, settingsStore.RootDirectory));
        services.AddSingleton<OpenTapWorkerClient>(sp =>
            new OpenTapWorkerClient(
                sp.GetRequiredService<AppSettings>(),
                Log.Logger,
                sp.GetRequiredService<ISafetyController>(),
                sp.GetRequiredService<IBenchOperationCoordinator>(),
                sp.GetRequiredService<CrashDossierWriter>(),
                sp.GetRequiredService<BuildInfo>(),
                clock: sp.GetRequiredService<IClock>()));
        // Same singleton instance for the aggregate and each focused surface (Phase 14).
        // UI process talks to the killable worker; in-process OpenTapSession is the test-only host.
        services.AddSingleton<IOpenTapSession>(sp => sp.GetRequiredService<OpenTapWorkerClient>());
        services.AddSingleton<IOpenTapPlanSession>(sp => sp.GetRequiredService<OpenTapWorkerClient>());
        services.AddSingleton<IOpenTapRunSession>(sp => sp.GetRequiredService<OpenTapWorkerClient>());
        services.AddSingleton<IOpenTapStationSession>(sp => sp.GetRequiredService<OpenTapWorkerClient>());
        services.AddSingleton<IOpenTapHostCatalog>(sp => sp.GetRequiredService<OpenTapWorkerClient>());
        services.AddSingleton(sp => new OperatorSession(sp.GetRequiredService<IClock>()));

        services.AddSingleton<IViewRegistrar>(_ => ViewRegistry.Shared);
        services.AddSingleton<ShellNotificationViewModel>();
        services.AddSingleton<HomeViewModel>(sp =>
            new HomeViewModel(
                settingsStore,
                sp.GetRequiredService<HardwareTest.Core.Storage.IExportTargetService>(),
                sp.GetRequiredService<ShellNotificationViewModel>(),
                guestTiles: sp.GetServices<IShellApplication>().SelectMany(app => app.HomeTiles)));
        services.AddSingleton<RunTestViewModel>();
        services.AddSingleton<InspectViewModel>();
        services.AddSingleton<ResultsViewModel>();
        services.AddSingleton<ReportPreviewViewModel>();
        services.AddSingleton<InstrumentsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton(sp =>
            new MainWindowViewModel(
                settingsStore,
                sp.GetRequiredService<HomeViewModel>(),
                sp.GetRequiredService<RunTestViewModel>(),
                sp.GetRequiredService<InspectViewModel>(),
                sp.GetRequiredService<ResultsViewModel>(),
                sp.GetRequiredService<ReportPreviewViewModel>(),
                sp.GetRequiredService<InstrumentsViewModel>(),
                sp.GetRequiredService<SettingsViewModel>(),
                sp.GetRequiredService<IRunControl>(),
                sp.GetRequiredService<IOpenTapRunSession>(),
                sp.GetRequiredService<ShellNotificationViewModel>(),
                sp.GetRequiredService<ISafetyController>(),
                extraPages: ShellApplicationComposer.BindPages(
                    sp.GetServices<IShellApplication>(),
                    sp,
                    sp.GetRequiredService<IViewRegistrar>())));
        services.AddSingleton<MainWindow>(sp =>
            new MainWindow(
                sp.GetRequiredService<MainWindowViewModel>(),
                settingsStore,
                sp.GetRequiredService<BuildInfo>()));

        return services.BuildServiceProvider();
    }
}
