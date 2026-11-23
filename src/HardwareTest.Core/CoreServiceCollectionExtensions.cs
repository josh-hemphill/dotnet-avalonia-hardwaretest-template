using HardwareTest.Core.Credentials;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Logging;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.StationHealth;
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
        _ = services.AddSingleton(settingsStore);
        _ = services.AddSingleton(settingsStore.AppSettings);
        _ = services.AddSingleton(settingsStore.UiState);
        _ = services.AddSingleton<IClock>(_ => SystemClock.Instance);
        _ = services.AddSingleton<INtpTimeSource, UdpNtpTimeSource>();
        _ = services.AddSingleton<IClockSkewDetector>(sp =>
            new ClockSkewDetector(
                settingsStore.AppSettings,
                sp.GetRequiredService<IClock>(),
                settingsStore.RootDirectory,
                sp.GetRequiredService<INtpTimeSource>(),
                Log.Logger));
        _ = services.AddSingleton(new VisaSessionGate());
        _ = services.AddSingleton<IBenchOperationCoordinator, BenchOperationCoordinator>();
        _ = services.AddSingleton<IRunControl>(sp => new RunControl(sp.GetRequiredService<VisaSessionGate>()));
        _ = services.AddSingleton<ISafetyController, NoOpSafetyController>();
        _ = services.AddSingleton<MeasurementAcquisition>();
        _ = services.AddSingleton<IRunStore>(_ => new FileRunStore(settingsStore.RunsDirectory));
        _ = services.AddSingleton<ISuiteRunStore>(sp =>
            new FileSuiteRunStore(sp.GetRequiredService<IRunStore>(), settingsStore.RunsDirectory));
        _ = services.AddSingleton<IRunComparisonService>(sp =>
            new RunComparisonService(sp.GetRequiredService<IRunStore>()));
        _ = services.AddSingleton<IDutHistoryService>(sp =>
            new DutHistoryService(sp.GetRequiredService<IRunStore>()));
        _ = services.AddSingleton<IStationIdnStore>(_ =>
            new FileStationIdnStore(settingsStore.RootDirectory, Log.Logger));
        _ = services.AddSingleton<IStationHealthStore>(_ =>
            new FileStationHealthStore(
                string.IsNullOrWhiteSpace(settingsStore.AppSettings.DataDirectory)
                    ? settingsStore.RootDirectory
                    : settingsStore.AppSettings.DataDirectory));
        _ = services.AddSingleton<IStationHealthGate>(sp =>
            new StationHealthGate(
                sp.GetRequiredService<IStationHealthStore>(),
                settingsStore.AppSettings));
        _ = services.AddSingleton(sp =>
            new VisaModeController(
                settingsStore.AppSettings.UseMockVisa,
                sp.GetRequiredService<VisaSessionGate>(),
                sp.GetRequiredService<IRunControl>(),
                message => Log.Warning("{Message}", message),
                bench: sp.GetRequiredService<IBenchOperationCoordinator>()));
        _ = services.AddSingleton<IVisaModeController>(sp => sp.GetRequiredService<VisaModeController>());
        _ = services.AddSingleton<IVisaSessionFactory>(sp => sp.GetRequiredService<VisaModeController>());
        _ = services.AddSingleton<IVisaBroker>(sp => sp.GetRequiredService<VisaModeController>());
        _ = services.AddSingleton<IVisaResourceDiscovery>(sp => sp.GetRequiredService<VisaModeController>());
        _ = services.AddSingleton<IOperatorCredentialPresenceBroker>(sp =>
            new PcscOperatorCredentialBroker(sp.GetRequiredService<IClock>()));
        _ = services.AddSingleton<IOperatorCredentialBroker>(sp =>
            new SettingsBackedCredentialBroker(
                settingsStore.AppSettings,
                new MockOperatorCredentialBroker(sp.GetRequiredService<IClock>()),
                new SmartCardSigningRouter(
                    settingsStore.AppSettings,
                    sp.GetService<INativeSigningDialogOwner>(),
                    sp.GetRequiredService<IClock>(),
                    sp.GetRequiredService<IOperatorCredentialPresenceBroker>())));
        _ = services.AddSingleton<IReportService>(sp =>
            new TypstReportService(
                sp.GetRequiredService<IRunStore>(),
                settingsStore.AppSettings,
                sp.GetRequiredService<ISuiteRunStore>()));
        _ = services.AddSingleton<IReportAttestationService>(sp =>
            new ReportAttestationService(
                sp.GetRequiredService<IOperatorCredentialBroker>(),
                sp.GetRequiredService<IRunStore>(),
                settingsStore.AppSettings,
                sp.GetRequiredService<IClock>(),
                new Lazy<IReportService>(sp.GetRequiredService<IReportService>)));
        _ = services.AddSingleton<IStorageHealthService>(_ =>
            new StorageHealthService(settingsStore.AppSettings, settingsStore.RootDirectory));
        _ = services.AddSingleton<IRunRetentionService>(sp =>
            new RunRetentionService(
                settingsStore.AppSettings,
                settingsStore.RunsDirectory,
                Log.Logger,
                sp.GetRequiredService<IClock>()));
        _ = services.AddSingleton<IExportTargetService>(_ =>
            new ExportTargetService(settingsStore.AppSettings, settingsStore.RootDirectory, Log.Logger));
        _ = services.AddSingleton(Log.Logger);
        return services;
    }
}
