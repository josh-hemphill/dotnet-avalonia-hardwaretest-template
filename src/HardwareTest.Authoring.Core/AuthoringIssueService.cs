using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed record AuthoringEditingIssue(string Code, string Message, string PlanId, Guid NodeId)
{
    public HardwareTest.OpenTap.Host.PlanContractSeverity Severity { get; init; } = HardwareTest.OpenTap.Host.PlanContractSeverity.Error;
    public string? Section { get; init; }
    public string? Field { get; init; }
    public string NavigationLabel => Section == "ProgramSettings" ? "Open program settings" : Field is null ? "Open location" : "Go to field";
}

/// Editing findings supplement compiler findings without blocking persistence.
public static class AuthoringIssueService
{
    public static IReadOnlyList<AuthoringEditingIssue> GetIssues(ProgramDraft draft) => GetIssues(draft, null);

    public static IReadOnlyList<AuthoringEditingIssue> GetIssues(ProgramDraft draft, OpenTapHome? home, string? homeResolutionError = null)
    {
        var index = AuthoringDependencyIndex.Build(draft);
        var issues = new List<AuthoringEditingIssue>();
        if (homeResolutionError is not null)
            issues.Add(new("INVALID_OPENTAP_HOME", homeResolutionError, draft.PlanId, Guid.Empty));
        if (!AuthoringSequence.HasMeasurement(draft.Measure))
            issues.Add(new("EMPTY_MEASURE", "Add a measurement and choose its hardware before deployment.", draft.PlanId, Guid.Empty));
        if (RequiredFieldIds.Contains(RequiredFieldIds.FromSidecar(draft.Sidecar), RequiredFieldIds.Serial)
            && !draft.Setup.OfType<IdentitySetup>().Any())
            issues.Add(new("MISSING_IDENTITY", "DUT serial is required; add an instrument identity check before deployment.", draft.PlanId, Guid.Empty)
            { Section = "ProgramSettings", Field = "RequireSerial" });
        foreach (var incomplete in draft.AuthoringState.IncompleteNumericText)
        {
            var parts = incomplete.Key.Split('/', 2);
            if (parts.Length != 2 || !Guid.TryParse(parts[0], out var nodeId)) continue;
            // Native consumers and formulas already use the shared compiler input finding.
            if (EnumerateMetricNodes(draft.Measure).Any(node => node.NodeId == nodeId
                && node.Metric.Source is ExpressionAlgorithm or TransferFunctionAlgorithm
                    or AlgorithmSource { AlgorithmId: AuthoringFunctionIds.BasicChannelAverage })) continue;
            var field = parts[1].StartsWith("MetricSetting:", StringComparison.Ordinal) ? parts[1]["MetricSetting:".Length..] : parts[1];
            issues.Add(new("INCOMPLETE_NUMERIC_INPUT", $"Complete {field} before compiling; saved input: '{incomplete.Value}'.", draft.PlanId, nodeId)
            { Section = "Configure", Field = field });
        }
        var sharedInputNodes = EnumerateMetricNodes(draft.Measure).Where(node => UsesCompilerInputValidation(node.Metric.Source))
            .Select(node => node.NodeId).ToHashSet();
        var channels = index.Nodes.Where(node => node.ProducedChannel is not null)
            .GroupBy(node => node.ProducedChannel!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var slots = draft.Instruments.GroupBy(instrument => instrument.SlotName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in index.Nodes.GroupBy(node => node.NodeId).Where(group => group.Count() > 1))
            foreach (var node in duplicate) Add("DUPLICATE_NODE_ID", "Another row uses this node identity.", node);
        foreach (var node in index.Nodes)
        {
            if (node.NodeId == Guid.Empty) Add("MISSING_NODE_ID", "The row has an empty node identity.", node);
            if (node.ProducedChannel is { } channel && channels[channel].Length > 1)
                Add("DUPLICATE_CHANNEL", $"Channel '{channel}' has multiple producers.", node);
            foreach (var repeated in node.InputChannels.GroupBy(input => input, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                Add("DUPLICATE_INPUT_CHANNEL", $"Input channel '{repeated.Key}' is referenced more than once.", node);
            foreach (var input in node.InputChannels)
            {
                if (!sharedInputNodes.Contains(node.NodeId) && !channels.ContainsKey(input)) Add("MISSING_CHANNEL", $"Input channel '{input}' has no supported producer.", node);
            }
            foreach (var slot in node.InstrumentSlots)
            {
                if (!slots.TryGetValue(slot, out var count)) Add("MISSING_INSTRUMENT", $"Instrument '{slot}' is not configured.", node);
                else if (count > 1) Add("DUPLICATE_INSTRUMENT", $"Instrument '{slot}' has multiple definitions.", node);
            }
            if (node.IsOpaque) Add("OPAQUE_REFERENCES", "Raw steps may contain references that cannot be indexed.", node);
        }
        foreach (var metricNode in EnumerateMetricNodes(draft.Measure))
        {
            if (metricNode.Metric.Source is AlgorithmSource algorithm
                && AuthoringFunctionCatalog.InputChannelIssue(algorithm.AlgorithmId, algorithm.InputChannelKeys) is { } inputIssue)
                issues.Add(new("INPUT_CHANNEL_CARDINALITY", inputIssue, draft.PlanId, metricNode.NodeId));
            if (metricNode.Metric.Source is ExpressionAlgorithm)
            {
                var excluded = AuthoringFormulaDeployment.ExcludedNodes(draft).Contains(metricNode.NodeId);
                var status = FormulaDeploymentClassifier.Classify(metricNode.Metric, draft, nodeId: metricNode.NodeId);
                if (excluded) issues.Add(new("FORMULA_EXCLUDED", "Exploration formula is saved unchanged and excluded from deployment.", draft.PlanId, metricNode.NodeId));
                else if (status.Kind != FormulaDeploymentStatusKind.DeployableRecipe)
                    issues.Add(new(status.Kind == FormulaDeploymentStatusKind.MissingRequirements
                        ? RequirementCode(status.Message) : "FORMULA_DEPLOYMENT", status.Message, draft.PlanId, metricNode.NodeId));
            }
            else if (metricNode.Metric.Source is TransferFunctionAlgorithm or AlgorithmSource { AlgorithmId: AuthoringFunctionIds.BasicChannelAverage })
            {
                try
                {
                    var projected = AuthoringFormulaDeployment.Project(draft);
                    PlanCompiler.ValidateFormulaInput(projected.Measure, metricNode.Metric.ChannelKey, projected.AuthoringState, metricNode.NodeId);
                }
                catch (AuthoringWorkspaceException error)
                { issues.Add(new(RequirementCode(error.Message), error.Message, draft.PlanId, metricNode.NodeId)); }
            }
            var required = metricNode.Metric.Source switch
            {
                MeasureSource m when AuthoringFunctionCatalog.TryGet(m.FunctionId, out var spec) && spec.NeedsInstrument => m.InstrumentSlot,
                AlgorithmSource a when AuthoringFunctionCatalog.TryGet(a.AlgorithmId, out var spec) && spec.NeedsInstrument => a.InstrumentSlot,
                _ => "unused"
            };
            if (string.IsNullOrWhiteSpace(required))
                issues.Add(new("MISSING_INSTRUMENT_BINDING", "Select an instrument slot for this function before compiling.", draft.PlanId, metricNode.NodeId));
        }
        foreach (var instrument in draft.Instruments)
        {
            if (!AuthoringInstrumentCatalog.TryGet(instrument.TypeId, out var adapter))
                issues.Add(new("INSTRUMENT_UNAVAILABLE", $"Instrument '{instrument.SlotName}' type '{instrument.TypeId}' has no authoring adapter; imported source is preserved.", draft.PlanId, Guid.Empty));
            else if (homeResolutionError is null && adapter.Availability(home) is { Available: false } unavailable)
                issues.Add(new("INSTRUMENT_UNAVAILABLE", unavailable.Reason!, draft.PlanId, Guid.Empty));
        }
        foreach (var instrument in draft.Instruments)
            if (!AuthoringInstrumentCatalog.CanReplace(draft, instrument.SlotName, instrument))
                issues.Add(new("INSTRUMENT_INCOMPATIBLE", $"Instrument '{instrument.SlotName}' lacks capabilities required by its bindings. Choose a compatible instrument or function.", draft.PlanId, Guid.Empty));
        return issues.Select(issue =>
        {
            if (issue.Code == AuthoringCompileCodes.MissingLimits)
            {
                var destination = AuthoringFindingNavigation.Resolve(draft, issue.NodeId,
                    new(ProgramId: draft.PlanId, NodeId: issue.NodeId, Section: "Configure", Field: "Threshold"));
                return issue with { Section = destination.Target.Section, Field = destination.Target.Field };
            }
            return issue.Code is "OPAQUE_REFERENCES" or "FORMULA_EXCLUDED"
                ? issue with { Severity = HardwareTest.OpenTap.Host.PlanContractSeverity.Warning } : issue;
        }).ToArray();

        void Add(string code, string message, AuthoringNodeDependency node)
            => issues.Add(new(code, message, draft.PlanId, node.NodeId));
    }
    private static bool UsesCompilerInputValidation(MetricSource source) => source switch
    {
        TransferFunctionAlgorithm or AlgorithmSource { AlgorithmId: AuthoringFunctionIds.BasicChannelAverage } => true,
        ExpressionAlgorithm expression when FormulaParser.TryParse(expression.Source, out var ast, out _)
            => ast!.Root is FilterCallExpr or CallExpr { Name: "mean", Args: [IdentExpr] },
        _ => false,
    };

    private static string RequirementCode(string message)
    {
        var code = message.Split(':')[0];
        return code is "MISSING_CHANNEL" or AuthoringCompileCodes.MissingLimits or AuthoringCompileCodes.TfMissingElapsed
            or AuthoringCompileCodes.TfGrid or "SAMPLE_SCOPE" ? code : "FORMULA_DEPLOYMENT";
    }

    private static IEnumerable<MetricNode> EnumerateMetricNodes(IEnumerable<MeasureNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is MetricNode metric) yield return metric;
            if (node is RepeatNode repeat)
                foreach (var child in EnumerateMetricNodes(repeat.Children)) yield return child;
        }
    }
}
