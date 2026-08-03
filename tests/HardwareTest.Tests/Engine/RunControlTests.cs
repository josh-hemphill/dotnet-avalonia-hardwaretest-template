using HardwareTest.Core.Hardware;
using HardwareTest.Core.Engine;
using Xunit;

namespace HardwareTest.Tests.Engine;

public sealed class RunControlTests
{
    [Fact]
    public void Pause_and_resume_toggle_flags()
    {
        var gate = new VisaSessionGate();
        var control = new RunControl(gate);
        using var cts = new CancellationTokenSource();
        control.AttachRun(cts);
        Assert.True(control.IsRunning);

        control.Pause();
        Assert.True(control.IsPaused);

        control.Resume();
        Assert.False(control.IsPaused);

        control.DetachRun();
        Assert.False(control.IsRunning);
    }

    [Fact]
    public void RequestSafetyStop_cancels_and_marks_flag()
    {
        var gate = new VisaSessionGate();
        var control = new RunControl(gate);
        using var cts = new CancellationTokenSource();
        control.AttachRun(cts);

        control.RequestSafetyStop();
        Assert.True(control.WasSafetyStopRequested);
        Assert.True(control.IsSafetyStopping);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task WaitIfPausedAsync_unblocks_on_resume()
    {
        var gate = new VisaSessionGate();
        var control = new RunControl(gate);
        using var cts = new CancellationTokenSource();
        control.AttachRun(cts);
        control.Pause();

        var wait = control.WaitIfPausedAsync(cts.Token);
        await Task.Delay(30);
        Assert.False(wait.IsCompleted);
        control.Resume();
        await wait;
    }
}
