using HardwareTest.Core;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Settings;
using HardwareTest.Features;
using HardwareTest.Features.Home;
using HardwareTest.Features.Inspect;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;
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
        services.AddSingleton<IOpenTapSession>(sp =>
            new OpenTapSession(sp.GetRequiredService<AppSettings>(), Log.Logger));
        services.AddSingleton<OperatorSession>();

        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<RunTestViewModel>();
        services.AddSingleton<InspectViewModel>();
        services.AddSingleton<ResultsViewModel>();
        services.AddSingleton<ReportPreviewViewModel>();
        services.AddSingleton<InstrumentsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>(sp =>
        {
            var mainVm = sp.GetRequiredService<MainWindowViewModel>();
            var results = sp.GetRequiredService<ResultsViewModel>();
            var preview = sp.GetRequiredService<ReportPreviewViewModel>();
            results.ReportOpened += async (_, path) =>
            {
                mainVm.NavigateToPageId("ReportPreview");
                await preview.LoadFromPathAsync(path);
            };
            return new MainWindow(mainVm, settingsStore, sp.GetRequiredService<BuildInfo>());
        });

        return services.BuildServiceProvider();
    }
}
