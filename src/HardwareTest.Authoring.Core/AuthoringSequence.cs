using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public enum SequenceSection
{
    Setup,
    Measure,
    Cleanup,
}

public enum SequenceRowKind
{
    Header,
    Setup,
    Metric,
    Repeat,
    Raw,
    Cleanup,
}

/// One row in the sectioned Setup / Measure / Cleanup list. Not a TreeView node.
public sealed record SequenceRow(
    string Key,
    SequenceSection Section,
    SequenceRowKind Kind,
    bool IsSelectable,
    int Depth,
    int Indent,
    string Label,
    string Detail,
    IReadOnlyList<int> IndexPath);

/// Column titles and purpose copy for the Program tab (Avalonia-free).
public static class AuthoringChrome
{
    public const string ProgramsTitle = "Programs";
    public const string ProgramsPurpose = "Pick which locked plan you are editing.";
    public const string SequenceTitle = "Sequence";
    public const string SequencePurpose =
        "Run order the compiler writes as Setup, the measure suite, and Cleanup.";
    public const string InspectorTitle = "Inspector";
    public const string InspectorPurpose =
        "Edit this step’s metric, algorithm, or prompt — not the whole program.";
    public const string PreviewTitle = "Operator preview";
    public const string PreviewPurpose =
        "How this metric will look on the operator board, from canned samples or a recording — not Execute.";
    public const string ProgramSettingsTitle = "Program settings";
    public const string ProgramSettingsPurpose =
        "Session, DUT, reports, and instrument slots for this program. They are not metrics.";
    public const string CatalogsPurpose =
        "Report kinds, program kinds, required DUT fields, and instrument slots come from this workspace and the open programs — not the in-repo demos. Add a token to use it on this program.";
    public const string RequiredFieldsPurpose =
        "Require the operator session fields this program needs. serial, partNumber, revision, and operator map to the sidecar flags; add other ids for later operator surfaces.";
    public const string RecipeAddHint =
        "Add recipe appends a getting-started type. Other OpenTAP steps stay Raw after you edit the TapPlan in TUI.";
    public const string SettingsTitle = "Settings";
    public const string SettingsPurpose =
        "Theme and workstation prefs for this engineer PC. Not operator appliance policy.";
    public const string LastWorkspaceOffer = "Open last workspace";
    public const string RemoveSelectedTitle = "Remove selected";
    public const string RemoveSelectedPurpose =
        "Remove the selected Setup, measure, Repeat, or Raw row. Repeat unwraps its children. Safe Shutdown turns Cleanup off.";
    public const string RemoveProgramTitle = "Remove program";
    public const string RemoveProgramPurpose =
        "Drop this program from the session. Deletes its TapPlan and sidecar when those files exist.";
    public const string SetupHeader = "Setup";
    public const string SetupPurpose = "Identity and operator prompts before measurements.";
    public const string MeasureHeader = "Measure";
    public const string MeasurePurpose = "Metrics and repeats the operator will see.";
    public const string CleanupHeader = "Cleanup";
    public const string CleanupPurpose = "Safe Shutdown after the suite (unless sidecar excludes it).";
    public const string EmptyMeasureHint =
        "Measure is empty. Add a recipe to the sequence (Acquire, Mean GTE, Formula).";
    public const string TestGroupHint =
        "Setup, measure, and Cleanup groups are written when you save the plan.";
}

/// Flattens ProgramDraft into a sectioned, indented list (no TreeView).
public static class AuthoringSequence
{
    public const int IndentPerDepth = 16;

    public static IReadOnlyList<SequenceRow> Flatten(ProgramDraft? draft)
    {
        if (draft is null)
        {
            return [];
        }

        var rows = new List<SequenceRow>
        {
            Header(SequenceSection.Setup, AuthoringChrome.SetupHeader, AuthoringChrome.SetupPurpose),
        };
        for (var i = 0; i < draft.Setup.Count; i++)
        {
            rows.Add(DescribeSetup(draft.Setup[i], i));
        }

        rows.Add(Header(SequenceSection.Measure, AuthoringChrome.MeasureHeader, AuthoringChrome.MeasurePurpose));
        AppendMeasure(rows, draft.Measure, []);

        rows.Add(Header(SequenceSection.Cleanup, AuthoringChrome.CleanupHeader, AuthoringChrome.CleanupPurpose));
        rows.Add(DescribeCleanup(draft.Cleanup));
        return rows;
    }

    public static bool SameKeys(IReadOnlyList<SequenceRow> left, IReadOnlyList<SequenceRow> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Key, right[i].Key, StringComparison.Ordinal)
                || left[i].Kind != right[i].Kind
                || left[i].Depth != right[i].Depth)
            {
                return false;
            }
        }

        return true;
    }

    public static int IndexOfKey(IReadOnlyList<SequenceRow> rows, string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || rows.Count == 0)
        {
            return -1;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsSelectable && string.Equals(rows[i].Key, key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public static int FirstSelectableIndex(IReadOnlyList<SequenceRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsSelectable)
            {
                return i;
            }
        }

        return -1;
    }

    public static int IndexOfTopLevelMeasure(IReadOnlyList<SequenceRow> rows, int measureIndex)
    {
        if (measureIndex < 0)
        {
            return -1;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Section != SequenceSection.Measure
                || !row.IsSelectable
                || row.IndexPath.Count != 1
                || row.IndexPath[0] != measureIndex)
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    public static MeasureNode? ResolveMeasure(ProgramDraft draft, IReadOnlyList<int> path)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (path.Count == 0)
        {
            return null;
        }

        IReadOnlyList<MeasureNode> nodes = draft.Measure;
        MeasureNode? current = null;
        foreach (var index in path)
        {
            if (index < 0 || index >= nodes.Count)
            {
                return null;
            }

            current = nodes[index];
            nodes = current is RepeatNode repeat ? repeat.Children : [];
        }

        return current;
    }

    public static SetupAction? ResolveSetup(ProgramDraft draft, IReadOnlyList<int> path)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (path.Count != 1 || path[0] < 0 || path[0] >= draft.Setup.Count)
        {
            return null;
        }

        return draft.Setup[path[0]];
    }

    public static IReadOnlyList<MeasureNode> MutateMeasure(
        IReadOnlyList<MeasureNode> nodes,
        IReadOnlyList<int> path,
        Func<MeasureNode, MeasureNode> mutate)
        => MutateMeasure(nodes, path, mutate, depth: 0);

    /// Drop the node at path. A Repeat at that path unwraps its children into the parent list.
    public static IReadOnlyList<MeasureNode> RemoveMeasure(
        IReadOnlyList<MeasureNode> nodes,
        IReadOnlyList<int> path)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (path.Count == 0)
        {
            return nodes;
        }

        if (path.Count == 1)
        {
            return RemoveAt(nodes, path[0]);
        }

        var parent = path.Take(path.Count - 1).ToArray();
        var child = path[^1];
        return MutateMeasure(nodes, parent, node => node is RepeatNode repeat
            ? repeat with { Children = RemoveAt(repeat.Children, child) }
            : node);
    }

    public static bool CanRemove(SequenceRow? row, ProgramDraft? draft)
    {
        if (row is null || !row.IsSelectable || draft is null)
        {
            return false;
        }

        return row.Kind switch
        {
            SequenceRowKind.Setup or SequenceRowKind.Metric or SequenceRowKind.Repeat or SequenceRowKind.Raw => true,
            SequenceRowKind.Cleanup => draft.Cleanup.IncludeSafeShutdown,
            _ => false,
        };
    }

    private static IReadOnlyList<MeasureNode> MutateMeasure(
        IReadOnlyList<MeasureNode> nodes,
        IReadOnlyList<int> path,
        Func<MeasureNode, MeasureNode> mutate,
        int depth)
    {
        if (path.Count == 0 || depth >= path.Count)
        {
            return nodes;
        }

        var index = path[depth];
        if (index < 0 || index >= nodes.Count)
        {
            return nodes;
        }

        var copy = nodes.ToArray();
        if (depth == path.Count - 1)
        {
            copy[index] = mutate(copy[index]);
            return copy;
        }

        if (copy[index] is RepeatNode repeat)
        {
            copy[index] = repeat with
            {
                Children = MutateMeasure(repeat.Children, path, mutate, depth + 1),
            };
        }

        return copy;
    }

    private static IReadOnlyList<MeasureNode> RemoveAt(IReadOnlyList<MeasureNode> nodes, int index)
    {
        if (index < 0 || index >= nodes.Count)
        {
            return nodes;
        }

        var result = new List<MeasureNode>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            if (i != index)
            {
                result.Add(nodes[i]);
                continue;
            }

            if (nodes[i] is RepeatNode repeat)
            {
                result.AddRange(repeat.Children);
            }
        }

        return result;
    }

    private static SequenceRow Header(SequenceSection section, string label, string detail)
        => new(
            $"header:{section.ToString().ToLowerInvariant()}",
            section,
            SequenceRowKind.Header,
            false,
            0,
            0,
            label,
            detail,
            []);

    private static SequenceRow DescribeSetup(SetupAction action, int index)
    {
        var (label, detail) = action switch
        {
            IdentitySetup identity => ("Identity Check", identity.InstrumentSlot),
            OperatorPromptSetup prompt => ("Operator Prompt", prompt.Name),
            OperatorInputSetup input => ("Operator Input", input.Name),
            _ => (action.GetType().Name, string.Empty),
        };
        return new SequenceRow(
            $"setup:{index}",
            SequenceSection.Setup,
            SequenceRowKind.Setup,
            true,
            0,
            0,
            label,
            detail,
            [index]);
    }

    private static void AppendMeasure(
        List<SequenceRow> rows,
        IReadOnlyList<MeasureNode> nodes,
        IReadOnlyList<int> prefix)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var path = prefix.Count == 0 ? [i] : prefix.Concat([i]).ToArray();
            var depth = prefix.Count;
            var indent = depth * IndentPerDepth;
            switch (nodes[i])
            {
                case MetricNode metric:
                    rows.Add(new SequenceRow(
                        MeasureKey(path),
                        SequenceSection.Measure,
                        SequenceRowKind.Metric,
                        true,
                        depth,
                        indent,
                        metric.Metric.Name,
                        $"{metric.Metric.DisplayRole} · {metric.Metric.ChannelKey}",
                        path));
                    break;
                case RepeatNode repeat:
                    rows.Add(new SequenceRow(
                        MeasureKey(path),
                        SequenceSection.Measure,
                        SequenceRowKind.Repeat,
                        true,
                        depth,
                        indent,
                        $"Repeat x{repeat.Count}",
                        $"{repeat.Children.Count} step(s)",
                        path));
                    AppendMeasure(rows, repeat.Children, path);
                    break;
                case RawStepNode raw:
                    rows.Add(new SequenceRow(
                        MeasureKey(path),
                        SequenceSection.Measure,
                        SequenceRowKind.Raw,
                        true,
                        depth,
                        indent,
                        raw.TypeName,
                        "Raw TUI step",
                        path));
                    break;
                default:
                    rows.Add(new SequenceRow(
                        MeasureKey(path),
                        SequenceSection.Measure,
                        SequenceRowKind.Raw,
                        true,
                        depth,
                        indent,
                        nodes[i].GetType().Name,
                        string.Empty,
                        path));
                    break;
            }
        }
    }

    private static SequenceRow DescribeCleanup(CleanupPolicy cleanup)
    {
        var included = cleanup.IncludeSafeShutdown;
        return new SequenceRow(
            "cleanup",
            SequenceSection.Cleanup,
            SequenceRowKind.Cleanup,
            true,
            0,
            0,
            included ? "Safe Shutdown" : "Cleanup skipped",
            included ? cleanup.InstrumentSlot : "Sidecar excludes Safe Shutdown from Run Selected",
            []);
    }

    private static string MeasureKey(IReadOnlyList<int> path)
        => "measure:" + string.Join('.', path);
}
