using HardwareTest.Core;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Plans;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Xunit;

namespace HardwareTest.Tests.Di;

public sealed class CoreCompositionTests
{
    [Fact]
    public async Task AddHardwareTestCore_resolves_required_services()
    {
        Log.Logger = new LoggerConfiguration().CreateLogger();
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync();
        store.AppSettings.UseMockVisa = true;

        var services = new ServiceCollection();
        services.AddHardwareTestCore(store);
        await using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IVisaSessionFactory>());
        Assert.NotNull(sp.GetRequiredService<ITestEngine>());
        Assert.NotNull(sp.GetRequiredService<IPlanLoader>());
        Assert.NotNull(sp.GetRequiredService<IRunStore>());
        Assert.NotNull(sp.GetRequiredService<IReportService>());
        Assert.NotNull(sp.GetRequiredService<ISuiteEngine>());
        Assert.NotNull(sp.GetRequiredService<ISuiteLoader>());
        Assert.NotNull(sp.GetRequiredService<IVisaResourceDiscovery>());
        Assert.NotNull(sp.GetRequiredService<VisaSessionGate>());
        Assert.NotNull(sp.GetRequiredService<MeasurementAcquisition>());
    }
}
