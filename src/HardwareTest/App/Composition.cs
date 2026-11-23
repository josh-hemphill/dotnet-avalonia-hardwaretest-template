using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using HardwareTest.Core;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Crash;
using HardwareTest.Core.Credentials;
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
using HardwareTest.Reporting;
using HardwareTest.Reporting.NativePrinting;
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
        _ = services.AddSingleton(buildInfo);
        _ = services.AddHardwareTestCore(settingsStore);
        _ = services.AddSingleton<IBenchReferenceStore>(sp => new FileBenchReferenceStore(settingsStore.RootDirectory, sp.GetRequiredService<IClock>()));
        _ = services.AddSingleton<IShellHost>(_ => new ShellHost(settingsStore));
        _ = services.AddShellApplications(new NotesApplication());
        _ = services.AddSingleton(sp =>
            CrashDossierWriter.FromSettings(settingsStore.AppSettings, settingsStore.RootDirectory));
        _ = services.AddSingleton(sp =>
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
        _ = services.AddSingleton<IOpenTapSession>(sp => sp.GetRequiredService<OpenTapWorkerClient>());
        _ = services.AddSingleton<IOpenTapPlanSession>(sp => sp.GetRequiredService<OpenTapWorkerClient>());
        _ = services.AddSingleton<IOpenTapRunSession>(sp => sp.GetRequiredService<OpenTapWorkerClient>());
        _ = services.AddSingleton<IOpenTapStationSession>(sp => sp.GetRequiredService<OpenTapWorkerClient>());
        _ = services.AddSingleton<IOpenTapHostCatalog>(sp => sp.GetRequiredService<OpenTapWorkerClient>());
        _ = services.AddSingleton(sp => new OperatorSession(sp.GetRequiredService<IClock>()));

        _ = services.AddSingleton<IViewRegistrar>(_ => ViewRegistry.Shared);
        _ = services.AddSingleton<ShellNotificationViewModel>();
        _ = services.AddSingleton(sp =>
            new HomeViewModel(
                settingsStore,
                sp.GetRequiredService<Core.Storage.IExportTargetService>(),
                sp.GetRequiredService<ShellNotificationViewModel>(),
                guestTiles: sp.GetServices<IShellApplication>().SelectMany(app => app.HomeTiles)));
        _ = services.AddSingleton<RunTestViewModel>();
        _ = services.AddSingleton<InspectViewModel>();
        _ = services.AddSingleton<ResultsViewModel>();
        _ = services.AddSingleton(_ => new NativeDialogOwner(
            () => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow));
        _ = services.AddSingleton<INativeSigningDialogOwner>(sp => sp.GetRequiredService<NativeDialogOwner>());
        _ = services.AddSingleton<IReportPrintService>(sp => OperatingSystem.IsWindows()
            ? new WindowsReportPrintService(sp.GetRequiredService<NativeDialogOwner>().GetHandleAsync,
                new WindowsPrintBackend(), new PdfPrintRenderer(), new StaPrintWorker())
            : new LpReportPrintService(new SystemLpPrintBackend()));
        _ = services.AddSingleton<IReportDesktopActions>(sp => new ReportDesktopActions(
            () => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow,
            settingsStore.RunsDirectory,
            OperatingSystem.IsWindows() ? new PdfViewerLauncher(sp.GetRequiredService<NativeDialogOwner>().GetHandleAsync,
                new WindowsPdfViewerBackend(), new StaPrintWorker()) : null));
        _ = services.AddSingleton<ReportPreviewViewModel>();
        _ = services.AddSingleton<InstrumentsViewModel>();
        _ = services.AddSingleton<SettingsViewModel>();
        _ = services.AddSingleton(sp =>
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
        _ = services.AddSingleton(sp =>
            new MainWindow(
                sp.GetRequiredService<MainWindowViewModel>(),
                settingsStore,
                sp.GetRequiredService<BuildInfo>()));

        return services.BuildServiceProvider();
    }
}
