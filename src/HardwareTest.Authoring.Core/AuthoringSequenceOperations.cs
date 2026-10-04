using System.Text.RegularExpressions;

namespace HardwareTest.Authoring;

/// Transactions operate on the authoritative draft and preserve identities of surviving nodes.
public static class AuthoringSequenceOperations
{
    public static string UniqueChannel(string channel, ISet<string> used)
    {
        var candidate = channel;
        for (var suffix = 2; !used.Add(candidate); suffix++) candidate = $"{channel}_{suffix}";
        return candidate;
    }

    internal static MetricDraft WithOutputChannel(MetricDraft metric, string channel)
    {
        IReadOnlyDictionary<string, string> Settings(IReadOnlyDictionary<string, string> settings, string function)
        {
            var output = settings.ToDictionary(pair => pair.Key, pair =>
                (pair.Key.Equals("Channel", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("MetricName", StringComparison.OrdinalIgnoreCase))
                ? channel : pair.Value,
                settings is Dictionary<string, string> dictionary ? dictionary.Comparer : StringComparer.Ordinal);
            if (function == AuthoringFunctionIds.BasicPublishBandScalar &&
                !output.Keys.Any(key => key.Equals("MetricName", StringComparison.OrdinalIgnoreCase)))
                output.Add("MetricName", channel);
            return output;
        }
        return metric with
        {
            ChannelKey = channel,
            Source = metric.Source switch
            {
                MeasureSource measure => measure with { Settings = Settings(measure.Settings, measure.FunctionId) },
                AlgorithmSource algorithm => algorithm with { Settings = Settings(algorithm.Settings, algorithm.AlgorithmId) },
                _ => metric.Source
            }
        };
    }

    public static ProgramDraft Insert(ProgramDraft draft, string recipeId, SequenceRow? target,
        bool before, string? instrumentSlot = null)
    {
        if (target is not null) target = CurrentTarget(draft, target);
        if (recipeId == AuthoringRecipeIds.TestGroup) throw Fail("Test Groups are generated on Save.");
        if (recipeId == AuthoringRecipeIds.Repeat) return Repeat(draft, target);
        if (recipeId == AuthoringRecipeIds.Shutdown) return AuthoringRecipeCatalog.Apply(draft, recipeId, instrumentSlot);
        var context = recipeId is AuthoringRecipeIds.Formula or AuthoringRecipeIds.TransferFunction
            ? draft with { Measure = PrecedingMeasure(draft, target, before) }
            : draft;
        var appended = AuthoringRecipeCatalog.Apply(context, recipeId, instrumentSlot);
        if (ReferenceEquals(appended, context)) return draft;
        if (appended.Setup.Count > draft.Setup.Count)
        {
            RequireSection(target, SequenceSection.Setup);
            if (appended.Setup[^1] is IdentitySetup identity)
            {
                var instrument = draft.Instruments.FirstOrDefault(i => string.Equals(i.SlotName, identity.InstrumentSlot, StringComparison.OrdinalIgnoreCase))
                    ?? throw Fail("Select a configured instrument slot for Identity Check.");
                AuthoringInstrumentCatalog.EnsureCompatible(instrument.TypeId, AuthoringFunctionIds.BasicIdentityCheck);
            }
            var at = target is null ? draft.Setup.Count : target.IndexPath[0] + (before ? 0 : 1);
            return draft with { Setup = [.. draft.Setup.Take(at), appended.Setup[^1], .. draft.Setup.Skip(at)] };
        }
        RequireSection(target, SequenceSection.Measure);
        var node = appended.Measure[^1];
        if (node is MetricNode metric)
        {
            var used = PlanCompiler.AuthoringOutputChannels(draft.Measure).ToHashSet(StringComparer.OrdinalIgnoreCase);
            metric = metric with { Metric = WithOutputChannel(metric.Metric, UniqueChannel(metric.Metric.ChannelKey, used)) };
            node = metric;
            var binding = metric.Metric.Source switch
            {
                MeasureSource measure => (measure.FunctionId, measure.InstrumentSlot),
                AlgorithmSource algorithm => (algorithm.AlgorithmId, algorithm.InstrumentSlot),
                _ => (string.Empty, (string?)null)
            };
            if (AuthoringFunctionCatalog.TryGet(binding.Item1, out var spec) && spec.NeedsInstrument)
            {
                var instrument = draft.Instruments.FirstOrDefault(i => string.Equals(i.SlotName, binding.Item2, StringComparison.OrdinalIgnoreCase))
                    ?? throw Fail("Select a configured instrument slot for this recipe.");
                AuthoringInstrumentCatalog.EnsureCompatible(instrument.TypeId, binding.Item1);
            }
        }
        var updated = draft with
        {
            Measure = EditSiblings(draft.Measure, target?.IndexPath ?? [], siblings =>
        {
            var at = target is null ? siblings.Count : target.IndexPath[^1] + (before ? 0 : 1);
            return [.. siblings.Take(at), node, .. siblings.Skip(at)];
        })
        };
        PlanCompiler.RequireUniqueNewOutputs(draft, updated);
        ValidateOrder(draft, updated);
        return updated;
    }

    public static ProgramDraft Repeat(ProgramDraft draft, SequenceRow? target)
    {
        if (target is not null) target = CurrentTarget(draft, target);
        if (target is not { Section: SequenceSection.Measure, Kind: SequenceRowKind.Metric or SequenceRowKind.Repeat })
            throw Fail("Select a supported measurement or loop to repeat.");
        var node = AuthoringSequence.ResolveMeasure(draft, target.IndexPath)!;
        RequireTransparent(draft, node);
        if (AuthoringDependencyIndex.Build(draft).HasOpaqueReferences)
            throw Fail("Opaque dependencies prevent safely changing loop scope.");
        // MutateMeasure preserves the replaced identity; wrapping needs a fresh loop identity.
        var updated = draft with
        {
            Measure = EditSiblings(draft.Measure, target.IndexPath, siblings =>
            siblings.Select((child, index) => index == target.IndexPath[^1] ? new RepeatNode(2, [node]) : child).ToArray())
        };
        ValidateOrder(draft, updated);
        return updated;
    }

    public static ProgramDraft Rename(ProgramDraft draft, SequenceRow target, string name)
    {
        target = CurrentTarget(draft, target);
        name = name.Trim();
        if (name.Length == 0) throw Fail("Enter a step name.");
        if (target.Kind == SequenceRowKind.Metric)
        {
            var updated = draft with
            {
                Measure = AuthoringSequence.MutateMeasure(draft.Measure, target.IndexPath,
                node => node is MetricNode metric ? metric with { Metric = metric.Metric with { Name = name } } : node)
            };
            if (AuthoringSequence.ResolveMeasure(draft, target.IndexPath) is MetricNode selected &&
                string.IsNullOrWhiteSpace(selected.Metric.ChannelKey))
                PlanCompiler.RequireUniqueNewOutputs(draft, updated);
            return updated;
        }
        if (target.Kind == SequenceRowKind.Setup)
        {
            var action = AuthoringSequence.ResolveSetup(draft, target.IndexPath);
            SetupAction renamed = action switch
            {
                OperatorPromptSetup prompt => prompt with { Name = name },
                OperatorInputSetup input => input with { Name = name },
                _ => throw Fail("This generated step name cannot be changed.")
            };
            return draft with { Setup = draft.Setup.Select(a => a.NodeId == renamed.NodeId ? renamed : a).ToArray() };
        }
        throw Fail("Select a measurement, operator prompt or input to rename.");
    }

    public static ProgramDraft Move(ProgramDraft draft, SequenceRow target, int direction)
    {
        target = CurrentTarget(draft, target);
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (target.Section == SequenceSection.Setup)
        {
            var actions = draft.Setup.ToArray();
            var index = target.IndexPath[0];
            var next = index + direction;
            if (next < 0 || next >= actions.Length) return draft;
            (actions[index], actions[next]) = (actions[next], actions[index]);
            return draft with { Setup = actions };
        }
        if (target.Section != SequenceSection.Measure) throw Fail("Cleanup cannot be moved.");
        var changed = false;
        var measure = EditSiblings(draft.Measure, target.IndexPath, siblings =>
        {
            var index = target.IndexPath[^1];
            var next = index + direction;
            if (next < 0 || next >= siblings.Count) return siblings;
            if (AuthoringDependencyIndex.Build(draft with { Setup = [], Measure = siblings }).HasOpaqueReferences)
                throw Fail("Opaque dependencies prevent safely changing sequence order.");
            RequireTransparent(draft, siblings[index]);
            RequireTransparent(draft, siblings[next]);
            var copy = siblings.ToArray();
            (copy[index], copy[next]) = (copy[next], copy[index]);
            changed = true;
            return copy;
        });
        if (!changed) return draft;
        var updated = draft with { Measure = measure };
        ValidateOrder(draft, updated);
        return updated;
    }

    public static ProgramDraft Duplicate(ProgramDraft draft, SequenceRow target)
    {
        target = CurrentTarget(draft, target);
        if (target.Kind == SequenceRowKind.Setup)
        {
            var action = AuthoringSequence.ResolveSetup(draft, target.IndexPath)!;
            if (action is IdentitySetup) throw Fail("Identity checks already exist for this instrument.");
            var clone = action with { NodeId = Guid.NewGuid() };
            var state = draft.AuthoringState.Clone();
            CopyState(draft, state, action.NodeId, clone.NodeId);
            var at = target.IndexPath[0] + 1;
            return draft with { Setup = [.. draft.Setup.Take(at), clone, .. draft.Setup.Skip(at)], AuthoringState = state };
        }
        if (target.Section != SequenceSection.Measure) throw Fail("Select a supported step to duplicate.");
        var selected = AuthoringSequence.ResolveMeasure(draft, target.IndexPath)!;
        RequireTransparent(draft, selected);
        var used = PlanCompiler.AuthoringOutputChannels(draft.Measure).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var channels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var metric in AuthoringRecipeCatalog.EnumerateMetrics([selected]))
        {
            if (!channels.TryAdd(metric.ChannelKey, UniqueChannel(metric.ChannelKey, used)))
                throw Fail($"Channel '{metric.ChannelKey}' is ambiguous inside this subtree.");
            if (AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure).Count(m => string.Equals(m.ChannelKey, metric.ChannelKey, StringComparison.OrdinalIgnoreCase)) != 1)
                throw Fail($"Channel '{metric.ChannelKey}' has multiple producers; duplication is unsafe.");
        }
        var authoring = draft.AuthoringState.Clone();
        var originals = new Dictionary<Guid, Guid>();
        MeasureNode Clone(MeasureNode node)
        {
            var id = Guid.NewGuid();
            originals[id] = node.NodeId;
            CopyState(draft, authoring, node.NodeId, id);
            return node switch
            {
                RepeatNode repeat => repeat with { NodeId = id, Children = repeat.Children.Select(Clone).ToArray() },
                MetricNode metric => metric with
                {
                    NodeId = id,
                    Metric = WithOutputChannel(metric.Metric, channels[metric.Metric.ChannelKey]) with
                    { Source = RemapSource(WithOutputChannel(metric.Metric, channels[metric.Metric.ChannelKey]).Source, channels) }
                },
                _ => throw Fail("Opaque steps cannot be duplicated.")
            };
        }
        var duplicate = Clone(selected);
        var updated = draft with
        {
            AuthoringState = authoring,
            Measure = EditSiblings(draft.Measure, target.IndexPath,
            siblings => [.. siblings.Take(target.IndexPath[^1] + 1), duplicate, .. siblings.Skip(target.IndexPath[^1] + 1)])
        };
        PlanCompiler.RequireUniqueNewOutputs(draft, updated);
        ValidateOrder(draft, updated, originals);
        return updated;
    }

    private static void CopyState(ProgramDraft draft, AuthoringDocumentState state, Guid oldId, Guid newId)
    {
        if (draft.AuthoringState.FormulaIntent.TryGetValue(oldId, out var intent)) state.FormulaIntent[newId] = intent;
        var prefix = $"{oldId:D}/";
        foreach (var pair in draft.AuthoringState.IncompleteNumericText.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)))
            state.IncompleteNumericText[$"{newId:D}/{pair.Key[prefix.Length..]}"] = pair.Value;
    }

    private static MetricSource RemapSource(MetricSource source, IReadOnlyDictionary<string, string> channels)
    {
        string Map(string key) => channels.GetValueOrDefault(key, key);
        IReadOnlyDictionary<string, string> Settings(string function, IReadOnlyDictionary<string, string> settings)
            => function is AuthoringFunctionIds.BasicChannelAverage or AuthoringFunctionIds.BasicApplyTransferFunction
                ? settings.ToDictionary(pair => pair.Key, pair => pair.Key.Equals("InputChannel", StringComparison.OrdinalIgnoreCase)
                    ? Map(pair.Value) : pair.Value, settings is Dictionary<string, string> dictionary ? dictionary.Comparer : StringComparer.Ordinal)
                : settings;
        return source switch
        {
            AlgorithmSource algorithm => algorithm with
            { InputChannelKeys = algorithm.InputChannelKeys.Select(Map).ToArray(), Settings = Settings(algorithm.AlgorithmId, algorithm.Settings) },
            TransferFunctionAlgorithm transfer => transfer with { InputChannelKey = Map(transfer.InputChannelKey) },
            ExpressionAlgorithm expression when FormulaParser.TryParse(expression.Source, out _, out _) => expression with
            {
                InputChannelKeys = expression.InputChannelKeys.Select(Map).ToArray(),
                Source = Regex.Replace(expression.Source, @"(?<![\p{L}\p{N}_.])[\p{L}_][\p{L}\p{N}_]*(?:\.[\p{L}_][\p{L}\p{N}_]*)*",
                    match => Regex.IsMatch(expression.Source[(match.Index + match.Length)..], @"^\s*\(") ? match.Value : Map(match.Value))
            },
            ExpressionAlgorithm => throw Fail("The formula cannot be parsed; its references cannot be duplicated safely."),
            MeasureSource measure => measure with { Settings = Settings(measure.FunctionId, measure.Settings) },
            _ => throw Fail("Unknown source references cannot be duplicated safely.")
        };
    }

    private static IReadOnlyList<MeasureNode> PrecedingMeasure(ProgramDraft draft, SequenceRow? target, bool before)
    {
        RequireSection(target, SequenceSection.Measure);
        var siblings = target is { IndexPath.Count: > 1 }
            ? ((RepeatNode)AuthoringSequence.ResolveMeasure(draft, target.IndexPath.Take(target.IndexPath.Count - 1).ToArray())!).Children
            : draft.Measure;
        var at = target is null ? siblings.Count : target.IndexPath[^1] + (before ? 0 : 1);
        // A completed nested loop is a separate scope, not an input producer for its parent.
        return siblings.Take(at).Where(node => node is MetricNode or RawStepNode).ToArray();
    }

    private static SequenceRow CurrentTarget(ProgramDraft draft, SequenceRow target)
        => target.NodeId is { } id
            ? AuthoringSequence.Flatten(draft).FirstOrDefault(row => row.NodeId == id)
                ?? throw Fail("The selected step no longer exists. Select an insertion point again.")
            : throw Fail("Select a step as the insertion point.");

    private static void RequireSection(SequenceRow? target, SequenceSection section)
    {
        if (target is not null && target.Section != section)
            throw Fail($"Select an insertion point in {section}, or choose End of section.");
    }

    private static void RequireTransparent(ProgramDraft draft, MeasureNode node)
    {
        var subtree = draft with { Setup = [], Measure = [node] };
        if (AuthoringDependencyIndex.Build(subtree).HasOpaqueReferences)
            throw Fail("Opaque step dependencies are unknown; this operation cannot safely change their order or loop scope.");
    }

    private static IReadOnlyList<MeasureNode> EditSiblings(IReadOnlyList<MeasureNode> nodes, IReadOnlyList<int> path,
        Func<IReadOnlyList<MeasureNode>, IReadOnlyList<MeasureNode>> edit)
    {
        if (path.Count <= 1) return edit(nodes);
        return AuthoringSequence.MutateMeasure(nodes, path.Take(path.Count - 1).ToArray(),
            node => node is RepeatNode repeat ? repeat with { Children = edit(repeat.Children) } : node);
    }

    internal static void ValidateOrder(ProgramDraft before, ProgramDraft after, IReadOnlyDictionary<Guid, Guid>? originals = null)
    {
        var existing = OrderIssues(before).ToHashSet(StringComparer.Ordinal);
        var introduced = OrderIssues(after).FirstOrDefault(issue =>
        {
            var comparable = issue;
            foreach (var pair in originals ?? new Dictionary<Guid, Guid>())
                comparable = comparable.Replace(pair.Key.ToString("D"), pair.Value.ToString("D"), StringComparison.OrdinalIgnoreCase);
            return !existing.Contains(comparable);
        });
        if (introduced is not null) throw Fail(introduced[(introduced.IndexOf('|') + 1)..]);
    }

    private static IReadOnlyList<string> OrderIssues(ProgramDraft draft)
    {
        var issues = new List<string>();
        void Validate(IReadOnlyList<MeasureNode> siblings)
        {
            var preceding = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rawPreceding = false;
            foreach (var node in siblings)
            {
                if (node is RepeatNode repeat) { Validate(repeat.Children); continue; }
                if (node is RawStepNode) { rawPreceding = true; continue; }
                if (node is not MetricNode metric) continue;
                var dependency = AuthoringDependencyIndex.Build(draft with { Setup = [], Measure = [node] }).Nodes[0];
                var inputs = dependency.InputChannels;
                if (metric.Metric.Source is ExpressionAlgorithm expression && FormulaParser.TryParse(expression.Source, out var ast, out _))
                    inputs = inputs.Concat(FormulaExprWalk.Identifiers(ast!.Root)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var input in inputs)
                    if (!preceding.Contains(input) && !rawPreceding) issues.Add($"{node.NodeId:D}|'{metric.Metric.Name}' requires preceding channel '{input}' in the same loop scope.");
                preceding.Add(metric.Metric.ChannelKey);
            }
        }
        Validate(draft.Measure);
        var projected = AuthoringFormulaDeployment.Project(draft);
        foreach (var metric in AuthoringRecipeCatalog.EnumerateMetrics(projected.Measure))
        {
            if (PlanCompiler.AuthoringInputChannels(metric.Source).Count == 0) continue;
            try { PlanCompiler.ValidateAuthoringSequenceInput(projected.Measure, metric.ChannelKey, projected.AuthoringState); }
            catch (AuthoringWorkspaceException error) { issues.Add(error.Message); }
        }
        return issues;
    }

    private static AuthoringWorkspaceException Fail(string message) => new(message);
}
