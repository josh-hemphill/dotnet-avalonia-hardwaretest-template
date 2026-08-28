using HardwareTest.Features.RunTest;
using HardwareTest.Features.Shell;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Operator prompt layout contracts and interaction/session keyboard-wedge helpers.
public sealed class OperatorPromptChromeTests
{
    [Fact]
    public void InteractionHostView_pins_continue_outside_the_body_scroller()
    {
        var axaml = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/InteractionHostView.axaml"));
        Assert.Contains("PromptBodyScroller", axaml, StringComparison.Ordinal);
        Assert.Contains("ContinueButton", axaml, StringComparison.Ordinal);
        Assert.Contains("DockPanel.Dock=\"Bottom\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", axaml, StringComparison.Ordinal);

        var continueIndex = axaml.IndexOf("x:Name=\"ContinueButton\"", StringComparison.Ordinal);
        var scrollerIndex = axaml.IndexOf("x:Name=\"PromptBodyScroller\"", StringComparison.Ordinal);
        Assert.True(continueIndex >= 0 && scrollerIndex >= 0, "Continue and body scroller must be named.");
        Assert.True(
            continueIndex < scrollerIndex,
            "Continue must be declared in the docked footer before the scrollable prompt body.");
    }

    [Fact]
    public void Interaction_and_run_board_share_stop_run_copy()
    {
        var header = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunHeaderView.axaml"));
        var prep = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunPreparationView.axaml"));
        var runAxaml = File.ReadAllText(FindRepoFile("src/HardwareTest/Features/RunTest/RunTestView.axaml"));
        Assert.Contains("StopRunCopy.CooperativeTip", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Blocking instrument I/O may continue", header, StringComparison.Ordinal);
        Assert.Contains("<vm:InteractionHostView", runAxaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DutSerialBox\"", prep, StringComparison.Ordinal);
        Assert.Contains("OnSessionFieldKeyDown", prep, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractionHostViewModel_rejects_non_numeric_number_field()
    {
        var host = new InteractionHostViewModel();
        var request = new OperatorInteractionRequest
        {
            Id = "req-num",
            Title = "Torque",
            Message = "Enter torque",
            Fields =
            [
                new OperatorInteractionField
                {
                    Id = "fixtureTorqueNm",
                    Label = "Fixture torque (N·m)",
                    Kind = OperatorInteractionFieldKind.Number,
                    Required = true,
                },
            ],
        };

        host.Apply(request, fallbackMessage: null);
        host.InteractionFields[0].Value = "not-a-number";
        Assert.False(host.TryCollectResponse(request, out _));
        Assert.Contains("must be a number", host.InteractionValidationError, StringComparison.OrdinalIgnoreCase);

        host.InteractionFields[0].Value = "3.5";
        Assert.True(host.TryCollectResponse(request, out var values));
        Assert.Equal("3.5", values["fixtureTorqueNm"]);
    }

    [Fact]
    public void InteractionHostViewModel_apply_requests_focus_on_first_field()
    {
        var host = new InteractionHostViewModel();
        var focused = 0;
        host.RequestFocusFirstField += (_, _) => focused++;
        host.Apply(
            new OperatorInteractionRequest
            {
                Id = "req-focus",
                Title = "Install fixture",
                Message = "Enter fixture id",
                Fields =
                [
                    new OperatorInteractionField
                    {
                        Id = "fixtureId",
                        Label = "Fixture id",
                        Kind = OperatorInteractionFieldKind.String,
                        Required = true,
                    },
                ],
            },
            fallbackMessage: null);
        Assert.Equal(1, focused);
    }

    [Fact]
    public void InteractionField_display_label_marks_required()
    {
        var required = new InteractionFieldViewModel(new OperatorInteractionField
        {
            Id = "fixtureId",
            Label = "Fixture id",
            Kind = OperatorInteractionFieldKind.String,
            Required = true,
        });
        Assert.Equal("Fixture id *", required.DisplayLabel);
        Assert.Equal("Fixture id *", required.AutomationName);

        var optional = new InteractionFieldViewModel(new OperatorInteractionField
        {
            Id = "torque",
            Label = "Fixture torque (N·m)",
            Kind = OperatorInteractionFieldKind.Number,
        });
        Assert.Equal("Fixture torque (N·m)", optional.DisplayLabel);
    }

    [Fact]
    public async Task Session_confirm_trims_scanner_suffixes_and_change_requests_focus()
    {
        var focused = 0;
        var panel = new OperatorSessionPanelViewModel(
            new OpenTap.Host.OperatorSession(),
            new Core.Settings.AppSettings(),
            _ => { });
        panel.RequestFocusDutSerial += (_, _) => focused++;

        panel.DutSerialInput = "  DUT-SCAN-1\r\n";
        panel.OperatorInput = "  Tech\t";
        await panel.ConfirmSessionCommand.ExecuteAsync();

        Assert.False(panel.SessionBlocked);
        Assert.Equal("DUT-SCAN-1", panel.Session.DutSerial);
        Assert.Equal("Tech", panel.Session.OperatorName);
        Assert.Equal("DUT-SCAN-1", panel.DutSerialInput);
        Assert.Equal("Tech", panel.OperatorInput);

        await panel.ChangeSessionCommand.ExecuteAsync();
        Assert.True(panel.NeedsDutConfirm);
        Assert.Equal(1, focused);
    }

    [Fact]
    public void NormalizeScan_strips_wedge_terminators()
    {
        Assert.Equal("SN-1", OperatorSessionPanelViewModel.NormalizeScan("  SN-1 \r\n"));
        Assert.Equal(string.Empty, OperatorSessionPanelViewModel.NormalizeScan("   "));
    }

    [Fact]
    public void OperatorTouchDensity_includes_interaction_host_caps()
    {
        Assert.Equal(280, OperatorTouchDensity.InteractionHostMaxHeight);
        Assert.Equal(180, OperatorTouchDensity.InteractionHostBodyMaxHeight);
        Assert.True(OperatorTouchDensity.InteractionHostBodyMaxHeight < OperatorTouchDensity.InteractionHostMaxHeight);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} from {AppContext.BaseDirectory}");
    }
}
