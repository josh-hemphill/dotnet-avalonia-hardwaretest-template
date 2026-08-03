using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// One node of the plan tree mirrored from the OpenTAP host for binding.
public partial class HierarchyStepViewModel : ReactiveObject
{
    public HierarchyStepViewModel(OpenTapStepNode node)
    {
        Node = node;
        Name = node.Name;
        Path = node.Path;
        Id = node.Id;
        IsStage = node.IsStage;
        IsExpanded = node.IsStage || node.Children.Count > 0;
        Children = new ObservableCollection<HierarchyStepViewModel>(
            node.Children.Select(c => new HierarchyStepViewModel(c)));
        SyncFromNode();
    }

    public OpenTapStepNode Node { get; }
    public string Id { get; }
    public string Name { get; }
    public string Path { get; }
    public bool IsStage { get; }
    public ObservableCollection<HierarchyStepViewModel> Children { get; }

    [Reactive] private string _statusText = "Pending";
    [Reactive] private string _verdict = "NotSet";
    [Reactive] private bool _enabled = true;
    [Reactive] private string? _keyValue;
    [Reactive] private string? _extra;
    [Reactive] private string _attemptsText = string.Empty;
    [Reactive] private bool _isExpanded;
    [Reactive] private string _chipText = "Pending";
    [Reactive] private string _progressText = string.Empty;
    [Reactive] private double _progressPercent;
    [Reactive] private int _completedLeaves;
    [Reactive] private int _totalLeaves;
    [Reactive] private int _failedLeaves;

    public void SyncFromNode()
    {
        StatusText = Node.StatusText;
        Verdict = Node.Verdict;
        Enabled = Node.Enabled;
        KeyValue = Node.KeyValue;
        Extra = Node.KeyValue;
        ChipText = StatusChip.FromStatus(StatusText, Verdict);
        foreach (var child in Children)
        {
            child.SyncFromNode();
        }
    }

    public void ExpandAll()
    {
        IsExpanded = true;
        foreach (var child in Children)
        {
            child.ExpandAll();
        }
    }
}

/// A stage / section entry in the right-hand scope lists.
public partial class StageItemViewModel : ReactiveObject
{
    public StageItemViewModel(HierarchyStepViewModel? step, string displayName)
    {
        Step = step;
        DisplayName = displayName;
        Path = step?.Path;
    }

    public HierarchyStepViewModel? Step { get; }
    public string DisplayName { get; }
    public string? Path { get; }

    [Reactive] private string _statusText = "Pending";
    [Reactive] private string _verdict = "NotSet";
    [Reactive] private string? _keyValue;
    [Reactive] private string _chipText = "Pending";
    [Reactive] private string _progressText = "0/0";
    [Reactive] private double _progressPercent;
    [Reactive] private int _completedLeaves;
    [Reactive] private int _totalLeaves;
    [Reactive] private int _failedLeaves;
}

/// Depth-first walk and identity lookup helpers over the mirrored plan tree.
public static class StepHierarchy
{
    public static IEnumerable<HierarchyStepViewModel> Flatten(IEnumerable<HierarchyStepViewModel> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Flatten(root.Children))
            {
                yield return child;
            }
        }
    }

    /// Resolves a step by path, then id, then name — the order the host reports them.
    public static HierarchyStepViewModel? Find(
        IReadOnlyList<HierarchyStepViewModel> flat,
        string? stepId,
        string? stepPath,
        string? stepName)
    {
        if (!string.IsNullOrWhiteSpace(stepPath))
        {
            var byPath = flat.FirstOrDefault(s =>
                string.Equals(s.Path, stepPath, StringComparison.OrdinalIgnoreCase));
            if (byPath is not null)
            {
                return byPath;
            }
        }

        if (!string.IsNullOrWhiteSpace(stepId))
        {
            var byId = flat.FirstOrDefault(s =>
                string.Equals(s.Id, stepId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(stepName))
        {
            return flat.FirstOrDefault(s =>
                string.Equals(s.Name, stepName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public static bool IsWithin(HierarchyStepViewModel scope, HierarchyStepViewModel leaf)
        => ReferenceEquals(scope, leaf)
           || leaf.Path.StartsWith(scope.Path + "/", StringComparison.OrdinalIgnoreCase);
}
