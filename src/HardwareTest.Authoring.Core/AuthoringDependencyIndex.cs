namespace HardwareTest.Authoring;

public sealed record AuthoringNodeDependency(Guid NodeId, string? ProducedChannel,
    IReadOnlyList<string> InputChannels, IReadOnlyList<string> InstrumentSlots, bool IsOpaque);

/// References retain node identities across rename and reorder, including nested repeats.
public sealed class AuthoringDependencyIndex
{
    private AuthoringDependencyIndex(IReadOnlyList<AuthoringNodeDependency> nodes) => Nodes = nodes;
    public IReadOnlyList<AuthoringNodeDependency> Nodes { get; }
    public bool HasOpaqueReferences => Nodes.Any(node => node.IsOpaque);

    public static AuthoringDependencyIndex Build(ProgramDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var nodes = new List<AuthoringNodeDependency>();
        foreach (var action in draft.Setup)
        {
            nodes.Add(action switch
            {
                IdentitySetup identity => new(action.NodeId, null, [], [identity.InstrumentSlot], false),
                OperatorPromptSetup or OperatorInputSetup => new(action.NodeId, null, [], [], false),
                _ => throw Unsupported(action)
            });
        }
        AddMeasure(nodes, draft.Measure);
        nodes.Add(new(draft.Cleanup.NodeId, null, [], AuthoringCleanup.ResolveSlots(draft).ToArray(), false));
        return new(nodes.ToArray());
    }

    private static void AddMeasure(List<AuthoringNodeDependency> nodes, IEnumerable<MeasureNode> measure)
    {
        foreach (var node in measure)
        {
            switch (node)
            {
                case RepeatNode repeat:
                    nodes.Add(new(repeat.NodeId, null, [], [], false));
                    AddMeasure(nodes, repeat.Children);
                    break;
                case RawStepNode raw:
                    nodes.Add(new(raw.NodeId, null, [], [], true));
                    break;
                case MetricNode metric:
                    var (inputs, instruments) = metric.Metric.Source switch
                    {
                        MeasureSource source => (Array.Empty<string>(), new[] { source.InstrumentSlot! }),
                        AlgorithmSource source => (source.InputChannelKeys.ToArray(),
                            !AuthoringFunctionCatalog.HasInstrumentDependency(source.AlgorithmId) || string.IsNullOrWhiteSpace(source.InstrumentSlot) ? Array.Empty<string>() : new[] { source.InstrumentSlot! }),
                        ExpressionAlgorithm source => (source.InputChannelKeys.ToArray(), Array.Empty<string>()),
                        TransferFunctionAlgorithm source => (new[] { source.InputChannelKey }, Array.Empty<string>()),
                        _ => throw Unsupported(metric.Metric.Source)
                    };
                    var opaque = metric.Metric.Source switch
                    {
                        AlgorithmSource source => !AuthoringFunctionCatalog.TryGet(source.AlgorithmId, out var spec)
                            || !spec.IsAlgorithm || (spec.NeedsInstrument && string.IsNullOrWhiteSpace(source.InstrumentSlot)),
                        MeasureSource source => !AuthoringFunctionCatalog.TryGet(source.FunctionId, out var spec)
                            || spec.IsAlgorithm,
                        ExpressionAlgorithm source => !FormulaParser.TryParse(source.Source, out _, out _),
                        _ => false
                    };
                    nodes.Add(new(metric.NodeId, metric.Metric.ChannelKey, inputs, instruments, opaque));
                    break;
                default:
                    throw Unsupported(node);
            }
        }
    }

    private static NotSupportedException Unsupported(object value)
        => new($"Dependency indexing does not support {value.GetType().FullName}.");
}
