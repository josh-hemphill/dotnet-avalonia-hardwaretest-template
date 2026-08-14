using System.Diagnostics;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Serilog;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

/// Phase 24: two in-process run contexts do not share pause/interaction.
/// Does not call <c>TestPlan.Execute</c> (that stays in <c>OpenTapSerial</c>).
public sealed class OpenTapRunContextTests
{
    [Fact]
    public async Task Two_run_contexts_do_not_share_pause_or_interaction()
    {
        using var a = CreateContext();
        using var b = CreateContext();
        a.Control.BeginRun(CancellationToken.None, startPaused: false);
        b.Control.BeginRun(CancellationToken.None, startPaused: false);

        a.Pause();
        var sw = Stopwatch.StartNew();
        b.WaitIfPaused();
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(250), $"context B WaitIfPaused blocked ({sw.Elapsed})");

        var blocked = Task.Run(() => a.WaitIfPaused());
        var finishedEarly = await Task.WhenAny(blocked, Task.Delay(80)) == blocked;
        Assert.False(finishedEarly);
        a.Resume();
        await blocked.WaitAsync(TimeSpan.FromSeconds(2));

        var request = OperatorInteractionRequest.ConfirmOnly("context A");
        var waiting = Task.Run(() => a.Control.HandleInteraction(request));
        await WaitUntilAsync(() => a.IsAwaitingOperator, TimeSpan.FromSeconds(2));
        Assert.False(b.IsAwaitingOperator);
        b.WaitIfPaused();
        a.Resume(OperatorInteractionResponse.Continue(request.Id));
        var response = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(response.Cancelled);
        Assert.False(a.IsAwaitingOperator);
    }

    private static OpenTapRunContext CreateContext()
        => new(new AppSettings(), new LoggerConfiguration().MinimumLevel.Fatal().CreateLogger());

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = Stopwatch.StartNew();
        while (!condition())
        {
            if (start.Elapsed > timeout)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }
}
