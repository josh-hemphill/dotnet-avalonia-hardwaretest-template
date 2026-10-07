using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class OperatorInteractionFakeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sequential_simulated_interactions_can_reuse_a_consumed_request_id(bool queued)
    {
        var openTap = new FakeOpenTapSession();
        var request = OperatorInteractionRequest.ConfirmOnly("Seat fixture");
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var response = OperatorInteractionResponse.Continue(request.Id,
                new Dictionary<string, string> { ["attempt"] = iteration.ToString() });
            if (queued) openTap.InteractionResponses.Enqueue(response);
            openTap.BeginInteraction(request);
            if (!queued)
            {
                Assert.True(openTap.IsAwaitingOperator);
                openTap.Resume(response);
            }

            Assert.False(openTap.IsAwaitingOperator);
            Assert.Null(openTap.PendingInteraction);
            Assert.Same(response, openTap.LastInteractionResponse);
            Assert.Equal(iteration.ToString(), openTap.LastInteractionResponse!.Values["attempt"]);
        }
    }

    [Fact]
    public void Aborted_simulated_interaction_is_consumed_before_the_request_id_is_reused()
    {
        var openTap = new FakeOpenTapSession();
        var request = OperatorInteractionRequest.ConfirmOnly("Seat fixture");
        openTap.BeginInteraction(request);
        openTap.Abort();
        Assert.False(openTap.IsAwaitingOperator);
        Assert.Null(openTap.PendingInteraction);
        Assert.Equal(request.Id, openTap.LastInteractionResponse?.RequestId);
        Assert.True(openTap.LastInteractionResponse!.Cancelled);

        openTap.BeginInteraction(request);
        Assert.True(openTap.IsAwaitingOperator);
        openTap.Resume();
        Assert.False(openTap.IsAwaitingOperator);
        Assert.Equal(request.Id, openTap.LastInteractionResponse?.RequestId);
        Assert.False(openTap.LastInteractionResponse!.Cancelled);
    }

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
