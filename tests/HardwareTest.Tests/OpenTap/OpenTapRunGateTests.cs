using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class OpenTapRunGateTests
{
    private static async Task<OpenTapSession> LoadAsync(string fixture)
    {
        var session = new OpenTapSession();
        await session.LoadPlanShapeAsync(fixture);
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-GATE", Family: "demo"));
        return session;
    }

    [Fact]
    public async Task Concurrent_RunAsync_rejects_second_call()
    {
        var session = await LoadAsync(PlanShapeFixtures.FlatLeavesName);
        session.Pause(); // Hold the first run inside WaitIfPaused so the gate stays taken.
        var first = session.RunAsync();
        await WaitUntilAsync(() => session.IsExecuting, TimeSpan.FromSeconds(2));

        var secondEx = await Record.ExceptionAsync(() => session.RunAsync());
        session.Resume();
        await first;

        Assert.IsType<InvalidOperationException>(secondEx);
        Assert.Contains("already in progress", secondEx!.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Step_enable_is_rejected_while_running()
    {
        var session = await LoadAsync(PlanShapeFixtures.SweepRepeatName);
        var run = session.RunAsync();
        var accepted = 0;
        for (var i = 0; i < 50 && !run.IsCompleted; i++)
        {
            foreach (var node in session.StepTree)
            {
                if (session.TrySetStepEnabled(node.Path, false))
                {
                    accepted++;
                }
            }

            await Task.Delay(5);
        }

        await run;
        Assert.Equal(0, accepted);
    }

    [Fact]
    public async Task RunAsync_refused_when_coordinator_holds_id_query()
    {
        var bench = new BenchOperationCoordinator();
        var session = new OpenTapSession(bench: bench);
        Assert.True(bench.TryEnter(BenchOperation.IdQuery, out var lease, out _));
        using (lease)
        {
            var ex = await Record.ExceptionAsync(() => session.RunAsync());
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("Instruments query", ex!.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RunAsync_releases_coordinator_when_finished()
    {
        var bench = new BenchOperationCoordinator();
        var session = new OpenTapSession(bench: bench);
        await session.LoadPlanShapeAsync(PlanShapeFixtures.FlatLeavesName);
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-GATE", Family: "demo"));

        await session.RunAsync();

        Assert.Null(bench.Current);
        Assert.True(bench.TryEnter(BenchOperation.ModeSwap, out var lease, out _));
        lease!.Dispose();
    }
}
