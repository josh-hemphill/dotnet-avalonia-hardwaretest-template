using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Logging;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
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
        services.AddSingleton(new VisaSessionGate());
        services.AddSingleton<IRunControl>(sp => new RunControl(sp.GetRequiredService<VisaSessionGate>()));
        services.AddSingleton<MeasurementAcquisition>();
        services.AddSingleton<IRunStore>(_ => new FileRunStore(settingsStore.RunsDirectory));
        services.AddSingleton<ISuiteRunStore>(sp =>
            new FileSuiteRunStore(sp.GetRequiredService<IRunStore>(), settingsStore.RunsDirectory));
        services.AddSingleton<IRunComparisonService, StubRunComparisonService>();
        services.AddSingleton<IDutHistoryService>(sp =>
            new DutHistoryService(sp.GetRequiredService<IRunStore>()));
        services.AddSingleton<IVisaSessionFactory>(sp =>
            new ConfigurableVisaSessionFactory(
                settingsStore.AppSettings.UseMockVisa,
                sp.GetRequiredService<VisaSessionGate>()));
        services.AddSingleton<IVisaResourceDiscovery>(_ =>
            new ConfigurableVisaResourceDiscovery(settingsStore.AppSettings.UseMockVisa));
        services.AddSingleton<IReportService>(sp =>
            new TypstReportService(
                sp.GetRequiredService<IRunStore>(),
                settingsStore.AppSettings,
                sp.GetRequiredService<ISuiteRunStore>()));
        services.AddSingleton(Log.Logger);
        return services;
    }
}
