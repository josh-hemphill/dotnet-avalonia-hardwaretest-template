using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    // Share the compiler's actual publisher declarations with allocation and palette defaults.
    // Unknown raw payloads cannot promise that a newly allocated output is collision-free.
    internal static IReadOnlyList<string> AuthoringOutputChannels(IReadOnlyList<MeasureNode> nodes)
    {
        var channels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? channel)
        {
            if (!string.IsNullOrWhiteSpace(channel)) channels.Add(channel);
        }
        void RawOutputs(ITestStep step)
        {
            if (step is not (TestGroupStep or RepeatLoopStep) && !AuthoringFunctionCatalog.TryGetByStep(step, out _))
                throw new AuthoringWorkspaceException($"Opaque outputs of '{step.Name}' are unknown; new output channels cannot be allocated safely.");
            Add(DeclaredChannel(step));
            Add(OpenTapPresentation.TryReadMixin(step)?.ChannelKey);
            if (step is PublishBandScalarStep scalar) Add(scalar.MetricName);
            if (step is ReportStationHealthStep)
            {
                Add(ReportStationHealthStep.OffsetMetric);
                Add(ReportStationHealthStep.AgeMetric);
            }
            if (step is MeanGteStep) Add("Mean");
            if (step is PublishSeriesComplianceStep or BitSweepAcquireStep)
            {
                Add("series.inband.pct");
                Add("series.excursion.max");
                Add("series.outband.ms");
            }
            foreach (var child in step.ChildTestSteps) RawOutputs(child);
        }
        void Visit(IReadOnlyList<MeasureNode> measure)
        {
            foreach (var node in measure)
            {
                switch (node)
                {
                    case RepeatNode repeat: Visit(repeat.Children); break;
                    case RawStepNode raw: RawOutputs(LoadRawStep(raw)); break;
                    case MetricNode metric:
                        Add(metric.Metric.ChannelKey);
                        if (metric.Metric.Source is AlgorithmSource { AlgorithmId: AuthoringFunctionIds.BasicPublishBandScalar } scalar)
                            Add(scalar.Settings.GetValueOrDefault("MetricName"));
                        break;
                    default: throw new AuthoringWorkspaceException("Unknown node outputs cannot be allocated safely.");
                }
            }
        }
        Visit(nodes);
        return channels.ToArray();
    }
}
