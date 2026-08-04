using System;
using System.Collections.Generic;
using System.Linq;
using HardwareTest.Core.Runs;

namespace HardwareTest.Features.RunTest;

/// Row rebuilds, rollup and live status projection for <see cref="StepTreeViewModel"/>.
public partial class StepTreeViewModel
{
    private void ApplyStageFilter(string? preserveStepPath = null)
    {
        RebuildSubsections();
        RebuildVisibleStepList();
        ResolveSelectedStep(preserveStepPath);
        ApplyAttemptTexts();
        _refreshHero();
    }

    private void RebuildSubsections()
    {
        Subsections.Clear();
        var stageStep = SelectedStage?.Step;
        if (stageStep is null)
        {
            HasSubsections = false;
            ClearNestedSubsectionsInternal();
            return;
        }

        foreach (var child in stageStep.Children.Where(c => c.IsStage || c.Children.Count > 0))
        {
            var item = new StageItemViewModel(child, child.Name);
            HierarchyRollup.ApplyToStage(item, _fullHierarchy);
            Subsections.Add(item);
        }

        HasSubsections = Subsections.Count > 0;
        ClearNestedSubsectionsInternal();
    }

    private void ClearNestedSubsectionsInternal()
    {
        NestedSubsections.Clear();
        HasNestedSubsections = false;
        _suppressNestedFilter = true;
        try
        {
            SelectedNestedSubsection = null;
        }
        finally
        {
            _suppressNestedFilter = false;
        }
    }

    private void RebuildNestedSubsections()
    {
        NestedSubsections.Clear();
        var subStep = SelectedSubsection?.Step;
        if (subStep is null)
        {
            HasNestedSubsections = false;
            return;
        }

        foreach (var child in subStep.Children.Where(c => c.IsStage || c.Children.Count > 0))
        {
            var item = new StageItemViewModel(child, child.Name);
            HierarchyRollup.ApplyToStage(item, _fullHierarchy);
            NestedSubsections.Add(item);
        }

        HasNestedSubsections = NestedSubsections.Count > 0;
    }

    private void RebuildVisibleStepList()
    {
        var scope = ActiveScopeStep;
        var scopeUsesSections = scope is not null
            && SelectedSubsection is null
            && SelectedNestedSubsection is null
            && scope.Children.Any(c => c.IsStage || c.Children.Count > 0);

        var items = StepRowBuilder.Build(_fullHierarchy, scope, scopeUsesSections, StepStatusFilter, StepSearchText);

        // Keep list-item identity when only live status/key fields changed. Clearing the
        // ObservableCollection under the pointer recreates ListBox rows and jumps focus/scroll.
        if (HasSameStepListStructure(StepListItems, items))
        {
            RefreshBreadcrumb();
            SyncSelectedStepListItem();
            return;
        }

        StepListItems.Clear();
        foreach (var item in items)
        {
            StepListItems.Add(item);
        }

        StepRows.Clear();
        foreach (var item in items)
        {
            if (!item.IsHeader && item.Step is not null)
            {
                StepRows.Add(item.Step);
            }
        }

        RefreshBreadcrumb();
        SyncSelectedStepListItem();
    }

    private static bool HasSameStepListStructure(
        IReadOnlyList<StepListItemViewModel> current,
        IReadOnlyList<StepListItemViewModel> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            var a = current[i];
            var b = next[i];
            if (a.IsHeader != b.IsHeader
                || !string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal)
                || !ReferenceEquals(a.Step, b.Step))
            {
                return false;
            }
        }

        return true;
    }

    /// Keeps a usable selection after a rebuild: preferred path, then the same instance, then same path, then first row.
    public void ResolveSelectedStep(string? preserveStepPath = null)
    {
        var listSteps = StepListItems.Where(i => i.Step is not null).Select(i => i.Step!).ToList();
        var flat = StepRows.Count > 0 ? StepRows.ToList() : listSteps;
        if (flat.Count == 0)
        {
            flat = HierarchyRollup.EnumerateLeaves(_fullHierarchy).ToList();
        }

        if (!string.IsNullOrWhiteSpace(preserveStepPath))
        {
            var match = listSteps.FirstOrDefault(s =>
                            string.Equals(s.Path, preserveStepPath, StringComparison.OrdinalIgnoreCase))
                        ?? flat.FirstOrDefault(s =>
                            string.Equals(s.Path, preserveStepPath, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                SelectedStep = match;
                SyncSelectedStepListItem();
                return;
            }
        }

        if (SelectedStep is not null && listSteps.Any(s => ReferenceEquals(s, SelectedStep)))
        {
            SyncSelectedStepListItem();
            return;
        }

        if (SelectedStep is not null)
        {
            var byPath = listSteps.FirstOrDefault(s =>
                             string.Equals(s.Path, SelectedStep.Path, StringComparison.OrdinalIgnoreCase))
                         ?? flat.FirstOrDefault(s =>
                             string.Equals(s.Path, SelectedStep.Path, StringComparison.OrdinalIgnoreCase));
            if (byPath is not null)
            {
                SelectedStep = byPath;
                SyncSelectedStepListItem();
                return;
            }
        }

        SelectedStep = flat.FirstOrDefault();
        SyncSelectedStepListItem();
    }

    private void SyncSelectedStepListItem()
    {
        var match = StepListItems.FirstOrDefault(i => ReferenceEquals(i.Step, SelectedStep));
        if (match is null || ReferenceEquals(SelectedStepListItem, match))
        {
            return;
        }

        SelectedStepListItem = match;
    }

    public void RollupParentStatuses()
    {
        HierarchyRollup.Apply(_fullHierarchy);
        foreach (var stage in Stages)
        {
            HierarchyRollup.ApplyToStage(stage, _fullHierarchy);
        }

        foreach (var subsection in Subsections)
        {
            HierarchyRollup.ApplyToStage(subsection, _fullHierarchy);
        }

        foreach (var nested in NestedSubsections)
        {
            HierarchyRollup.ApplyToStage(nested, _fullHierarchy);
        }

        ReorderStagesByPriority();
        RefreshSuiteSummary();
        RebuildVisibleStepList();
    }

    /// Re-mirrors host node state onto the tree (used on every UI flush during a run).
    public void SyncFromNodes()
    {
        foreach (var root in _fullHierarchy)
        {
            root.SyncFromNode();
        }

        RollupParentStatuses();
    }

    /// Projects the finished run's per-step verdicts onto the tree.
    public void ApplyStepResults(IEnumerable<StepResultRecord> steps)
    {
        var flat = StepHierarchy.Flatten(_fullHierarchy).ToList();
        foreach (var step in steps)
        {
            var vm = StepHierarchy.Find(flat, step.StepId, step.StepPath, step.StepType);
            if (vm is null)
            {
                continue;
            }

            var status = ResolveStatusText(step);
            vm.StatusText = status;
            vm.Verdict = status;
            vm.Node.StatusText = status;
            vm.Node.Verdict = status;
            vm.ChipText = StatusChip.FromStatus(status);
        }

        RollupParentStatuses();
        _refreshHero();
        RefreshSuiteSummary();
        MaybeAutoFocusFail();
    }

    /// Applies one streaming status update; returns the touched node so the coordinator can update the hero.
    public HierarchyStepViewModel? ApplyLiveStep(
        string? stepId,
        string? stepPath,
        string? statusText,
        string? verdict,
        string? keyValue)
    {
        if (string.IsNullOrWhiteSpace(stepId)
            && string.IsNullOrWhiteSpace(stepPath)
            && string.IsNullOrWhiteSpace(statusText)
            && string.IsNullOrWhiteSpace(verdict)
            && keyValue is null)
        {
            return null;
        }

        var vm = StepHierarchy.Find(
            StepHierarchy.Flatten(_fullHierarchy).ToList(),
            stepId,
            stepPath,
            stepName: null);
        if (vm is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            vm.StatusText = statusText;
            vm.Node.StatusText = statusText;
        }

        if (!string.IsNullOrWhiteSpace(verdict))
        {
            vm.Verdict = verdict;
            vm.Node.Verdict = verdict;
        }

        if (keyValue is not null)
        {
            vm.KeyValue = keyValue;
            vm.Extra = keyValue;
            vm.Node.KeyValue = keyValue;
        }

        vm.ChipText = StatusChip.FromStatus(vm.StatusText, vm.Verdict);
        return vm;
    }

    public void ApplyAttemptTexts()
    {
        foreach (var root in _fullHierarchy)
        {
            ApplyAttemptTexts(root);
        }
    }

    public void ClearAttemptTexts()
    {
        foreach (var root in _fullHierarchy)
        {
            ClearAttemptTexts(root);
        }
    }

    private void ApplyAttemptTexts(HierarchyStepViewModel node)
    {
        if (_attemptLookup(node.Path) is { } ledger)
        {
            node.AttemptsText = ledger.Display;
        }

        foreach (var child in node.Children)
        {
            ApplyAttemptTexts(child);
        }
    }

    private static void ClearAttemptTexts(HierarchyStepViewModel node)
    {
        node.AttemptsText = string.Empty;
        foreach (var child in node.Children)
        {
            ClearAttemptTexts(child);
        }
    }

    private static string ResolveStatusText(StepResultRecord step)
    {
        if (!step.Passed)
        {
            return string.IsNullOrWhiteSpace(step.Message) ? "Fail" : step.Message;
        }

        var status = string.IsNullOrWhiteSpace(step.Message)
            || string.Equals(step.Message, "NotSet", StringComparison.OrdinalIgnoreCase)
            || StatusChip.FromStatus(step.Message) == "Pending"
            ? "Pass"
            : step.Message!;
        return StatusChip.FromStatus(status) == "Pending" ? "Pass" : status;
    }

    private void ReorderStagesByPriority()
    {
        if (Stages.Count < 2)
        {
            return;
        }

        var ordered = Stages
            .Select((stage, index) => (stage, index))
            .OrderBy(x => StagePriority(x.stage))
            .ThenBy(x => x.index)
            .Select(x => x.stage)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            if (!ReferenceEquals(Stages[i], ordered[i]))
            {
                Stages.Move(Stages.IndexOf(ordered[i]), i);
            }
        }
    }

    private static int StagePriority(StageItemViewModel stage)
    {
        if (stage.Step is null)
        {
            return 0;
        }

        return stage.ChipText == "Fail" ? 1 : 2;
    }

    private void RefreshSuiteSummary()
    {
        var chips = HierarchyRollup.EnumerateLeaves(_fullHierarchy)
            .Select(l => StatusChip.FromStatus(l.StatusText, l.Verdict))
            .ToList();
        SuitePassedCount = chips.Count(c => c == "Pass");
        SuiteFailedCount = chips.Count(c => c == "Fail");
        SuitePendingCount = chips.Count(c => c is not "Pass" and not "Fail");
    }

    private void RefreshBreadcrumb()
    {
        var parts = new List<string>
        {
            SelectedStage?.Step is null ? "Entire program" : SelectedStage.DisplayName,
        };

        if (SelectedSubsection is not null)
        {
            parts.Add(SelectedSubsection.DisplayName);
        }

        if (SelectedNestedSubsection is not null)
        {
            parts.Add(SelectedNestedSubsection.DisplayName);
        }

        BreadcrumbText = string.Join(" › ", parts);

        var activeItem = SelectedNestedSubsection ?? SelectedSubsection ?? SelectedStage;
        BreadcrumbDetailText = activeItem is null
            ? string.Empty
            : $"({HierarchyRollup.FormatProgressText(activeItem.CompletedLeaves, activeItem.TotalLeaves, activeItem.FailedLeaves)})";
    }
}
