using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    // Bind to an earlier producer in the same sequence scope. Ambiguous or forward references fail closed.
    private static void BindChannelProducers(TestPlan plan)
    {
        BindSequence(plan.ChildTestSteps);
    }

    private static void BindSequence(IEnumerable<ITestStep> steps)
    {
        var producers = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            if (step is ChannelAverageStep average)
            {
                if (!producers.TryGetValue(average.InputChannel, out var producer))
                    throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.FormulaNoLower}: missing preceding producer '{average.InputChannel}' in this sequence.");
                average.ProducerStepId = producer;
                average.Unit = OpenTapPresentation.TryReadMixin(step)?.YUnit ?? string.Empty;
                average.Channel = OpenTapPresentation.TryReadMixin(step)?.ChannelKey ?? average.Channel;
            }
            if (step.ChildTestSteps.Count > 0) BindSequence(step.ChildTestSteps);
            var channel = OpenTapPresentation.TryReadMixin(step)?.ChannelKey;
            if (!string.IsNullOrWhiteSpace(channel)) producers[channel] = step.Id;
        }
    }
}
