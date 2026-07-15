using HardwareTest.Core.Hardware;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class VisaSessionGateTests
{
    [Fact]
    public async Task Parallel_writes_through_tracing_gate_never_overlap()
    {
        var gate = new VisaSessionGate();
        var spy = new SpyVisaSession(writeDelay: TimeSpan.FromMilliseconds(50));
        IVisaSession session = new TracingVisaSession(spy, gate);

        var tasks = Enumerable.Range(0, 8)
            .Select(i => session.WriteAsync($"CMD{i}"))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, spy.MaxActive);
        Assert.Equal(8, spy.Log.Count);
    }

    [Fact]
    public async Task Second_caller_waits_until_first_releases()
    {
        var gate = new VisaSessionGate();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();

        var first = gate.RunAsync(
            async _ =>
            {
                order.Add("first-enter");
                await tcs.Task;
                order.Add("first-exit");
            },
            CancellationToken.None);

        await Task.Delay(20);
        var secondStarted = false;
        var second = gate.RunAsync(
            _ =>
            {
                secondStarted = true;
                order.Add("second");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await Task.Delay(30);
        Assert.False(secondStarted);

        tcs.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(["first-enter", "first-exit", "second"], order);
    }

    [Fact]
    public async Task Waiting_caller_can_be_cancelled()
    {
        var gate = new VisaSessionGate();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var holder = gate.RunAsync(async _ => await release.Task, CancellationToken.None);
        await Task.Delay(20);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => gate.RunAsync(_ => Task.CompletedTask, cts.Token));

        release.SetResult();
        await holder;
    }
}
