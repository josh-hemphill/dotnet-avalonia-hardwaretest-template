namespace HardwareTest.Authoring;

public sealed record AuthoringEditingIssue(string Code, string Message, string PlanId, Guid NodeId);

/// Editing findings supplement compiler findings without blocking persistence.
public static class AuthoringIssueService
{
    public static IReadOnlyList<AuthoringEditingIssue> GetIssues(ProgramDraft draft)
    {
        var index = AuthoringDependencyIndex.Build(draft);
        var issues = new List<AuthoringEditingIssue>();
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
                if (!channels.ContainsKey(input)) Add("MISSING_CHANNEL", $"Input channel '{input}' has no supported producer.", node);
            }
            foreach (var slot in node.InstrumentSlots)
            {
                if (!slots.TryGetValue(slot, out var count)) Add("MISSING_INSTRUMENT", $"Instrument '{slot}' is not configured.", node);
                else if (count > 1) Add("DUPLICATE_INSTRUMENT", $"Instrument '{slot}' has multiple definitions.", node);
            }
            if (node.IsOpaque) Add("OPAQUE_REFERENCES", "Raw steps may contain references that cannot be indexed.", node);
        }
        return issues.ToArray();

        void Add(string code, string message, AuthoringNodeDependency node)
            => issues.Add(new(code, message, draft.PlanId, node.NodeId));
    }
}
