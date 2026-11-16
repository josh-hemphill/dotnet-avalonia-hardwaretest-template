using System.Text.Json;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class OperatorDutReconfirmationTests
{
    [Fact]
    public async Task Editing_a_reopened_confirmation_to_another_dut_resets_attempts_and_credential_before_its_saved_run()
    {
        var tap = new FakeOpenTapSession { ReportSamples = false };
        tap.SummarySamplesFactory = (started, completed) =>
        [
            new StoredSample
            {
                MetricKey = "VDC", Channel = "VDC", Unit = "V",
                Timestamp = started + (completed - started) / 2,
                Value = tap.LastDut?.Serial == "DUT-A" ? 1 : 2,
            },
        ];
        using var store = new FakeRunStore();
        var vm = new RunTestViewModel(tap, tap, tap, new OperatorSession(), new FakeRunControl(), new FakeReportService(), store,
            new AppSettings { UseMockVisa = true, IsEngineerDebugMode = true })
        { UiScheduler = action => action() };
        await vm.ProgramSelection.RefreshProgramsAsync();
        vm.SessionPanel.DutSerialInput = "DUT-A";
        vm.SessionPanel.DutPartInput = "PART-A";
        vm.SessionPanel.DutRevisionInput = "A";
        vm.SessionPanel.OperatorInput = "Same technician";
        vm.Session.ApplyOperatorCredential("A-CARD", CredentialTransport.Contactless, "Same technician");
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        var firstSession = vm.Session.SessionId;
        await vm.Run.RunCommand.ExecuteAsync();
        await vm.Run.RunCommand.ExecuteAsync();
        var sourceA = Assert.IsType<TestRunRecord>(await store.LoadAsync(vm.LastRunId!));
        Assert.NotEmpty(sourceA.StepAttempts);
        Assert.All(sourceA.StepAttempts, attempt => Assert.Equal(2, attempt.AttemptCount));
        var original = JsonSerializer.Serialize(sourceA, AppJsonContext.Default.TestRunRecord);

        vm.SessionPanel.RequireContextConfirmation();
        vm.SessionPanel.DutSerialInput = "  DUT-B\r\n";
        vm.SessionPanel.DutPartInput = "PART-B";
        vm.SessionPanel.DutRevisionInput = "B";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(vm.SessionPanel.SessionBlocked);
        Assert.NotEqual(firstSession, vm.Session.SessionId);
        Assert.Null(vm.Session.OperatorCredentialSerial);
        Assert.Null(vm.Session.OperatorCredentialTransport);
        Assert.Equal("DUT-B", vm.SessionPanel.DutSerialInput);
        Assert.Equal("PART-B", vm.SessionPanel.DutPartInput);
        Assert.Equal("B", vm.SessionPanel.DutRevisionInput);
        Assert.Equal("Same technician", vm.SessionPanel.OperatorInput);
        Assert.All(sourceA.StepAttempts, attempt => Assert.Null(vm.Run.FindAttempt(attempt.StepPath)));
        await vm.Run.RunCommand.ExecuteAsync();
        var sourceB = Assert.IsType<TestRunRecord>(await store.LoadAsync(vm.LastRunId!));
        Assert.Equal("DUT-B", sourceB.DutSerial);
        Assert.Equal(vm.Session.SessionId, sourceB.SessionId);
        Assert.NotEqual(sourceA.SessionId, sourceB.SessionId);
        Assert.NotEmpty(sourceB.StepAttempts);
        Assert.All(sourceB.StepAttempts, attempt => Assert.Equal(1, attempt.AttemptCount));
        Assert.All(sourceB.Samples, sample => Assert.Equal(2, sample.Value));
        Assert.Equal(original, JsonSerializer.Serialize(Assert.IsType<TestRunRecord>(await store.LoadAsync(sourceA.RunId)), AppJsonContext.Default.TestRunRecord));

        var secondSession = vm.Session.SessionId;
        vm.SessionPanel.RequireContextConfirmation();
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.Equal(secondSession, vm.Session.SessionId);
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.All((await store.LoadAsync(vm.LastRunId!))!.StepAttempts, attempt => Assert.Equal(2, attempt.AttemptCount));

        vm.Session.ApplyOperatorCredential("B-CARD", CredentialTransport.Contactless, "Same technician");
        vm.SessionPanel.RequireContextConfirmation();
        vm.SessionPanel.OperatorInput = "Incoming technician";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.NotEqual(secondSession, vm.Session.SessionId);
        Assert.Null(vm.Session.OperatorCredentialSerial);
        Assert.Equal("DUT-B", vm.Session.DutSerial);
        await vm.Run.RunCommand.ExecuteAsync();
        var incoming = Assert.IsType<TestRunRecord>(await store.LoadAsync(vm.LastRunId!));
        Assert.Equal("Incoming technician", incoming.OperatorName);
        Assert.All(incoming.StepAttempts, attempt => Assert.Equal(1, attempt.AttemptCount));
        Assert.Equal(original, JsonSerializer.Serialize(Assert.IsType<TestRunRecord>(await store.LoadAsync(sourceA.RunId)), AppJsonContext.Default.TestRunRecord));
    }

    [Theory]
    [InlineData("part")]
    [InlineData("revision")]
    public async Task Changing_known_dut_metadata_crosses_the_new_session_boundary(string field)
    {
        var session = new OperatorSession();
        var clears = 0;
        var panel = new OperatorSessionPanelViewModel(session, new AppSettings(), _ => { }, onSessionCleared: () => clears++);
        panel.DutSerialInput = "DUT"; panel.DutPartInput = "PART"; panel.DutRevisionInput = "A";
        panel.OperatorInput = "Technician";
        await panel.ConfirmSessionCommand.ExecuteAsync();
        session.ApplyOperatorCredential("OLD", CredentialTransport.Contactless, "Technician");
        var oldSession = session.SessionId;
        panel.RequireContextConfirmation();
        if (field == "part") panel.DutPartInput = "OTHER-PART";
        else panel.DutRevisionInput = "B";
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.NotEqual(oldSession, session.SessionId);
        Assert.Equal(1, clears);
        Assert.Null(session.OperatorCredentialSerial);
        Assert.Equal("DUT", session.DutSerial);
        Assert.Equal(field == "part" ? "OTHER-PART" : "PART", session.DutPartNumber);
        Assert.Equal(field == "revision" ? "B" : "A", session.DutRevision);
    }

    [Fact]
    public async Task Completing_newly_required_metadata_with_the_same_technician_preserves_the_session()
    {
        var selected = new ProgramItemViewModel { Id = "dut", DisplayName = "DUT", Path = "dut.TapPlan", Requirements = ProgramRequirements.Sample };
        var session = new OperatorSession();
        var clears = 0;
        var panel = new OperatorSessionPanelViewModel(session, new AppSettings(), _ => { }, () => selected, () => clears++);
        panel.DutSerialInput = "DUT"; panel.OperatorInput = "First technician";
        await panel.ConfirmSessionCommand.ExecuteAsync();
        var original = session.SessionId;
        selected = new() { Id = "dut", DisplayName = "DUT", Path = "dut.TapPlan", Requirements = new() { RequireSerial = true, RequireOperator = true, RequirePartNumber = true, RequireRevision = true } };
        panel.RefreshRequirementFlags();
        panel.DutPartInput = "NEWLY-RECORDED-PART"; panel.DutRevisionInput = "A";
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        Assert.Equal(original, session.SessionId);
        Assert.Equal(0, clears);
        Assert.Equal("First technician", session.OperatorName);
        Assert.Equal("NEWLY-RECORDED-PART", session.DutPartNumber);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Badge_capture_binds_to_the_incoming_dut_or_technician_and_confirmation_keeps_that_fresh_presence(bool differentDut)
    {
        var session = new OperatorSession();
        session.ConfirmDut("DUT-A");
        session.ApplyOperatorCredential("OLD", CredentialTransport.Contactless, "Old technician");
        var original = session.SessionId;
        var clears = 0;
        var panel = new OperatorSessionPanelViewModel(session, new AppSettings { RequireCredentialForOperator = true }, _ => { },
            onSessionCleared: () => clears++, credentialBroker: new MockOperatorCredentialBroker());
        panel.DutSerialInput = differentDut ? "DUT-B" : "DUT-A";
        await panel.CaptureCredentialCommand.ExecuteAsync();
        var incoming = session.SessionId;
        Assert.NotEqual(original, incoming);
        Assert.Equal(1, clears);
        Assert.Equal(MockOperatorCredentialBroker.MockSerial, session.OperatorCredentialSerial);
        Assert.False(panel.IsCapturingCredential);
        Assert.True(panel.SessionBlocked);
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        Assert.Equal(incoming, session.SessionId);
        Assert.Equal(differentDut ? "DUT-B" : "DUT-A", session.DutSerial);
        Assert.Equal(MockOperatorCredentialBroker.MockSerial, session.OperatorCredentialSerial);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_known_incoming_technician_cannot_reuse_the_departing_badge_through_full_or_same_dut_confirmation(bool sameDutPrompt)
    {
        var session = new OperatorSession();
        Assert.True(session.TryConfirm(ProgramRequirements.Sample, "DUT", "PART", "A", "Departing technician", "generic", out _));
        session.ApplyOperatorCredential("DEPARTING", CredentialTransport.Contactless, "Departing technician");
        var original = session.SessionId;
        var clears = 0;
        var panel = new OperatorSessionPanelViewModel(session, new AppSettings { RequireCredentialForOperator = true }, _ => { },
            onSessionCleared: () => clears++, credentialBroker: new MockOperatorCredentialBroker())
        { DutSerialInput = "DUT", DutPartInput = "PART", DutRevisionInput = "A", OperatorInput = "Departing technician" };
        if (sameDutPrompt) { session.MarkStale(); panel.RefreshSessionSummary(); }
        else panel.RequireContextConfirmation();
        panel.OperatorInput = MockOperatorCredentialBroker.MockDisplayName;
        if (sameDutPrompt) await panel.ConfirmSameDutCommand.ExecuteAsync();
        else await panel.ConfirmSessionCommand.ExecuteAsync();
        var incomingSession = session.SessionId;
        Assert.NotEqual(original, incomingSession);
        Assert.Equal(1, clears);
        Assert.Null(session.OperatorCredentialSerial);
        Assert.Null(session.OperatorName);
        Assert.True(panel.SessionBlocked);
        Assert.Contains("badge", panel.OperatorError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("DUT", panel.DutSerialInput);
        Assert.Equal("PART", panel.DutPartInput);
        Assert.Equal("A", panel.DutRevisionInput);
        Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, panel.OperatorInput);

        await panel.CaptureCredentialCommand.ExecuteAsync();
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        Assert.Equal(incomingSession, session.SessionId);
        Assert.Equal("DUT", session.DutSerial);
        Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, session.OperatorName);
        Assert.Equal(MockOperatorCredentialBroker.MockSerial, session.OperatorCredentialSerial);
    }

    [Fact]
    public async Task Typed_technician_handoff_invalidates_a_late_departing_reader_response()
    {
        var session = new OperatorSession();
        Assert.True(session.TryConfirm(ProgramRequirements.Sample, "DUT", null, null, "Departing technician", "generic", out _));
        var original = session.SessionId;
        var reader = new PendingReader();
        var panel = new OperatorSessionPanelViewModel(session, new AppSettings(), _ => { }, credentialBroker: reader)
        { DutSerialInput = "DUT", OperatorInput = "Departing technician" };
        var departing = panel.CaptureCredentialCommand.ExecuteAsync();
        Assert.True(panel.IsCapturingCredential);
        panel.RequireContextConfirmation();
        panel.OperatorInput = "Incoming technician";
        await panel.ConfirmSessionCommand.ExecuteAsync();
        await departing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotEqual(original, session.SessionId);
        reader.Result.SetResult(new() { Credential = new() { DisplayName = "Departing technician", Serial = "LATE", Transport = CredentialTransport.Contactless } });
        Assert.Equal("Incoming technician", session.OperatorName);
        Assert.Equal("Incoming technician", panel.OperatorInput);
        Assert.Null(session.OperatorCredentialSerial);
        Assert.False(panel.IsCapturingCredential);
    }

    [Fact]
    public async Task Active_run_ownership_refuses_editable_reconfirmation_and_credential_rebinding()
    {
        var session = new OperatorSession();
        session.ConfirmDut("DUT-A");
        session.ApplyOperatorCredential("OLD", CredentialTransport.Contactless, "Old technician");
        var original = session.SessionId;
        var clears = 0;
        var panel = new OperatorSessionPanelViewModel(session, new AppSettings(), _ => { },
            onSessionCleared: () => clears++, credentialBroker: new MockOperatorCredentialBroker(), isRunning: () => true)
        { DutSerialInput = "DUT-B", OperatorInput = "Incoming technician" };
        await panel.ConfirmSessionCommand.ExecuteAsync();
        await panel.CaptureCredentialCommand.ExecuteAsync();
        Assert.Equal(original, session.SessionId);
        Assert.Equal("DUT-A", session.DutSerial);
        Assert.Equal("OLD", session.OperatorCredentialSerial);
        Assert.Equal("Old technician", session.OperatorName);
        Assert.Equal(0, clears);
        Assert.Equal("DUT-B", panel.DutSerialInput);
    }

    private sealed class PendingReader : IOperatorCredentialBroker
    {
        public TaskCompletionSource<CredentialCaptureResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string StatusText => "Waiting for badge";
        public bool IsMock => true;
        public bool CanSign => false;
        public string? SigningAlgorithm => null;
        public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => Result.Task;
        public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
