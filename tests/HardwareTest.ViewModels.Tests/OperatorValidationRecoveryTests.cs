using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class OperatorValidationRecoveryTests
{
    [Fact]
    public async Task Required_identity_errors_follow_their_fields_and_correction_clears_stale_help()
    {
        var program = new ProgramItemViewModel
        {
            Id = "required-identity",
            Path = "fixture.TapPlan",
            DisplayName = "Required identity",
            Requirements = new ProgramRequirements { RequireSerial = true, RequirePartNumber = true, RequireRevision = true, RequireOperator = true }
        };
        var panel = new OperatorSessionPanelViewModel(new OperatorSession(), new AppSettings(), _ => { }, () => program);
        var focus = new List<string>();
        panel.RequestFocusSessionField += focus.Add;
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.Equal(OperatorSessionPanelViewModel.SessionFieldDutSerial, focus[^1]);
        Assert.True(panel.HasDutSerialError);
        panel.DutSerialInput = "DUT-1";
        Assert.False(panel.HasDutSerialError);
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.Equal(OperatorSessionPanelViewModel.SessionFieldDutPart, focus[^1]);
        Assert.True(panel.HasDutPartError);
        Assert.False(panel.HasDutSerialError);
        panel.DutPartInput = "CONTROL-BOARD";
        Assert.Empty(panel.DutPartError);
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.Equal(OperatorSessionPanelViewModel.SessionFieldDutRevision, focus[^1]);
        Assert.True(panel.HasDutRevisionError);
        panel.DutRevisionInput = "C";
        Assert.Empty(panel.DutRevisionError);
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.Equal(OperatorSessionPanelViewModel.SessionFieldTechnician, focus[^1]);
        Assert.True(panel.HasOperatorError);
        panel.OperatorInput = "Alex";
        Assert.Empty(panel.OperatorError);
        await panel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(panel.SessionBlocked);
        Assert.True(panel.Session.CanRun);
        Assert.Equal("CONTROL-BOARD", panel.Session.DutPartNumber);
        Assert.Equal("C", panel.Session.DutRevision);
    }

    [Fact]
    public void Invalid_prompt_focus_targets_the_actual_later_field_without_losing_other_values()
    {
        var request = new OperatorInteractionRequest
        {
            Id = "fixture",
            Title = "Fixture setup",
            Message = "Enter the fixture values.",
            Fields =
            [
                new() { Id = "fixtureId", Label = "Fixture", Kind = OperatorInteractionFieldKind.String, Required = true },
                new() { Id = "torque", Label = "Torque", Kind = OperatorInteractionFieldKind.Number, Required = true },
                new() { Id = "secured", Label = "Fixture secured", Kind = OperatorInteractionFieldKind.Boolean, Required = true }
            ]
        };
        var host = new InteractionHostViewModel();
        host.Apply(request, null);
        host.InteractionFields[0].Value = "FIXTURE-14";
        host.InteractionFields[1].Value = "invalid torque";
        var focus = new List<string>();
        host.RequestFocusField += focus.Add;
        Assert.False(host.TryCollectResponse(request, out _));
        Assert.Equal("torque", focus[^1]);
        Assert.Equal(host.InteractionValidationError, host.InteractionFields[1].ValidationError);
        Assert.Equal("invalid torque", host.InteractionFields[1].Value);
        host.InteractionFields[1].Value = "3.5";
        Assert.Empty(host.InteractionFields[1].ValidationError);
        Assert.False(host.TryCollectResponse(request, out _));
        Assert.Equal("secured", focus[^1]);
        host.InteractionFields[2].BoolValue = true;
        Assert.Empty(host.InteractionFields[2].ValidationError);
        Assert.True(host.TryCollectResponse(request, out var values));
        Assert.Equal("FIXTURE-14", values["fixtureId"]);
        Assert.Equal("3.5", values["torque"]);
        Assert.Null(host.InteractionValidationError);
    }
}
