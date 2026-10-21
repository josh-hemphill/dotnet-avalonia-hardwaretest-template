namespace HardwareTest.Authoring;

public static partial class AuthoringSequenceOperations
{
    public static ProgramDraft AddSection(ProgramDraft draft, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw Fail("Enter a section name, such as Power supply or Communications.");
        return draft with { Measure = [.. draft.Measure, new SectionNode(name, [])] };
    }

    /// Sections organize steps without changing execution scope. Moving into or out of a loop is prohibited.
    public static ProgramDraft MoveToSection(ProgramDraft draft, SequenceRow source, Guid? destinationId)
    {
        source = CurrentTarget(draft, source);
        if (source.Section != SequenceSection.Measure) throw Fail("Setup and Cleanup stay in their lifecycle phases.");
        var rows = AuthoringSequence.Flatten(draft);
        var destination = destinationId is null ? null : rows.FirstOrDefault(row => row.NodeId == destinationId && row.IsSection)
            ?? throw Fail("The destination section no longer exists.");
        if (destination is not null && destination.IndexPath.Take(source.IndexPath.Count).SequenceEqual(source.IndexPath))
            throw Fail("A section cannot be moved into itself or one of its children.");
        Guid? LoopScope(SequenceRow? row) => row is null ? null : rows.LastOrDefault(candidate =>
            candidate.Kind == SequenceRowKind.Repeat && candidate.IndexPath.Count < row.IndexPath.Count &&
            row.IndexPath.Take(candidate.IndexPath.Count).SequenceEqual(candidate.IndexPath))?.NodeId;
        if (LoopScope(source) != LoopScope(destination)) throw Fail("Keep steps in the same loop scope when moving between sections.");
        if (AuthoringDependencyIndex.Build(draft).HasOpaqueReferences)
            throw Fail("Opaque dependencies prevent safely moving steps between sections.");
        var selected = AuthoringSequence.ResolveMeasure(draft, source.IndexPath)!;
        IReadOnlyList<MeasureNode> Remove(IReadOnlyList<MeasureNode> nodes) => nodes.Where(node => node.NodeId != selected.NodeId)
            .Select(node => node switch
            {
                SectionNode section => section with { Children = Remove(section.Children) },
                RepeatNode repeat => repeat with { Children = Remove(repeat.Children) },
                _ => node
            }).ToArray();
        var updated = draft with { Measure = Remove(draft.Measure) };
        if (destinationId is null) updated = updated with { Measure = [.. updated.Measure, selected] };
        else
        {
            var currentDestination = AuthoringSequence.Flatten(updated).Single(row => row.NodeId == destinationId);
            updated = updated with
            {
                Measure = AuthoringSequence.MutateMeasure(updated.Measure, currentDestination.IndexPath,
                node => ((SectionNode)node) with { Children = [.. ((SectionNode)node).Children, selected] })
            };
        }
        ValidateOrder(draft, updated);
        return updated;
    }

    private static IEnumerable<MeasureNode> TransparentSections(IEnumerable<MeasureNode> nodes)
    {
        foreach (var node in nodes)
            if (node is SectionNode section)
                foreach (var child in TransparentSections(section.Children)) yield return child;
            else yield return node;
    }
}
