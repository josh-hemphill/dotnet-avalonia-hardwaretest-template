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

    [Fact]
    public async Task Cancelling_run_token_does_not_abandon_ipc_or_kill_the_next_run()
    {
        using var temp = new TempDataDirectory();
        var safety = new RecordingSafetyController();
        var killTimeout = TimeSpan.FromMilliseconds(800);
        using var client = new OpenTapWorkerClient(
            new AppSettings
            {
                UseMockVisa = true,
                CrashEnabled = false,
                DataDirectory = temp.Path,
            },
            safety: safety,
            killTimeout: killTimeout);

        await client.LoadPlanShapeAsync(PlanShapeFixtures.HangForeverName);
        using var cts = new CancellationTokenSource();
        var first = client.RunAsync(cancellationToken: cts.Token);
        await WaitUntilAsync(() => client.IsExecuting, TimeSpan.FromSeconds(15));
        cts.Cancel();
        client.Abort(safetyStop: true);

        var firstError = await Record.ExceptionAsync(() => first.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Null(firstError);
        var firstSummary = await first;
        Assert.Equal(RunResult.Cancelled, firstSummary.Result);
        Assert.False(client.IsExecuting);

        await client.LoadPlanShapeAsync(PlanShapeFixtures.HangForeverName);
        var second = client.RunAsync();
        await WaitUntilAsync(() => client.IsExecuting, TimeSpan.FromSeconds(15));
        await Task.Delay(killTimeout + TimeSpan.FromMilliseconds(400));
        Assert.True(
            client.IsExecuting,
            "A kill timer armed by the cancelled first run must not tear down the next run.");

        client.Abort(safetyStop: true);
        var secondSummary = await second.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(RunResult.Cancelled, secondSummary.Result);
        Assert.False(client.IsExecuting);
    }

    [Fact]
    public async Task Run_without_plan_surfaces_worker_error_without_killing_process()
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
            killTimeout: TimeSpan.FromSeconds(30));

        var error = await Record.ExceptionAsync(() => client.RunAsync());
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("plan", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("terminated", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, safety.SafeIdleCount);
        Assert.False(client.IsExecuting);

        await client.LoadPlanShapeAsync(PlanShapeFixtures.FlatLeavesName);
        var summary = await client.RunAsync().WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(
            summary.Result is RunResult.Passed or RunResult.Failed,
            $"Worker was not reusable after a run error: {summary.Result} {summary.ErrorMessage}");
        Assert.Equal(0, safety.SafeIdleCount);
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
