using HardwareTest.Core.Engine;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Host.Worker;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class OpenTapWorkerKillTests
{
    [Fact]
    public async Task Hung_step_kill_timeout_runs_SafeIdle_and_allows_a_second_run()
    {
        using var temp = new TempDataDirectory();
        var safety = new RecordingSafetyController();
        using var client = new OpenTapWorkerClient(
            new AppSettings
            {
                UseMockVisa = true,
                CrashEnabled = false,
                DataDirectory = temp.Path,
            },
            safety: safety,
            killTimeout: TimeSpan.FromMilliseconds(400));

        await client.LoadPlanShapeAsync(PlanShapeFixtures.HangForeverName);
        var runTask = client.RunAsync();
        await WaitUntilAsync(() => client.IsExecuting, TimeSpan.FromSeconds(15));
        client.Abort(safetyStop: true);
        var summary = await runTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(RunResult.Cancelled, summary.Result);
        Assert.True(safety.SafeIdleCount >= 1, "SafeIdle must run before or at worker kill.");

        await client.LoadPlanShapeAsync(PlanShapeFixtures.FlatLeavesName);
        var second = await client.RunAsync().WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(
            second.Result is RunResult.Passed or RunResult.Failed,
            $"Second run after worker kill failed: {second.Result} {second.ErrorMessage}");
        Assert.False(client.IsExecuting);
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

            await Task.Delay(20);
        }
    }

    private sealed class RecordingSafetyController : ISafetyController
    {
        public int SafeIdleCount;

        public bool IsArmed => false;

        public string StatusText => NoOpSafetyController.NotWiredStatus;

        public IReadOnlyList<string> Channels => [];

        public void SafeIdle() => Interlocked.Increment(ref SafeIdleCount);
    }
}
