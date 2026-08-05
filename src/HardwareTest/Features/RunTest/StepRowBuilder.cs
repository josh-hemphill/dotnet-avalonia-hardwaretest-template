using System;
using System.Collections.Generic;
using System.Linq;

namespace HardwareTest.Features.RunTest;

/// Flattens the plan tree into the visible step list, keeping stage / section markers in plan order.
public static class StepRowBuilder
{
    public static List<StepListItemViewModel> Build(
        IReadOnlyList<HierarchyStepViewModel> fullHierarchy,
        HierarchyStepViewModel? scope,
        bool scopeUsesSections,
        string statusFilter,
        string? searchText)
    {
        var items = new List<StepListItemViewModel>();

        if (scope is null)
        {
            // Entire program: keep stage / section markers across the full hierarchy.
            foreach (var root in fullHierarchy)
            {
                AppendScopeWithMarkers(root, items, pathPrefix: null, statusFilter, searchText);
            }

            return items;
        }

        // Keep the active stage/section as a clickable header so Run Selected can target the whole scope.
        items.Add(StepListItemViewModel.Header(scope.Name, scope));

        if (scopeUsesSections)
        {
            AppendScopeWithMarkers(scope, items, pathPrefix: null, statusFilter, searchText);
            return items;
        }

        foreach (var leaf in FilterLeaves(HierarchyRollup.EnumerateLeaves([scope]), statusFilter, searchText))
        {
            items.Add(StepListItemViewModel.Leaf(leaf));
        }

        return items;
    }

    public static List<HierarchyStepViewModel> FilterLeaves(
        IEnumerable<HierarchyStepViewModel> leaves,
        string statusFilter,
        string? searchText)
    {
        IEnumerable<HierarchyStepViewModel> query = leaves;
        if (!string.Equals(statusFilter, StepStatusFilter.All, StringComparison.Ordinal))
        {
            query = query.Where(l =>
                string.Equals(StatusChip.FromStatus(l.StatusText, l.Verdict), statusFilter, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var term = searchText.Trim();
            query = query.Where(l =>
                l.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || l.Path.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    private static void AppendScopeWithMarkers(
        HierarchyStepViewModel node,
        List<StepListItemViewModel> items,
        string? pathPrefix,
        string statusFilter,
        string? searchText)
    {
        List<HierarchyStepViewModel>? pendingDirect = null;

        void FlushDirectLeaves()
        {
            if (pendingDirect is null)
            {
                return;
            }

            var leaves = FilterLeaves(pendingDirect, statusFilter, searchText);
            pendingDirect = null;
            if (leaves.Count == 0)
            {
                return;
            }

            // At the suite root, keep ungrouped leaves inline between stage blocks (no fake "suite" header
            // that would push them to the bottom). Under a named section path, group them with that header.
            if (!string.IsNullOrWhiteSpace(pathPrefix))
            {
                items.Add(StepListItemViewModel.Header(pathPrefix, node));
            }

            foreach (var leaf in leaves)
            {
                items.Add(StepListItemViewModel.Leaf(leaf));
            }
        }

        // Entire-program roots that are themselves leaves (or orphan siblings like Sweep Safe Shutdown)
        // never appear as children — include them so the step list is not empty.
        if (!node.IsStage && node.Children.Count == 0)
        {
            pendingDirect = [node];
            FlushDirectLeaves();
            return;
        }

        foreach (var child in node.Children)
        {
            var isSection = child.IsStage || child.Children.Count > 0;
            if (!isSection)
            {
                pendingDirect ??= [];
                pendingDirect.Add(child);
                continue;
            }

            FlushDirectLeaves();

            var header = string.IsNullOrWhiteSpace(pathPrefix)
                ? child.Name
                : $"{pathPrefix} / {child.Name}";

            if (child.Children.Any(c => c.IsStage || c.Children.Count > 0))
            {
                AppendScopeWithMarkers(child, items, header, statusFilter, searchText);
                continue;
            }

            var sectionLeaves = FilterLeaves(HierarchyRollup.EnumerateLeaves([child]), statusFilter, searchText);
            if (sectionLeaves.Count == 0)
            {
                continue;
            }

            items.Add(StepListItemViewModel.Header(header, child));
            foreach (var leaf in sectionLeaves)
            {
                items.Add(StepListItemViewModel.Leaf(leaf));
            }
        }

        FlushDirectLeaves();
    }
}
