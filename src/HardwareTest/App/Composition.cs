using HardwareTest.Core;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
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
        services.AddSingleton<OpenTapSession>(sp =>
            new OpenTapSession(
                sp.GetRequiredService<AppSettings>(),
                Log.Logger,
                sp.GetRequiredService<IVisaBroker>(),
                sp.GetRequiredService<IBenchOperationCoordinator>()));
        // Same singleton instance for the aggregate and each focused surface (Phase 14).
        services.AddSingleton<IOpenTapSession>(sp => sp.GetRequiredService<OpenTapSession>());
        services.AddSingleton<IOpenTapPlanSession>(sp => sp.GetRequiredService<OpenTapSession>());
        services.AddSingleton<IOpenTapRunSession>(sp => sp.GetRequiredService<OpenTapSession>());
        services.AddSingleton<IOpenTapStationSession>(sp => sp.GetRequiredService<OpenTapSession>());
        services.AddSingleton<IOpenTapHostCatalog>(sp => sp.GetRequiredService<OpenTapSession>());
        services.AddSingleton<OperatorSession>();

        services.AddSingleton<ShellNotificationViewModel>();
        services.AddSingleton<HomeViewModel>(sp =>
            new HomeViewModel(
                settingsStore,
                sp.GetRequiredService<HardwareTest.Core.Storage.IExportTargetService>(),
                sp.GetRequiredService<ShellNotificationViewModel>()));
        services.AddSingleton<RunTestViewModel>();
        services.AddSingleton<InspectViewModel>();
        services.AddSingleton<ResultsViewModel>();
        services.AddSingleton<ReportPreviewViewModel>();
        services.AddSingleton<InstrumentsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>(sp =>
            new MainWindow(
                sp.GetRequiredService<MainWindowViewModel>(),
                settingsStore,
                sp.GetRequiredService<BuildInfo>()));

        return services.BuildServiceProvider();
    }
}
