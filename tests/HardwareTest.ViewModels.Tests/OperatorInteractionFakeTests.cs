using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class OperatorInteractionFakeTests
{
    [Fact]
    public void Fake_BeginInteraction_waits_for_Resume_response()
    {
        var openTap = new FakeOpenTapSession();
        var request = OperatorInteractionRequest.ConfirmOnly("Seat fixture");
        openTap.BeginInteraction(request);
        Assert.True(openTap.IsAwaitingOperator);
        Assert.Equal(request.Id, openTap.PendingInteraction?.Id);

        var response = OperatorInteractionResponse.Continue(request.Id, new Dictionary<string, string>
        {
            ["note"] = "ok",
        });
        openTap.Resume(response);
        Assert.False(openTap.IsAwaitingOperator);
        Assert.Null(openTap.PendingInteraction);
        Assert.Equal(response.RequestId, openTap.LastInteractionResponse?.RequestId);
        Assert.Equal("ok", openTap.LastInteractionResponse?.Values["note"]);
    }

    [Fact]
    public void Fake_queued_response_auto_completes_BeginInteraction()
    {
        var openTap = new FakeOpenTapSession();
        var request = new OperatorInteractionRequest
        {
            Id = "req-1",
            Message = "Enter serial",
            Fields =
            [
                new OperatorInteractionField { Id = "serial", Label = "Serial", Required = true },
            ],
        };
        openTap.InteractionResponses.Enqueue(OperatorInteractionResponse.Continue("req-1", new Dictionary<string, string>
        {
            ["serial"] = "SN-99",
        }));
        openTap.BeginInteraction(request);
        Assert.False(openTap.IsAwaitingOperator);
        Assert.Equal("SN-99", openTap.LastInteractionResponse?.Values["serial"]);
    }
}
