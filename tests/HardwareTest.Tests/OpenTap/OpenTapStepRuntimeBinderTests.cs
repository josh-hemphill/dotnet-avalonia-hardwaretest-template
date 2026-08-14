using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class OpenTapStepRuntimeBinderTests
{
    [Fact]
    public void StepRuntimeBinder_attaches_distinct_runtimes()
    {
        var runtimeA = new RecordingStepRuntime();
        var runtimeB = new RecordingStepRuntime();
        var groupA = new TestGroupStep { Name = "GroupA" };
        groupA.ChildTestSteps.Add(new PublishBandScalarStep { Name = "A" });
        var groupB = new TestGroupStep { Name = "GroupB" };
        groupB.ChildTestSteps.Add(new PublishBandScalarStep { Name = "B" });

        StepRuntimeBinder.Attach(groupA, runtimeA);
        StepRuntimeBinder.Attach(groupB, runtimeB);

        Assert.Same(runtimeA, ((IRuntimeAwareStep)groupA.ChildTestSteps[0]).Runtime);
        Assert.Same(runtimeA, ((IRuntimeAwareStep)groupA).Runtime);
        Assert.Same(runtimeB, ((IRuntimeAwareStep)groupB.ChildTestSteps[0]).Runtime);
        Assert.Same(runtimeB, ((IRuntimeAwareStep)groupB).Runtime);
        Assert.NotSame(
            ((IRuntimeAwareStep)groupA.ChildTestSteps[0]).Runtime,
            ((IRuntimeAwareStep)groupB.ChildTestSteps[0]).Runtime);
    }

    private sealed class RecordingStepRuntime : IStepRuntime
    {
        public void WaitIfPaused()
        {
        }

        public OperatorInteractionResponse RequestInteraction(OperatorInteractionRequest request)
            => OperatorInteractionResponse.Continue(request.Id);
    }
}
