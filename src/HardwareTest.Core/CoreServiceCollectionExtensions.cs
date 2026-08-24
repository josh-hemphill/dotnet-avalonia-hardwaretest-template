using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Logging;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.Core.Time;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace HardwareTest.Core;

public static class CoreServiceCollectionExtensions
{
    /// Registers Core services with explicit DI (no assembly scanning).
    public static IServiceCollection AddHardwareTestCore(
        this IServiceCollection services,
        ISettingsStore settingsStore)
    {
        services.AddSingleton(settingsStore);
        services.AddSingleton(settingsStore.AppSettings);
        services.AddSingleton(settingsStore.UiState);
        services.AddSingleton<IClock>(_ => SystemClock.Instance);
        services.AddSingleton<INtpTimeSource, UdpNtpTimeSource>();
        services.AddSingleton<IClockSkewDetector>(sp =>
            new ClockSkewDetector(
                settingsStore.AppSettings,
                sp.GetRequiredService<IClock>(),
                settingsStore.RootDirectory,
                sp.GetRequiredService<INtpTimeSource>(),
                Log.Logger));
        services.AddSingleton(new VisaSessionGate());
        services.AddSingleton<IBenchOperationCoordinator, BenchOperationCoordinator>();
        services.AddSingleton<IRunControl>(sp => new RunControl(sp.GetRequiredService<VisaSessionGate>()));
        services.AddSingleton<ISafetyController, NoOpSafetyController>();
        services.AddSingleton<MeasurementAcquisition>();
        services.AddSingleton<IRunStore>(_ => new FileRunStore(settingsStore.RunsDirectory));
        services.AddSingleton<ISuiteRunStore>(sp =>
            new FileSuiteRunStore(sp.GetRequiredService<IRunStore>(), settingsStore.RunsDirectory));
        services.AddSingleton<IRunComparisonService>(sp =>
            new RunComparisonService(sp.GetRequiredService<IRunStore>()));
        services.AddSingleton<IDutHistoryService>(sp =>
            new DutHistoryService(sp.GetRequiredService<IRunStore>()));
        services.AddSingleton<IStationIdnStore>(_ =>
            new FileStationIdnStore(settingsStore.RootDirectory, Log.Logger));
        services.AddSingleton<VisaModeController>(sp =>
            new VisaModeController(
                settingsStore.AppSettings.UseMockVisa,
                sp.GetRequiredService<VisaSessionGate>(),
                sp.GetRequiredService<IRunControl>(),
                message => Log.Warning("{Message}", message),
                bench: sp.GetRequiredService<IBenchOperationCoordinator>()));
        services.AddSingleton<IVisaModeController>(sp => sp.GetRequiredService<VisaModeController>());
        services.AddSingleton<IVisaSessionFactory>(sp => sp.GetRequiredService<VisaModeController>());
        services.AddSingleton<IVisaBroker>(sp => sp.GetRequiredService<VisaModeController>());
        services.AddSingleton<IVisaResourceDiscovery>(sp => sp.GetRequiredService<VisaModeController>());
        services.AddSingleton<IReportService>(sp =>
            new TypstReportService(
                sp.GetRequiredService<IRunStore>(),
                settingsStore.AppSettings,
                sp.GetRequiredService<ISuiteRunStore>()));
        services.AddSingleton<IStorageHealthService>(_ =>
            new StorageHealthService(settingsStore.AppSettings, settingsStore.RootDirectory));
        services.AddSingleton<IRunRetentionService>(sp =>
            new RunRetentionService(
                settingsStore.AppSettings,
                settingsStore.RunsDirectory,
                Log.Logger,
                sp.GetRequiredService<IClock>()));
        services.AddSingleton<IExportTargetService>(_ =>
            new ExportTargetService(settingsStore.AppSettings, settingsStore.RootDirectory, Log.Logger));
        services.AddSingleton(Log.Logger);
        return services;
    }
}
