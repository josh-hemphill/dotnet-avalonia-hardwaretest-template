using HardwareTest.Core.Settings;
using HardwareTest.Core.StationHealth;
using HardwareTest.Tests.Fixtures;
using HardwareTest.Tests.Time;
using Xunit;

namespace HardwareTest.Tests.StationHealth;

public sealed class StationHealthGateTests
{
    [Fact]
    public void Missing_sidecar_keys_and_health_program_are_off()
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        var settings = new AppSettings();
        var gate = new StationHealthGate(store, settings);
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            StationHealthGateLevels.Off,
            gate.Evaluate(new StationHealthGateRequest { RequireStationHealth = false }, clock).Level);
        Assert.Equal(
            StationHealthGateLevels.Off,
            gate.Evaluate(
                new StationHealthGateRequest
                {
                    ProgramKind = ProgramKinds.StationHealth,
                    RequireStationHealth = true,
                    Gate = StationHealthGates.Block,
                },
                clock).Level);
    }

    [Fact]
    public async Task FakeClock_stale_and_missing_warn_or_block()
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        var settings = new AppSettings();
        var gate = new StationHealthGate(store, settings);
        var measured = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        await store.WriteAsync(new StationHealthRecord
        {
            ProfileId = "default",
            MeasuredAt = measured,
            Verdict = StationHealthVerdicts.Pass,
        });

        var staleClock = new FakeClock(measured.AddHours(29));
        var request = new StationHealthGateRequest
        {
            RequireStationHealth = true,
            Gate = StationHealthGates.Warn,
            MaxAgeHours = 24,
        };
        var warn = gate.Evaluate(request, staleClock);
        Assert.Equal(StationHealthGateLevels.Warn, warn.Level);
        Assert.Contains("29 h", warn.Message, StringComparison.Ordinal);

        var block = gate.Evaluate(request with { Gate = StationHealthGates.Block }, staleClock);
        Assert.Equal(StationHealthGateLevels.Block, block.Level);

        var fresh = gate.Evaluate(request, new FakeClock(measured.AddHours(4)));
        Assert.Equal(StationHealthGateLevels.Ok, fresh.Level);

        var missing = gate.Evaluate(
            request with { ProfileId = "other" },
            staleClock);
        Assert.Equal(StationHealthGateLevels.Warn, missing.Level);
        Assert.Contains("missing", missing.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Override_off_disables_sidecar_block()
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        var settings = new AppSettings { StationHealthGateOverride = StationHealthGateOverrides.Off };
        var gate = new StationHealthGate(store, settings);
        await store.WriteAsync(new StationHealthRecord
        {
            ProfileId = "default",
            MeasuredAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            Verdict = StationHealthVerdicts.Fail,
        });

        var result = gate.Evaluate(
            new StationHealthGateRequest
            {
                RequireStationHealth = true,
                Gate = StationHealthGates.Block,
            },
            new FakeClock(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(StationHealthGateLevels.Off, result.Level);
    }
}
