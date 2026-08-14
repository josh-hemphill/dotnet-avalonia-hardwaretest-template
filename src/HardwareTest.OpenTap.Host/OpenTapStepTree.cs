using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Plan-tree helpers shared by the session façade (not run-state).
internal static class OpenTapStepTree
{
    public static IEnumerable<ITestStep> Flatten(ITestStepParent parent)
    {
        foreach (var child in parent.ChildTestSteps)
        {
            yield return child;
            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }

    public static HardwareDut? FindDut(TestPlan plan)
        => Flatten(plan).OfType<IdentityCheckStep>().Select(s => s.Dut).FirstOrDefault();

    public static bool IsInSubtree(ITestStep candidate, ITestStep root)
    {
        if (ReferenceEquals(candidate, root) || candidate.Id == root.Id)
        {
            return true;
        }

        return Flatten(root).Any(s => s.Id == candidate.Id);
    }

    /// True when candidate is an ancestor group that must stay enabled for selected to execute.
    public static bool IsAncestorOf(ITestStep candidate, ITestStep selected)
        => Flatten(candidate).Any(s => s.Id == selected.Id);

    public static OpenTapStepNode? FindNode(IEnumerable<OpenTapStepNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var nested = FindNode(node.Children, id);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    public static OpenTapStepNode? FindNodeByName(IEnumerable<OpenTapStepNode> nodes, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (var node in nodes)
        {
            if (string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var nested = FindNodeByName(node.Children, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    public static void ResetLiveState(IEnumerable<OpenTapStepNode> nodes, HashSet<string>? resetIds = null)
    {
        foreach (var node in nodes)
        {
            if (resetIds is null || resetIds.Contains(node.Id))
            {
                node.StatusText = "Pending";
                node.Verdict = "NotSet";
                node.KeyValue = null;
            }

            ResetLiveState(node.Children, resetIds);
        }
    }

    public static bool IsPathUnderAnyScope(string? path, IReadOnlyList<string> scopeRoots)
    {
        if (string.IsNullOrWhiteSpace(path) || scopeRoots.Count == 0)
        {
            return false;
        }

        foreach (var root in scopeRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefix = root.TrimEnd('/') + "/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static List<OpenTapStepNode> Build(TestPlan plan)
    {
        OpenTapStepNode Map(ITestStep step, string parentPath)
        {
            var path = string.IsNullOrEmpty(parentPath) ? step.Name : $"{parentPath}/{step.Name}";
            var node = new OpenTapStepNode
            {
                Id = step.Id.ToString(),
                Name = step.Name,
                Path = path,
                Enabled = step.Enabled,
                IsStage = step is TestGroupStep || step.ChildTestSteps.Count > 0,
            };
            foreach (var child in step.ChildTestSteps)
            {
                node.Children.Add(Map(child, path));
            }

            return node;
        }

        return plan.ChildTestSteps.Select(s => Map(s, string.Empty)).ToList();
    }
}
