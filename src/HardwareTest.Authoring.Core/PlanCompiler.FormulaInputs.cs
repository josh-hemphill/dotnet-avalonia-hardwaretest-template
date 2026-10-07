using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    // Uses the same concrete publisher capabilities and sequence validation as the compiled plan.
    internal static void ValidateFormulaInputs(ProgramDraft draft)
        => BindSequence(RequirementSteps(draft.Measure), bindProducer: false, incompleteFields: draft.AuthoringState.IncompleteNumericText);

    internal static void ValidateFormulaInput(IReadOnlyList<MeasureNode> nodes, string outputChannel, AuthoringDocumentState? state = null)
        => BindSequence(RequirementSteps(nodes, RelevantInputNodes(nodes, outputChannel)), bindProducer: false, onlyOutput: outputChannel, incompleteFields: state?.IncompleteNumericText);

    // Authoring edits need the real dependency validator, but unrelated unfinished numeric
    // fields must not be parsed while checking one consumer's preceding input closure.
    internal static void ValidateAuthoringSequenceInput(IReadOnlyList<MeasureNode> nodes, string outputChannel, AuthoringDocumentState state)
        => BindSequence(RequirementSteps(nodes, RelevantInputNodes(nodes, outputChannel), structural: true),
            bindProducer: false, onlyOutput: outputChannel, incompleteFields: state.IncompleteNumericText);

    private static HashSet<Guid> RelevantInputNodes(IReadOnlyList<MeasureNode> nodes, string outputChannel)
    {
        var relevant = new HashSet<Guid>();
        void Scope(IReadOnlyList<MeasureNode> siblings)
        {
            void Include(int index)
            {
                if (!relevant.Add(siblings[index].NodeId)) return;
                var inputs = siblings[index] switch
                {
                    MetricNode metric => AuthoringInputChannels(metric.Metric.Source),
                    RawStepNode raw => LoadRawStep(raw) switch
                    {
                        ChannelAverageStep average when average.Enabled => new[] { average.InputChannel },
                        ApplyTransferFunctionStep filter when filter.Enabled => new[] { filter.InputChannel },
                        _ => Array.Empty<string>()
                    },
                    _ => Array.Empty<string>()
                };
                foreach (var input in inputs)
                    for (var previous = 0; previous < index; previous++)
                    {
                        var channel = siblings[previous] switch
                        {
                            MetricNode producer => producer.Metric.ChannelKey,
                            RawStepNode raw => DeclaredChannel(LoadRawStep(raw)),
                            _ => null
                        };
                        if (string.Equals(channel, input, StringComparison.OrdinalIgnoreCase)) Include(previous);
                    }
            }
            for (var index = 0; index < siblings.Count; index++)
            {
                if (siblings[index] is RepeatNode repeat) Scope(repeat.Children);
                if (siblings[index] is MetricNode metric &&
                    string.Equals(metric.Metric.ChannelKey, outputChannel, StringComparison.OrdinalIgnoreCase)) Include(index);
            }
        }
        Scope(nodes);
        return relevant;
    }

    internal static double? ScalarMeanPreviewExample(ProgramDraft draft, string outputChannel)
    {
        double? example = null;
        BindSequence(RequirementSteps(draft.Measure, RelevantInputNodes(draft.Measure, outputChannel)), bindProducer: false, onlyOutput: outputChannel,
            incompleteFields: draft.AuthoringState.IncompleteNumericText, scalarExample: value => example = value);
        return example;
    }

    private static IEnumerable<ITestStep> RequirementSteps(IEnumerable<MeasureNode> nodes, ISet<Guid>? relevant = null, bool structural = false)
    {
        foreach (var node in nodes)
        {
            ITestStep? step = null;
            if (node is RawStepNode raw) step = LoadRawStep(raw);
            else if (node is RepeatNode repeat)
            {
                var loop = new RepeatLoopStep();
                foreach (var child in RequirementSteps(repeat.Children, relevant, structural)) loop.ChildTestSteps.Add(child);
                step = loop;
            }
            else if (node is MetricNode metric)
            {
                MetricSource source;
                try { source = ResolveSource(metric.Metric); }
                catch (AuthoringWorkspaceException error) when (structural &&
                    error.Message.StartsWith(AuthoringCompileCodes.MissingLimits, StringComparison.Ordinal) &&
                    metric.Metric.Source is ExpressionAlgorithm expression && FormulaParser.TryParse(expression.Source, out var ast, out _) &&
                    ast!.Root is CallExpr { Name: "mean", Args: [IdentExpr] })
                {
                    // Sequence edits validate the input shape of an incomplete mean, never its
                    // criterion. Strict status, preview and compilation retain the real limits.
                    source = ResolveSource(metric.Metric with { Limits = new LimitSpec(null, null, 0) });
                }
                catch (AuthoringWorkspaceException) { continue; } // Lowering itself owns unsupported expression errors.
                if (source is TransferFunctionAlgorithm tf)
                    step = new ApplyTransferFunctionStep { InputChannel = tf.InputChannelKey, Channel = metric.Metric.ChannelKey, TsSeconds = tf.TsSeconds };
                else
                {
                    var id = source switch { MeasureSource m => m.FunctionId, AlgorithmSource a => a.AlgorithmId, _ => string.Empty };
                    if (!AuthoringFunctionCatalog.TryGet(id, out _)) continue;
                    step = AuthoringFunctionCatalog.CreateStep(id);
                    var settings = source switch { MeasureSource m => m.Settings, AlgorithmSource a => a.Settings, _ => new Dictionary<string, string>() };
                    try
                    {
                        if (relevant is null || relevant.Contains(node.NodeId)) ApplySettings(step, settings);
                        else ApplyAuthoringStringSettings(step, settings);
                    }
                    catch (Exception error) when (relevant is not null && error is FormatException or OverflowException)
                    {
                        throw new AuthoringWorkspaceException($"BUILD_INCOMPLETE: Deployment settings on step '{node.NodeId:D}' contain incomplete numeric text.", error);
                    }
                    step.GetType().GetProperty("Channel")?.SetValue(step, metric.Metric.ChannelKey);
                    if (step is ChannelAverageStep average && source is AlgorithmSource algorithm)
                        average.InputChannel = AuthoringFunctionCatalog.InputChannelIssue(id, algorithm.InputChannelKeys) is null
                            ? algorithm.InputChannelKeys[0] : string.Empty;
                    PresentationAttach.Apply(step, metric.Metric);
                }
            }
            if (step is not null) { step.Id = node.NodeId; yield return step; }
        }
    }

    private static string? SampleChannel(ITestStep step) => step switch
    {
        AcquireVoltageStep acquire => acquire.Channel,
        BitSweepAcquireStep sweep => sweep.Channel,
        PublishTimedSampleStep timed => timed.Channel,
        ApplyTransferFunctionStep filter => filter.Channel,
        _ => null,
    };

    private static string? DeclaredChannel(ITestStep step)
        => SampleChannel(step) ?? step.GetType().GetProperty("Channel")?.GetValue(step) as string
            ?? OpenTapPresentation.TryReadMixin(step)?.ChannelKey
            ?? (step is PublishBandScalarStep scalar ? scalar.MetricName : null);

    private static void RequireFilterGrid(IReadOnlyList<ITestStep> producers, ApplyTransferFunctionStep filter)
    {
        try
        {
            if (producers.All(step => step is PublishTimedSampleStep))
            {
                var elapsed = producers.Cast<PublishTimedSampleStep>().Select(step => step.ElapsedMs ?? double.NaN).ToArray();
                if (elapsed.Any(value => !double.IsFinite(value)))
                    throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.TfMissingElapsed}: '{filter.InputChannel}' does not publish ElapsedMs.");
                TransferFunctionGrid.RequireUniform(elapsed, filter.TsSeconds);
                return;
            }
            if (producers.Any(step => SampleChannel(step) is null))
                throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.TfMissingElapsed}: '{filter.InputChannel}' does not publish ElapsedMs.");
            if (producers.Count != 1)
                throw new AuthoringWorkspaceException($"MISSING_CHANNEL: Input '{filter.InputChannel}' has ambiguous Sample producers in this sequence.");
            var (count, ts) = producers[0] switch
            {
                AcquireVoltageStep acquire => (acquire.SampleCount, acquire.IntervalMs / 1000.0),
                BitSweepAcquireStep sweep => (sweep.BitCount, sweep.IntervalMs / 1000.0),
                ApplyTransferFunctionStep previous => (2, previous.TsSeconds),
                _ => (0, 0d),
            };
            if (count < 2) throw new InvalidOperationException("TF_GRID: elapsed series length is < 2.");
            TransferFunctionGrid.RequireUniform([0, ts * 1000], filter.TsSeconds);
        }
        catch (InvalidOperationException error) { throw new AuthoringWorkspaceException(error.Message, error); }
    }

    private static void RequireSampleInput(Dictionary<string, List<ITestStep>> producers, string input,
        bool filter, out IReadOnlyList<ITestStep> matches)
    {
        if (!producers.TryGetValue(input, out var found) || found.Count == 0)
            throw new AuthoringWorkspaceException($"MISSING_CHANNEL: Input '{input}' requires a supported preceding Sample producer in the same sequence.");
        matches = found;
        if (!filter && found.Any(step => SampleChannel(step) is null))
            throw new AuthoringWorkspaceException($"MISSING_CHANNEL: Input '{input}' does not have a supported Sample publisher.");
        if (!filter && found.Count != 1)
            throw new AuthoringWorkspaceException($"MISSING_CHANNEL: Input '{input}' has ambiguous Sample producers in this sequence.");
    }
}
