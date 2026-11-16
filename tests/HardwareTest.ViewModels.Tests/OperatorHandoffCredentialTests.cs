using HardwareTest.Core.Credentials;
using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Time;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class OperatorHandoffCredentialTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changing_session_discards_late_badge_success_or_failure_and_allows_incoming_capture(bool success)
    {
        var broker = new DelayedReader();
        var session = new OperatorSession();
        var panel = CreatePanel(session, broker);
        var departing = panel.CaptureCredentialCommand.ExecuteAsync();
        Assert.True(panel.IsCapturingCredential);
        Assert.Single(broker.Requests);

        await panel.ChangeSessionCommand.ExecuteAsync();
        await departing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(broker.Requests[0].Token.IsCancellationRequested);
        Assert.False(panel.IsCapturingCredential);
        Assert.True(panel.SessionBlocked);
        var incoming = panel.CaptureCredentialCommand.ExecuteAsync();
        Assert.Equal(2, broker.Requests.Count);
        Assert.True(panel.IsCapturingCredential);

        // This reader deliberately ignores cancellation and returns after the next
        // operator has already started identification.
        broker.Requests[0].Result.SetResult(success ? Captured("Departing", "OLD") : new() { Error = "Old reader error" });
        Assert.Null(session.OperatorName);
        Assert.Null(session.OperatorCredentialSerial);
        Assert.Empty(panel.OperatorInput);
        Assert.Empty(panel.OperatorError);
        Assert.Equal(broker.StatusText, panel.CredentialStatus);
        Assert.True(panel.IsCapturingCredential);

        broker.Requests[1].Result.SetResult(Captured("Incoming", "NEW"));
        await incoming.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("Incoming", panel.OperatorInput);
        Assert.Equal("Incoming", session.OperatorName);
        Assert.Equal("NEW", session.OperatorCredentialSerial);
        Assert.False(panel.IsCapturingCredential);
        Assert.True(panel.SessionBlocked); // Identification never starts or confirms a run.
    }

    [Theory]
    [InlineData("program")]
    [InlineData("path")]
    [InlineData("dut")]
    public async Task Changing_session_context_invalidates_the_old_reader_request(string change)
    {
        var session = new OperatorSession();
        session.SelectProgram("first", "first.TapPlan", "First");
        var broker = new DelayedReader();
        var panel = CreatePanel(session, broker);
        var capture = panel.CaptureCredentialCommand.ExecuteAsync();

        switch (change)
        {
            case "program": session.SelectProgram("second", "second.TapPlan", "Second"); break;
            case "path": session.SelectProgram("first", "replacement.TapPlan", "Replacement"); break;
            default: session.ChangeDut(); break;
        }
        await capture.WaitAsync(TimeSpan.FromSeconds(2));
        broker.Requests[0].Result.SetResult(Captured("Old context", "OLD"));
        Assert.True(broker.Requests[0].Token.IsCancellationRequested);
        Assert.Null(session.OperatorName);
        Assert.Null(session.OperatorCredentialSerial);
        Assert.Empty(panel.OperatorInput);
        Assert.Empty(panel.OperatorError);
        Assert.False(panel.IsCapturingCredential);
    }

    [Fact]
    public async Task Explicit_confirmation_cancels_optional_capture_and_keeps_the_confirmed_technician()
    {
        var session = new OperatorSession();
        var broker = new DelayedReader();
        var panel = CreatePanel(session, broker);
        var capture = panel.CaptureCredentialCommand.ExecuteAsync();
        panel.DutSerialInput = "DUT-INCOMING";
        panel.OperatorInput = "Confirmed technician";

        await panel.ConfirmSessionCommand.ExecuteAsync();
        await capture.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(session.CanRun);
        Assert.True(broker.Requests[0].Token.IsCancellationRequested);
        broker.Requests[0].Result.SetResult(Captured("Late badge", "LATE"));
        Assert.Equal("Confirmed technician", session.OperatorName);
        Assert.Equal("Confirmed technician", panel.OperatorInput);
        Assert.Null(session.OperatorCredentialSerial);
        Assert.Empty(panel.OperatorError);
    }

    [Fact]
    public async Task Capture_cannot_replace_identity_during_an_active_run()
    {
        var session = new OperatorSession();
        session.ConfirmDut("DUT-ACTIVE");
        session.ApplyOperatorCredential("ACTIVE", CredentialTransport.Contactless, "Active technician");
        var broker = new DelayedReader();
        var panel = new OperatorSessionPanelViewModel(session, new AppSettings(), _ => { },
            credentialBroker: broker, isRunning: () => true);
        await panel.CaptureCredentialCommand.ExecuteAsync();
        Assert.Empty(broker.Requests);
        Assert.Equal("Active technician", session.OperatorName);
        Assert.Equal("ACTIVE", session.OperatorCredentialSerial);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Idle_expiry_discards_pending_reader_work_without_blocking_a_fresh_stale_capture(bool lateSuccess)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
        var session = new OperatorSession(clock);
        Assert.True(session.TryConfirm(ProgramRequirements.Sample, "IDLE-DUT", null, null,
            "Current technician", "generic", out _));
        session.ApplyOperatorCredential("CONFIRMED", CredentialTransport.Contactless, "Current technician");
        var sessionId = session.SessionId;
        var broker = new DelayedReader();
        var panel = new OperatorSessionPanelViewModel(session, new AppSettings(), _ => { },
            clock: clock, credentialBroker: broker)
        {
            OperatorInput = "Current technician"
        };
        var expiring = panel.CaptureCredentialCommand.ExecuteAsync();
        Assert.True(panel.IsCapturingCredential);
        clock.Advance(TimeSpan.FromMinutes(11));
        session.EvaluateIdle(TimeSpan.FromMinutes(10), 80);
        await expiring.WaitAsync(TimeSpan.FromSeconds(2));
        panel.RefreshSessionSummary();
        Assert.Equal(OperatorSessionState.Stale, session.State);
        Assert.Equal(sessionId, session.SessionId);
        Assert.True(broker.Requests[0].Token.IsCancellationRequested);
        Assert.Equal("CONFIRMED", session.OperatorCredentialSerial);
        Assert.Equal("Current technician", session.OperatorName);
        Assert.True(panel.SessionBlocked);
        Assert.True(panel.ShowSameDutPrompt);

        var fresh = panel.CaptureCredentialCommand.ExecuteAsync();
        Assert.Equal(2, broker.Requests.Count);
        Assert.True(panel.IsCapturingCredential);
        broker.Requests[0].Result.SetResult(lateSuccess ? Captured("Expired reader", "EXPIRED")
            : new() { Error = "Late expired reader failure" });
        Assert.Equal("CONFIRMED", session.OperatorCredentialSerial);
        Assert.Equal("Current technician", session.OperatorName);
        Assert.Equal("Current technician", panel.OperatorInput);
        Assert.Empty(panel.OperatorError);
        Assert.Equal(broker.StatusText, panel.CredentialStatus);
        Assert.True(panel.IsCapturingCredential);

        broker.Requests[1].Result.SetResult(Captured("Current technician", "FRESH"));
        await fresh.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("FRESH", session.OperatorCredentialSerial);
        Assert.False(panel.IsCapturingCredential);
        Assert.True(panel.SessionBlocked); // Fresh presence does not auto-confirm or resume.
        await panel.ConfirmSameDutCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        Assert.Equal(OperatorSessionState.Active, session.State);
        Assert.Equal(sessionId, session.SessionId);
    }

    private static OperatorSessionPanelViewModel CreatePanel(OperatorSession session, DelayedReader broker)
        => new(session, new AppSettings(), _ => { }, credentialBroker: broker);

    private static CredentialCaptureResult Captured(string name, string serial) => new()
    {
        Credential = new OperatorCredential { DisplayName = name, Serial = serial, Transport = CredentialTransport.Contactless }
    };

    private sealed class DelayedReader : IOperatorCredentialBroker
    {
        internal sealed record Request(CancellationToken Token, TaskCompletionSource<CredentialCaptureResult> Result);
        public List<Request> Requests { get; } = [];
        public string StatusText => "Present the next operator's badge.";
        public bool IsMock => true;
        public bool CanSign => false;
        public string? SigningAlgorithm => null;

        public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var request = new Request(cancellationToken, new(TaskCreationOptions.RunContinuationsAsynchronously));
            Requests.Add(request);
            return request.Result.Task;
        }

        public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential,
            string? pin = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
