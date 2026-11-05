using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.Tests.Fixtures;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class LogicalInstrumentBindingsTests
{
    [Fact]
    public async Task Reloaded_full_runs_share_the_acquisition_instrument_with_cleanup_and_repeat_the_same_measurements()
    {
        using var temp = new TempDataDirectory();
        var instrument = new MockDmmInstrument { Name = "DMM", ResourceName = "MOCK::SHARED" };
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new IdentityCheckStep { Instrument = instrument, Dut = new HardwareDut() });
        plan.ChildTestSteps.Add(new AcquireVoltageStep { Instrument = instrument, Channel = "repeatable", SampleCount = 3, IntervalMs = 0 });
        plan.ChildTestSteps.Add(new SafeShutdownStep { Instrument = instrument });
        var path = Path.Combine(temp.Path, "shared.TapPlan");
        plan.Save(path);
        var original = File.ReadAllBytes(path);

        var catalogPlan = InstrumentSlotCollector.CreatePlan(new ProgramCatalogEntry
        {
            Id = "shared",
            DisplayName = "Shared instrument",
            Path = path,
        });
        Assert.Single(InstrumentResourceAccess.CollectFromPlan(catalogPlan));
        var acquire = Assert.IsType<AcquireVoltageStep>(catalogPlan.ChildTestSteps[1]);
        Assert.Same(acquire.Instrument, Assert.IsType<IdentityCheckStep>(catalogPlan.ChildTestSteps[0]).Instrument);
        Assert.Same(acquire.Instrument, Assert.IsType<SafeShutdownStep>(catalogPlan.ChildTestSteps[2]).Instrument);

        var session = new OpenTapSession(new AppSettings { UseMockVisa = true });
        await session.LoadPlanAsync(path);
        Assert.Single(session.InstrumentSlots);
        var first = await session.RunAsync();
        var second = await session.RunAsync();
        Assert.Equal(RunResult.Passed, first.Result);
        Assert.Equal(RunResult.Passed, second.Result);
        var firstValues = first.Samples.Where(sample => sample.Channel == "repeatable").Select(sample => sample.Value).ToArray();
        var secondValues = second.Samples.Where(sample => sample.Channel == "repeatable").Select(sample => sample.Value).ToArray();
        Assert.Equal(3, firstValues.Length);
        Assert.Equal(firstValues, secondValues);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Same_logical_name_with_conflicting_wrapper_configuration_is_rejected_without_discarding_settings()
    {
        using var temp = new TempDataDirectory();
        var assembly = PublishedLibraryFixture.Assembly;
        var plan = new TestPlan();
        foreach (var timeout in new[] { 1000, 2000 })
        {
            var instrument = PublishedLibraryFixture.CreateDmm();
            instrument.Name = "DMM";
            instrument.GetType().GetProperty("IoTimeoutMilliseconds")!.SetValue(instrument, timeout);
            var step = (TestStep)Activator.CreateInstance(assembly.GetType("InstrumentComponents.OpenTap.IdentityQueryStep", true)!)!;
            step.GetType().GetProperty("Instrument")!.SetValue(step, instrument);
            plan.ChildTestSteps.Add(step);
        }
        var path = Path.Combine(temp.Path, "conflicting.TapPlan");
        plan.Save(path);
        var original = File.ReadAllBytes(path);
        var session = new OpenTapSession(new AppSettings { UseMockVisa = true });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => session.LoadPlanAsync(path));
        Assert.Contains("conflicting", error.Message, StringComparison.Ordinal);
        Assert.Null(session.LoadedPlanPath);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void Different_logical_slots_remain_distinct_even_at_the_same_address()
    {
        using var temp = new TempDataDirectory();
        var plan = new TestPlan();
        foreach (var name in new[] { "DMM-A", "DMM-B" })
            plan.ChildTestSteps.Add(new AcquireVoltageStep
            {
                Instrument = new MockDmmInstrument { Name = name, ResourceName = "MOCK::SAME" },
            });
        var path = Path.Combine(temp.Path, "distinct.TapPlan");
        plan.Save(path);
        var loaded = InstrumentSlotCollector.CreatePlan(new ProgramCatalogEntry { Id = "distinct", DisplayName = "Distinct", Path = path });
        Assert.Equal(2, InstrumentResourceAccess.CollectFromPlan(loaded).Count());
    }
}
