using HardwareTest.Core.Hardware;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class StationReadinessEvaluatorTests
{
    [Fact]
    public void Unbound_slot_blocks_run()
    {
        var report = StationReadinessEvaluator.Evaluate(
            [new StationSlotSnapshot { SlotName = "DMM", EffectiveResource = "  " }],
            useMockVisa: true);
        Assert.False(report.CanRun);
        Assert.Equal(["DMM"], report.BlockingSlotNames);
        Assert.Equal(StationSlotReadinessKind.Unbound, report.Slots[0].Kind);
        Assert.Contains("unbound", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mock_resource_is_ready_when_mock_visa_is_on()
    {
        var report = StationReadinessEvaluator.Evaluate(
            [new StationSlotSnapshot { SlotName = "DMM", EffectiveResource = "MOCK::INSTR0", TypeName = "MockDmmInstrument" }],
            useMockVisa: true);
        Assert.True(report.CanRun);
        Assert.Equal(StationSlotReadinessKind.Ready, report.Slots[0].Kind);
    }

    [Fact]
    public void Mock_resource_blocks_when_mock_visa_is_off()
    {
        var report = StationReadinessEvaluator.Evaluate(
            [new StationSlotSnapshot { SlotName = "DMM", EffectiveResource = "MOCK::INSTR0" }],
            useMockVisa: false);
        Assert.False(report.CanRun);
        Assert.Equal(StationSlotReadinessKind.DemoOnly, report.Slots[0].Kind);
        Assert.Contains("mock", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ready_slots_allow_run()
    {
        var report = StationReadinessEvaluator.Evaluate(
            [new StationSlotSnapshot { SlotName = "DMM", EffectiveResource = "USB0::INSTR" }],
            useMockVisa: false);
        Assert.True(report.CanRun);
        Assert.Empty(report.BlockingSlotNames);
        Assert.Contains("ready", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_slot_list_is_ready()
    {
        var report = StationReadinessEvaluator.Evaluate([], useMockVisa: false);
        Assert.True(report.CanRun);
        Assert.Contains("No instrument slots", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class StationIdnStoreTests
{
    [Fact]
    public void Upsert_round_trips_without_settings_schema()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hwtest-idn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileStationIdnStore(dir);
            store.Upsert(new StationIdnRecord
            {
                PlanId = "sample",
                SlotName = "DMM",
                Resource = "MOCK::INSTR0",
                IdnRaw = "MOCK,DMM,1,0",
                IdnSummary = "MOCK DMM",
                QueriedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            });

            Assert.True(File.Exists(Path.Combine(dir, FileStationIdnStore.FileName)));
            var found = store.Find("SAMPLE", "dmm");
            Assert.NotNull(found);
            Assert.Equal("MOCK DMM", found.IdnSummary);
            Assert.Equal("MOCK::INSTR0", found.Resource);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
