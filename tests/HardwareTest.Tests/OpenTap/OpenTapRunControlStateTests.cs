using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

public sealed class OpenTapRunControlStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_teardown_cancels_an_outstanding_request_and_clears_operator_state(bool dispose)
    {
        using var control = new OpenTapRunControlState();
        control.BeginRun(CancellationToken.None);
        var request = OperatorInteractionRequest.ConfirmOnly("Outstanding request");
        control.BeginPendingInteraction(request);
        var waiting = Task.Run(() => control.WaitForInteractionResponse(request.Id));

        if (dispose) control.Dispose();
        else control.CompleteRun();

        var response = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(request.Id, response.RequestId);
        Assert.True(response.Cancelled);
        Assert.False(control.IsAwaitingOperator);
        Assert.Null(control.PendingInteraction);
        Assert.Null(control.OperatorPromptMessage);
        Assert.False(control.IsPaused);
        Assert.True(control.WaitPauseGate(0));
    }

    [Fact]
    public async Task Reentrant_next_request_preserves_the_completed_request_response_and_its_own_gates()
    {
        using var control = new OpenTapRunControlState();
        var first = OperatorInteractionRequest.ConfirmOnly("First request");
        var next = OperatorInteractionRequest.ConfirmOnly("Next request");
        var started = false;
        control.OperatorStateChanged += () =>
        {
            if (control.PendingInteraction?.Id == first.Id)
                control.Resume(OperatorInteractionResponse.Continue(first.Id));
            else if (!started && !control.IsAwaitingOperator)
            {
                started = true;
                control.BeginPendingInteraction(next);
            }
        };

        var firstResponse = await Task.Run(() => control.HandleInteraction(first)).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(first.Id, firstResponse.RequestId);
        Assert.False(firstResponse.Cancelled);
        Assert.Same(next, control.PendingInteraction);
        Assert.True(control.IsAwaitingOperator);
        Assert.True(control.IsPaused);
        Assert.False(control.WaitPauseGate(0));
        var nextWaiting = Task.Run(() => control.WaitForInteractionResponse(next.Id));
        Assert.NotSame(nextWaiting, await Task.WhenAny(nextWaiting, Task.Delay(50)));

        // A duplicate completion cannot release or overwrite the next request.
        control.Resume(firstResponse);
        Assert.Same(next, control.PendingInteraction);
        Assert.False(control.WaitPauseGate(0));
        Assert.NotSame(nextWaiting, await Task.WhenAny(nextWaiting, Task.Delay(50)));
        control.Resume(OperatorInteractionResponse.Continue(next.Id));
        var nextResponse = await nextWaiting.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(next.Id, nextResponse.RequestId);
        Assert.False(nextResponse.Cancelled);
        Assert.False(control.IsAwaitingOperator);
        Assert.False(control.IsPaused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_notification_can_start_another_interaction_without_reopening_its_gates(bool abort)
    {
        using var control = new OpenTapRunControlState();
        var first = OperatorInteractionRequest.ConfirmOnly("First request");
        var next = OperatorInteractionRequest.ConfirmOnly("Next request");
        control.BeginPendingInteraction(first);
        var started = false;
        control.OperatorStateChanged += () =>
        {
            if (started || control.IsAwaitingOperator) return;
            started = true;
            control.BeginPendingInteraction(next);
        };

        // No active run: Abort tests the notification transition independently of
        // cancellation, which intentionally releases the gates of a cancelled run.
        if (abort) control.Abort();
        else control.Resume();

        Assert.True(started);
        Assert.Same(next, control.PendingInteraction);
        Assert.True(control.IsAwaitingOperator);
        Assert.True(control.IsPaused);
        Assert.False(control.WaitPauseGate(0));
        var firstResponse = control.WaitForInteractionResponse(first.Id);
        Assert.Equal(first.Id, firstResponse.RequestId);
        Assert.Equal(abort, firstResponse.Cancelled);
        var nextWaiting = Task.Run(() => control.WaitForInteractionResponse(next.Id));
        Assert.NotSame(nextWaiting, await Task.WhenAny(nextWaiting, Task.Delay(50)));
        control.Resume();
        var nextResponse = await nextWaiting.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(next.Id, nextResponse.RequestId);
        Assert.False(nextResponse.Cancelled);
    }

    [Fact]
    public async Task Pending_notification_can_resume_immediately_and_leave_execution_unpaused()
    {
        using var control = new OpenTapRunControlState();
        control.BeginRun(CancellationToken.None);
        var request = OperatorInteractionRequest.ConfirmOnly("Immediate continue");
        control.OperatorStateChanged += () =>
        {
            if (control.IsAwaitingOperator)
                control.Resume(OperatorInteractionResponse.Continue(control.PendingInteraction!.Id));
        };

        var response = await Task.Run(() => control.HandleInteraction(request)).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(response.Cancelled);
        Assert.Equal(request.Id, response.RequestId);
        await Task.Run(control.WaitIfPaused).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(control.IsAwaitingOperator);
        Assert.False(control.IsPaused);
    }

    [Fact]
    public async Task Concurrent_polling_resume_does_not_lose_the_pause_release()
    {
        using var control = new OpenTapRunControlState();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        control.BeginRun(timeout.Token);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var request = OperatorInteractionRequest.ConfirmOnly($"Request {iteration}");
            var waiting = Task.Run(() =>
            {
                var response = control.HandleInteraction(request);
                control.WaitIfPaused();
                return response;
            });
            var resuming = Task.Run(() =>
            {
                while (!control.IsAwaitingOperator)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    Thread.Yield();
                }
                control.Resume(OperatorInteractionResponse.Continue(request.Id));
            });
            await resuming.WaitAsync(timeout.Token);
            var response = await waiting.WaitAsync(timeout.Token);
            Assert.Equal(request.Id, response.RequestId);
            Assert.False(response.Cancelled);
            Assert.False(control.IsPaused);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Abort_and_external_cancellation_release_pause_and_complete_the_cancelled_request(bool external)
    {
        using var control = new OpenTapRunControlState();
        using var cancellation = new CancellationTokenSource();
        control.BeginRun(cancellation.Token);
        var request = OperatorInteractionRequest.ConfirmOnly("Cancel request");
        control.BeginPendingInteraction(request);

        if (external) cancellation.Cancel();
        else control.Abort();

        var response = await Task.Run(() => control.WaitForInteractionResponse(request.Id)).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(response.Cancelled);
        Assert.Equal(request.Id, response.RequestId);
        Assert.True(control.IsCancellationRequested);
        Assert.False(control.IsAwaitingOperator);
        Assert.Null(control.PendingInteraction);
        Assert.Null(control.OperatorPromptMessage);
        Assert.False(control.IsPaused);
        Assert.True(control.WaitPauseGate(0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Run(control.WaitIfPaused).WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Interaction_started_after_run_cancellation_returns_cancelled_without_blocking()
    {
        using var control = new OpenTapRunControlState();
        using var cancellation = new CancellationTokenSource();
        control.BeginRun(cancellation.Token);
        cancellation.Cancel();
        var request = OperatorInteractionRequest.ConfirmOnly("Already cancelled");

        var response = await Task.Run(() => control.HandleInteraction(request)).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(request.Id, response.RequestId);
        Assert.True(response.Cancelled);
        Assert.False(control.IsAwaitingOperator);
        Assert.Null(control.PendingInteraction);
        Assert.Null(control.OperatorPromptMessage);
        Assert.False(control.IsPaused);
        Assert.True(control.WaitPauseGate(0));
    }
}
