using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    // Allocation reserves both presentation aliases and concrete runtime outputs; collision
    // preflight compares actual outputs by producer identity so existing imports remain editable.
    internal static IReadOnlyList<string> AuthoringOutputChannels(IReadOnlyList<MeasureNode> nodes)
        => AuthoringOutputs(nodes, includePresentation: true).Select(output => output.Channel)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static void RequireUniqueNewOutputs(ProgramDraft before, ProgramDraft after)
    {
        var existing = AuthoringOutputs(AuthoringFormulaDeployment.Project(before).Measure, includePresentation: false);
        var outputs = AuthoringOutputs(AuthoringFormulaDeployment.Project(after).Measure, includePresentation: false);
        foreach (var output in outputs)
        {
            if (existing.Any(old => old.NodeId == output.NodeId &&
                string.Equals(old.Channel, output.Channel, StringComparison.OrdinalIgnoreCase))) continue;
            if (outputs.Any(other => other.NodeId != output.NodeId &&
                string.Equals(other.Channel, output.Channel, StringComparison.OrdinalIgnoreCase)))
                throw new AuthoringWorkspaceException($"Output '{output.Channel}' already has a producer. This publisher's runtime output cannot be made unique by changing its presentation channel. Choose a configurable publisher or remove the conflicting producer.");
        }
    }

    private static IReadOnlyList<(Guid NodeId, string Channel)> AuthoringOutputs(IReadOnlyList<MeasureNode> nodes, bool includePresentation)
    {
        var outputs = new List<(Guid, string)>();
        void Add(Guid id, string? channel)
        {
            if (!string.IsNullOrWhiteSpace(channel) && !outputs.Any(output => output.Item1 == id &&
                string.Equals(output.Item2, channel, StringComparison.OrdinalIgnoreCase))) outputs.Add((id, channel));
        }
        void StepOutputs(ITestStep step)
        {
            if (includePresentation) Add(step.Id, OpenTapPresentation.TryReadMixin(step)?.ChannelKey);
            switch (step)
            {
                case ChannelAverageStep average: Add(step.Id, RuntimeChannel(average.Channel, step.Name)); break;
                case ApplyTransferFunctionStep filter: Add(step.Id, RuntimeChannel(filter.Channel, step.Name)); break;
                case PublishBandScalarStep scalar: Add(step.Id, scalar.MetricName); break;
                case MeanGteStep: Add(step.Id, "Mean"); break;
                case ReportStationHealthStep:
                    Add(step.Id, ReportStationHealthStep.OffsetMetric);
                    Add(step.Id, ReportStationHealthStep.AgeMetric);
                    break;
                default: Add(step.Id, SampleChannel(step)); break;
            }
            if (step is PublishSeriesComplianceStep or BitSweepAcquireStep)
            {
                Add(step.Id, "series.inband.pct");
                Add(step.Id, "series.excursion.max");
                Add(step.Id, "series.outband.ms");
            }
        }
        void RawOutputs(ITestStep step)
        {
            if (!step.Enabled) return;
            if (step is not (TestGroupStep or RepeatLoopStep) && !AuthoringFunctionCatalog.TryGetByStep(step, out _))
                throw new AuthoringWorkspaceException($"Opaque outputs of '{step.Name}' are unknown; new output channels cannot be allocated safely.");
            StepOutputs(step);
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
                        if (includePresentation) Add(node.NodeId, metric.Metric.ChannelKey);
                        if (AuthoringPublisherStep(metric.Metric) is { } step)
                        {
                            step.Id = node.NodeId;
                            StepOutputs(step);
                        }
                        break;
                    default: throw new AuthoringWorkspaceException("Unknown node outputs cannot be allocated safely.");
                }
            }
        }
        Visit(nodes);
        return outputs;
    }

    private static string RuntimeChannel(string channel, string name) => string.IsNullOrWhiteSpace(channel) ? name : channel;

    // String settings share the compiler's casing/enumeration semantics, without parsing
    // unrelated unfinished numeric settings or assigning/connecting instruments.
    private static void ApplyAuthoringStringSettings(ITestStep step, IReadOnlyDictionary<string, string> settings)
        => ApplySettings(step, settings.Where(pair => step.GetType().GetProperty(pair.Key,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
            ?.PropertyType == typeof(string)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    private static ITestStep? AuthoringPublisherStep(MetricDraft metric)
    {
        MetricSource source;
        try { source = ResolveSource(metric); }
        catch (AuthoringWorkspaceException) { return null; } // Unlowered exploration drafts have no runtime publisher.
        if (source is TransferFunctionAlgorithm tf)
            return new ApplyTransferFunctionStep { Name = metric.Name, Channel = metric.ChannelKey, InputChannel = tf.InputChannelKey };
        var (function, settings) = source switch
        {
            MeasureSource measure => (measure.FunctionId, measure.Settings),
            AlgorithmSource algorithm => (algorithm.AlgorithmId, algorithm.Settings),
            _ => (string.Empty, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>())
        };
        if (!AuthoringFunctionCatalog.TryGet(function, out _))
            throw new AuthoringWorkspaceException($"Opaque outputs of function '{function}' are unknown; new output channels cannot be allocated safely.");
        var step = AuthoringFunctionCatalog.CreateStep(function);
        step.Name = metric.Name;
        ApplyAuthoringStringSettings(step, settings);
        step.GetType().GetProperty("Channel")?.SetValue(step, metric.ChannelKey);
        return step;
    }

    internal static IReadOnlyList<string> AuthoringInputChannels(MetricSource source)
    {
        if (source is TransferFunctionAlgorithm transfer) return [transfer.InputChannelKey];
        if (source is ExpressionAlgorithm expression)
            return FormulaParser.TryParse(expression.Source, out var ast, out _)
                ? FormulaExprWalk.Identifiers(ast!.Root).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : expression.InputChannelKeys;
        var (function, settings) = source switch
        {
            MeasureSource measure => (measure.FunctionId, measure.Settings),
            AlgorithmSource algorithm => (algorithm.AlgorithmId, algorithm.Settings),
            _ => (string.Empty, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>())
        };
        if (source is AlgorithmSource { AlgorithmId: AuthoringFunctionIds.BasicChannelAverage } average)
            return average.InputChannelKeys;
        if (function is not (AuthoringFunctionIds.BasicChannelAverage or AuthoringFunctionIds.BasicApplyTransferFunction)) return [];
        var step = AuthoringFunctionCatalog.CreateStep(function);
        ApplyAuthoringStringSettings(step, settings);
        return step switch
        {
            ChannelAverageStep channelAverage => [channelAverage.InputChannel],
            ApplyTransferFunctionStep filter => [filter.InputChannel],
            _ => []
        };
    }
}
