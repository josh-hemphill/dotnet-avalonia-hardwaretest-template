namespace HardwareTest.Features.RunTest;

/// Computes all-leaves Pass rollup and progress for hierarchy / stage nodes.
public static class HierarchyRollup
{
    public static void Apply(IEnumerable<HierarchyStepViewModel> roots)
    {
        foreach (var root in roots)
        {
            RollupNode(root);
        }
    }

    public static void ApplyToStage(
        StageItemViewModel stage,
        IReadOnlyList<HierarchyStepViewModel> fullHierarchy)
    {
        var leaves = stage.Step is null
            ? EnumerateLeaves(fullHierarchy).ToList()
            : EnumerateLeaves([stage.Step]).ToList();

        var (status, completed, total, failed) = SummarizeLeaves(leaves);
        stage.StatusText = status;
        stage.Verdict = status;
        stage.ChipText = StatusChip.FromStatus(status);
        stage.CompletedLeaves = completed;
        stage.TotalLeaves = total;
        stage.FailedLeaves = failed;
        stage.ProgressPercent = total == 0 ? 0 : 100.0 * completed / total;
        stage.ProgressText = FormatProgressText(completed, total, failed);

        if (stage.Step is not null)
        {
            stage.Step.Node.StatusText = status;
            stage.Step.Node.Verdict = status;
        }
    }

    public static string FormatProgressText(int completed, int total, int failed)
        => failed > 0 ? $"{completed}/{total} · {failed}F" : $"{completed}/{total}";

    public static IEnumerable<HierarchyStepViewModel> EnumerateLeaves(IEnumerable<HierarchyStepViewModel> roots)
    {
        foreach (var root in roots)
        {
            if (root.Children.Count == 0)
            {
                yield return root;
                continue;
            }

            foreach (var leaf in EnumerateLeaves(root.Children))
            {
                yield return leaf;
            }
        }
    }

    private static void RollupNode(HierarchyStepViewModel node)
    {
        if (node.Children.Count == 0)
        {
            node.ChipText = StatusChip.FromStatus(node.StatusText, node.Verdict);
            node.TotalLeaves = 1;
            node.CompletedLeaves = IsTerminalChip(node.ChipText) ? 1 : 0;
            node.FailedLeaves = node.ChipText == "Fail" ? 1 : 0;
            node.ProgressPercent = node.CompletedLeaves * 100.0;
            node.ProgressText = string.Empty;
            node.Node.StatusText = node.StatusText;
            node.Node.Verdict = node.Verdict;
            return;
        }

        foreach (var child in node.Children)
        {
            RollupNode(child);
        }

        var leaves = EnumerateLeaves([node]).ToList();
        var (status, completed, total, failed) = SummarizeLeaves(leaves);
        node.StatusText = status;
        node.Verdict = status;
        node.ChipText = StatusChip.FromStatus(status);
        node.CompletedLeaves = completed;
        node.TotalLeaves = total;
        node.FailedLeaves = failed;
        node.ProgressPercent = total == 0 ? 0 : 100.0 * completed / total;
        node.ProgressText = FormatProgressText(completed, total, failed);
        node.Node.StatusText = status;
        node.Node.Verdict = status;
    }

    private static (string Status, int Completed, int Total, int Failed) SummarizeLeaves(
        IReadOnlyList<HierarchyStepViewModel> leaves)
    {
        var total = leaves.Count;
        if (total == 0)
        {
            return ("Pending", 0, 0, 0);
        }

        var chips = leaves
            .Select(l => StatusChip.FromStatus(l.StatusText, l.Verdict))
            .ToList();
        var completed = chips.Count(IsTerminalChip);
        var failed = chips.Count(c => c == "Fail");

        if (chips.Contains("Fail"))
        {
            return ("Fail", completed, total, failed);
        }

        if (chips.Contains("Awaiting"))
        {
            return ("Awaiting", completed, total, failed);
        }

        if (chips.Contains("Running"))
        {
            return ("Running", completed, total, failed);
        }

        if (chips.All(c => c == "Pass"))
        {
            return ("Pass", completed, total, failed);
        }

        return ("Pending", completed, total, failed);
    }

    private static bool IsTerminalChip(string chip)
        => chip is "Pass" or "Fail";
}
