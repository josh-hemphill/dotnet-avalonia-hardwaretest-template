namespace HardwareTest.Authoring;

public static partial class MetricPreviewBuilder
{
    /// Resolves the actual draft object first; value-equivalent copies require an unambiguous node.
    internal static Guid? ResolveNodeId(IReadOnlyList<MeasureNode> nodes, MetricDraft metric)
    {
        IEnumerable<MetricNode> Metrics(IReadOnlyList<MeasureNode> scope)
            => scope.SelectMany(node => node switch
            {
                MetricNode item => new[] { item },
                RepeatNode repeat => Metrics(repeat.Children),
                _ => [],
            });
        var candidates = Metrics(nodes).ToArray();
        var references = candidates.Where(node => ReferenceEquals(node.Metric, metric)).ToArray();
        if (references.Length == 1) return references[0].NodeId;
        var equals = candidates.Where(node => node.Metric == metric).ToArray();
        return equals.Length == 1 ? equals[0].NodeId : null;
    }
}
