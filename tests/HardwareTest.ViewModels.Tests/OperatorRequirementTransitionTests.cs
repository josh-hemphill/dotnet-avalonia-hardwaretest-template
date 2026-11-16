using HardwareTest.Core.Credentials;
using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class OperatorRequirementTransitionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stricter_program_requires_full_confirmation_without_discarding_confirmed_context(bool differentFamily)
    {
        var selected = Program("first", "controller", new() { RequireSerial = true, RequireOperator = true });
        var session = new OperatorSession();
        session.SelectProgram(selected.Id, selected.Path, selected.DisplayName, selected.DutFamily);
        var panel = new OperatorSessionPanelViewModel(session, new AppSettings(), _ => { }, () => selected);
        panel.DutSerialInput = "CONFIRMED-DUT";
        panel.OperatorInput = "Confirmed technician";
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        var sessionId = session.SessionId;

        selected = Program("stricter", differentFamily ? "different-controller" : "controller", new()
        {
            RequireSerial = true,
            RequirePartNumber = true,
            RequireRevision = true,
            RequireOperator = true
        });
        session.SelectProgram(selected.Id, selected.Path, selected.DisplayName, selected.DutFamily);
        panel.RefreshRequirementFlags();
        Assert.True(panel.SessionBlocked);
        Assert.True(panel.NeedsDutConfirm);
        Assert.True(panel.ShowFullFormChangeSession);
        Assert.False(panel.ShowSameDutPrompt);
        Assert.True(panel.RequirePartNumber);
        Assert.True(panel.RequireRevision);
        Assert.Equal("CONFIRMED-DUT", panel.DutSerialInput);
        Assert.Equal("Confirmed technician", panel.OperatorInput);
        Assert.Equal(sessionId, session.SessionId);
        // The core's deliberately narrow state/serial contract remains unchanged.
        Assert.Equal(!differentFamily, session.CanRun);

        await panel.ConfirmSameDutCommand.ExecuteAsync();
        Assert.True(panel.SessionBlocked);
        Assert.True(panel.HasDutPartError);
        panel.DutPartInput = "PART-REQUIRED";
        await panel.ConfirmSameDutCommand.ExecuteAsync();
        Assert.True(panel.SessionBlocked);
        Assert.True(panel.HasDutRevisionError);
        panel.DutRevisionInput = "D";
        panel.RefreshSessionSummary();
        await panel.ConfirmSameDutCommand.ExecuteAsync();
        Assert.True(panel.SessionBlocked); // Typing and Same DUT cannot satisfy the new confirmation.
        Assert.Null(session.DutPartNumber);
        Assert.Null(session.DutRevision);

        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        Assert.False(panel.NeedsDutConfirm);
        Assert.False(panel.ShowFullFormChangeSession);
        Assert.True(session.CanRun);
        Assert.Equal("PART-REQUIRED", session.DutPartNumber);
        Assert.Equal("D", session.DutRevision);
        Assert.Equal("Confirmed technician", session.OperatorName);
        Assert.Equal(sessionId, session.SessionId);
    }

    [Fact]
    public async Task Newly_required_technician_cannot_be_satisfied_by_typing_without_confirmation()
    {
        var selected = Program("anonymous", "controller", new() { RequireSerial = true, RequireOperator = false });
        var session = new OperatorSession();
        var panel = new OperatorSessionPanelViewModel(session, new AppSettings(), _ => { }, () => selected);
        panel.DutSerialInput = "ANONYMOUS-DUT";
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        selected = Program("named", "controller", new() { RequireSerial = true, RequireOperator = true });
        panel.RefreshRequirementFlags();
        Assert.True(panel.NeedsDutConfirm);
        Assert.True(panel.SessionBlocked);
        panel.OperatorInput = "Newly required technician";
        panel.RefreshSessionSummary();
        await panel.ConfirmSameDutCommand.ExecuteAsync();
        Assert.True(panel.SessionBlocked);
        Assert.Null(session.OperatorName);
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        Assert.Equal("Newly required technician", session.OperatorName);
    }

    [Fact]
    public async Task Live_badge_policy_change_requires_presence_and_an_explicit_new_confirmation()
    {
        var selected = Program("badge-policy", "controller", new() { RequireSerial = true, RequireOperator = true });
        var settings = new AppSettings();
        var session = new OperatorSession();
        var panel = new OperatorSessionPanelViewModel(session, settings, _ => { }, () => selected,
            credentialBroker: new MockOperatorCredentialBroker());
        panel.DutSerialInput = "BADGE-POLICY-DUT";
        panel.OperatorInput = "Typed technician";
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        Assert.Null(session.OperatorCredentialSerial);
        settings.RequireCredentialForOperator = true;
        panel.RefreshRequirementFlags();
        Assert.True(panel.SessionBlocked);
        Assert.True(panel.NeedsDutConfirm);
        Assert.True(panel.ShowFullFormChangeSession);
        panel.OperatorInput = "Another typed name";
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.True(panel.SessionBlocked);
        Assert.Contains("badge", panel.OperatorError, StringComparison.OrdinalIgnoreCase);
        await panel.ConfirmSameDutCommand.ExecuteAsync();
        Assert.True(panel.SessionBlocked);
        await panel.CaptureCredentialCommand.ExecuteAsync();
        panel.RefreshSessionSummary();
        Assert.True(panel.SessionBlocked);
        Assert.True(panel.NeedsDutConfirm);
        Assert.Equal(MockOperatorCredentialBroker.MockSerial, session.OperatorCredentialSerial);
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, session.OperatorName);
        Assert.Equal("BADGE-POLICY-DUT", session.DutSerial);
    }

    private static ProgramItemViewModel Program(string id, string family, ProgramRequirements requirements) => new()
    {
        Id = id,
        DisplayName = id,
        Path = id + ".TapPlan",
        DutFamily = family,
        Requirements = requirements
    };
}
