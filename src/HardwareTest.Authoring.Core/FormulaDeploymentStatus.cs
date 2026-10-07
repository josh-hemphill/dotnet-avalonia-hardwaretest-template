using HardwareTest.Core.Runs;

namespace HardwareTest.Authoring;

public enum FormulaDeploymentStatusKind { DeployableRecipe, PreviewOnly, InvalidExpression, MissingRequirements }

public sealed record FormulaDeploymentStatus(FormulaDeploymentStatusKind Kind, string Label,
    string Target, string Requirements, string Message)
{
    public string RecordingEvidence { get; init; } = string.Empty;
}

/// Deployment classification uses the same parser, lowerer, criteria and clocks as compilation.
public static class FormulaDeploymentClassifier
{
    public static FormulaDeploymentStatus Classify(MetricDraft metric, ProgramDraft? draft = null,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? series = null, Guid? nodeId = null)
    {
        if (metric.Source is not ExpressionAlgorithm expression)
            throw new ArgumentException("An expression metric is required.", nameof(metric));
        if (!FormulaParser.TryParse(expression.Source, out var ast, out var error))
            return new(FormulaDeploymentStatusKind.InvalidExpression, "Invalid expression", "None", "Valid expression required.", error ?? "Invalid expression.");
        var target = ast!.Root switch
        {
            CallExpr { Name: "mean", Args: [IdentExpr] } => "Channel Average",
            FilterCallExpr => "Apply Transfer Function",
            _ => "None"
        };
        var inputs = FormulaExprWalk.Identifiers(ast.Root).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var requirements = $"Inputs: {(inputs.Length == 0 ? "none" : string.Join(", ", inputs))}. " + (ast.Root is FilterCallExpr
            ? "A supported preceding Sample producer in the same sequence; at least two samples on a uniform, strictly increasing elapsedMs grid at 0.005 s. Recording preview uses its own recorded clock."
            : target == "Channel Average" ? "Finite scalar threshold and exactly one supported preceding Sample producer in the same actual execution scope required." : "No supported deployment recipe.");
        try
        {
            _ = FormulaLowerer.Lower(expression, metric.Limits);
            AuthoringCriteria.Validate(metric);
            if (draft is not null)
            {
                var identity = nodeId ?? MetricPreviewBuilder.ResolveNodeId(draft.Measure, metric);
                var deployment = DeploymentContext(metric, draft, identity);
                PlanCompiler.ValidateFormulaInput(deployment.Measure, metric.ChannelKey, deployment.AuthoringState, identity);
            }
            return new(FormulaDeploymentStatusKind.DeployableRecipe, "Deployable recipe", target, requirements, $"Deploys as {target}.")
            { RecordingEvidence = DescribeRecording(metric, series) };
        }
        catch (AuthoringWorkspaceException ex)
        {
            if (ex.Message.StartsWith(AuthoringCompileCodes.FormulaNoLower, StringComparison.Ordinal))
                return new(FormulaDeploymentStatusKind.PreviewOnly, "Preview only", target, requirements, "Exploration can be saved and excluded from deployment. No equivalent supported recipe exists for this expression.");
            return new(FormulaDeploymentStatusKind.MissingRequirements, "Missing deployment requirements", target, requirements, ex.Message);
        }
    }

    internal static ProgramDraft DeploymentContext(MetricDraft metric, ProgramDraft draft, Guid? nodeId = null)
    {
        // Assess the selected expression even when it is exploration, preserving all other exclusions.
        var identity = nodeId ?? MetricPreviewBuilder.ResolveNodeId(draft.Measure, metric);
        var state = draft.AuthoringState.Clone();
        foreach (var node in AuthoringDependencyIndex.Build(draft).Nodes.Where(node =>
            identity is { } id ? node.NodeId == id : string.Equals(node.ProducedChannel, metric.ChannelKey, StringComparison.OrdinalIgnoreCase)))
            state.FormulaIntent.Remove(node.NodeId);
        return AuthoringFormulaDeployment.Project(draft with { AuthoringState = state });
    }

    public static string DescribeRecording(MetricDraft metric,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? series)
    {
        if (series is null || metric.Source is not ExpressionAlgorithm expression) return string.Empty;
        try
        {
            var lowered = FormulaLowerer.Lower(expression, metric.Limits, series);
            return lowered is TransferFunctionAlgorithm tf
                ? $"Selected recording: uniform grid at {tf.TsSeconds:g} s. Preview derives this period; deployment uses {FormulaLowerer.DefaultTsSeconds:g} s and validates its own source clock."
                : "Selected recording is preview evidence; it is not a saved build input.";
        }
        catch (AuthoringWorkspaceException error)
        {
            return $"Selected recording preview: {error.Message} The saved build validates deployment sources independently.";
        }
    }

}

/// Preserves source documents while producing a deployment graph with exploration nodes excluded.
public static class AuthoringFormulaDeployment
{
    public static IReadOnlyList<Guid> ExcludedNodes(ProgramDraft draft)
        => Nodes(draft.Measure).Where(node => node.Metric.Source is ExpressionAlgorithm
            && draft.AuthoringState.FormulaIntent.GetValueOrDefault(node.NodeId) == FormulaDeploymentIntent.Explore)
            .Select(node => node.NodeId).ToArray();

    public static ProgramDraft Project(ProgramDraft draft)
    {
        var excluded = ExcludedNodes(draft).ToHashSet();
        var state = draft.AuthoringState.Clone();
        foreach (var key in state.IncompleteNumericText.Keys.Where(key =>
            Guid.TryParse(key.Split('/')[0], out var id) && excluded.Contains(id)).ToArray())
            state.IncompleteNumericText.Remove(key);
        return draft with { Measure = ProjectNodes(draft.Measure, excluded), AuthoringState = state };
    }

    private static IReadOnlyList<MeasureNode> ProjectNodes(IReadOnlyList<MeasureNode> nodes, HashSet<Guid> excluded)
        => nodes.Where(node => !excluded.Contains(node.NodeId)).Select(node => node is RepeatNode repeat
            ? repeat with { Children = ProjectNodes(repeat.Children, excluded) } : node).ToArray();

    private static IEnumerable<MetricNode> Nodes(IEnumerable<MeasureNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is MetricNode metric) yield return metric;
            if (node is RepeatNode repeat)
                foreach (var child in Nodes(repeat.Children)) yield return child;
        }
    }
}
