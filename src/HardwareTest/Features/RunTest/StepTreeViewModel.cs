using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using StepFilter = HardwareTest.Features.RunTest.StepStatusFilter;

namespace HardwareTest.Features.RunTest;

/// Plan tree, scope lists (stage / section / subsection), visible step rows, filters and fail navigation.
public partial class StepTreeViewModel : ReactiveObject
{
    private readonly List<HierarchyStepViewModel> _fullHierarchy = [];
    private readonly Func<IEnumerable<OpenTapStepNode>> _stepTreeSource;
    private readonly Func<string, StepAttemptSummary?> _attemptLookup;
    private readonly Action _openSelectedDetail;
    private readonly Action _refreshHero;
    private readonly Func<string> _getCurrentStepPath;

    private bool _suppressStageFilter;
    private bool _suppressSubsectionFilter;
    private bool _suppressNestedFilter;

    public StepTreeViewModel(
        Func<IEnumerable<OpenTapStepNode>>? stepTreeSource = null,
        Func<string, StepAttemptSummary?>? attemptLookup = null,
        Action? openSelectedDetail = null,
        Action? refreshHero = null,
        Func<string>? getCurrentStepPath = null)
    {
        _stepTreeSource = stepTreeSource ?? Array.Empty<OpenTapStepNode>;
        _attemptLookup = attemptLookup ?? (_ => null);
        _openSelectedDetail = openSelectedDetail ?? (() => { });
        _refreshHero = refreshHero ?? (() => { });
        _getCurrentStepPath = getCurrentStepPath ?? (() => string.Empty);

        NextFailCommand = ReactiveCommand.Create(() => CycleFail(forward: true));
        PrevFailCommand = ReactiveCommand.Create(() => CycleFail(forward: false));
        JumpToCurrentCommand = ReactiveCommand.Create(JumpToCurrent);
        ClearSubsectionCommand = ReactiveCommand.Create(() => { SelectedSubsection = null; });
        FilterFailCommand = ReactiveCommand.Create(FilterFail);
        ClearFailFilterCommand = ReactiveCommand.Create(() =>
        {
            StepStatusFilter = StepFilter.All;
        });
        ToggleCompactCommand = ReactiveCommand.Create(() => { CompactStepRows = !CompactStepRows; });
        FocusStepSearchCommand = ReactiveCommand.Create(
            () => RequestFocusStepSearch?.Invoke(this, EventArgs.Empty));
        SetStepFilterCommand = ReactiveCommand.Create<string>(filter =>
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                StepStatusFilter = filter;
            }
        });

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectedStage) && !_suppressStageFilter)
            {
                _suppressSubsectionFilter = true;
                try
                {
                    SelectedSubsection = null;
                }
                finally
                {
                    _suppressSubsectionFilter = false;
                }

                ApplyStageFilter();
            }
            else if (args.PropertyName == nameof(SelectedSubsection) && !_suppressSubsectionFilter)
            {
                _suppressNestedFilter = true;
                try
                {
                    SelectedNestedSubsection = null;
                }
                finally
                {
                    _suppressNestedFilter = false;
                }

                RebuildNestedSubsections();
                RebuildVisibleStepList();
                ResolveSelectedStep();
            }
            else if (args.PropertyName == nameof(SelectedNestedSubsection) && !_suppressNestedFilter)
            {
                RebuildVisibleStepList();
                ResolveSelectedStep();
            }
            else if (args.PropertyName is nameof(StepStatusFilter) or nameof(StepSearchText))
            {
                RebuildVisibleStepList();
                if (args.PropertyName == nameof(StepStatusFilter))
                {
                    this.RaisePropertyChanged(nameof(IsFilteredToFail));
                    this.RaisePropertyChanged(nameof(IsFilterAll));
                    this.RaisePropertyChanged(nameof(IsFilterFail));
                    this.RaisePropertyChanged(nameof(IsFilterRunning));
                    this.RaisePropertyChanged(nameof(IsFilterPending));
                }
            }
            else if (args.PropertyName == nameof(SelectedStepListItem)
                     && SelectedStepListItem?.Step is not null)
            {
                SelectedStep = SelectedStepListItem.Step;
            }
        };
    }

    public ObservableCollection<HierarchyStepViewModel> Hierarchy { get; } = [];
    public ObservableCollection<HierarchyStepViewModel> StepRows { get; } = [];
    public ObservableCollection<StageItemViewModel> Stages { get; } = [];
    public ObservableCollection<StageItemViewModel> Subsections { get; } = [];
    public ObservableCollection<StageItemViewModel> NestedSubsections { get; } = [];
    public ObservableCollection<StepListItemViewModel> StepListItems { get; } = [];

    public IReadOnlyList<HierarchyStepViewModel> FullHierarchy => _fullHierarchy;

    public event EventHandler? RequestScrollToSelectedStep;
    public event EventHandler? RequestFocusStepSearch;

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NextFailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> PrevFailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> JumpToCurrentCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ClearSubsectionCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> FilterFailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ClearFailFilterCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleCompactCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> FocusStepSearchCommand { get; }
    public ReactiveCommand<string, System.Reactive.Unit> SetStepFilterCommand { get; }

    [Reactive] private StageItemViewModel? _selectedStage;
    [Reactive] private StageItemViewModel? _selectedSubsection;
    [Reactive] private StageItemViewModel? _selectedNestedSubsection;
    [Reactive] private HierarchyStepViewModel? _selectedStep;
    [Reactive] private StepListItemViewModel? _selectedStepListItem;
    [Reactive] private string _stepStatusFilter = StepFilter.All;
    [Reactive] private string _stepSearchText = string.Empty;
    [Reactive] private bool _hasSubsections;
    [Reactive] private bool _hasNestedSubsections;
    [Reactive] private bool _compactStepRows;
    [Reactive] private string _breadcrumbText = "Entire program";
    [Reactive] private string _breadcrumbDetailText = string.Empty;
    [Reactive] private int _suitePassedCount;
    [Reactive] private int _suiteFailedCount;
    [Reactive] private int _suitePendingCount;

    /// True when the step list is currently narrowed to failed steps only (set automatically after a suite fail).
    public bool IsFilteredToFail => string.Equals(_stepStatusFilter, StepFilter.Fail, StringComparison.Ordinal);
    public bool IsFilterAll => string.Equals(_stepStatusFilter, StepFilter.All, StringComparison.Ordinal);
    public bool IsFilterFail => string.Equals(_stepStatusFilter, StepFilter.Fail, StringComparison.Ordinal);
    public bool IsFilterRunning => string.Equals(_stepStatusFilter, StepFilter.Running, StringComparison.Ordinal);
    public bool IsFilterPending => string.Equals(_stepStatusFilter, StepFilter.Pending, StringComparison.Ordinal);

    public HierarchyStepViewModel? ActiveScopeStep
        => SelectedNestedSubsection?.Step ?? SelectedSubsection?.Step ?? SelectedStage?.Step;

    /// Mirrors the host plan tree into fresh view models, then restores stage / step selection by path.
    public void RebuildFromHost(string? preserveStagePath = null, string? preserveStepPath = null)
    {
        _fullHierarchy.Clear();
        Hierarchy.Clear();
        Stages.Clear();
        Subsections.Clear();
        NestedSubsections.Clear();
        StepRows.Clear();
        StepListItems.Clear();
        foreach (var node in _stepTreeSource())
        {
            _fullHierarchy.Add(new HierarchyStepViewModel(node));
        }

        foreach (var root in _fullHierarchy)
        {
            Hierarchy.Add(root);
        }

        Stages.Add(new StageItemViewModel(null, "Entire program"));
        foreach (var root in _fullHierarchy)
        {
            foreach (var stage in EnumerateStages(root))
            {
                Stages.Add(new StageItemViewModel(stage, stage.Name));
            }
        }

        RollupParentStatuses();
        RestoreSelection(preserveStagePath, preserveStepPath);
    }

    public void RestoreSelection(string? preserveStagePath, string? preserveStepPath)
    {
        _suppressStageFilter = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(preserveStagePath))
            {
                SelectedStage = Stages.FirstOrDefault(s =>
                    string.Equals(s.Path, preserveStagePath, StringComparison.OrdinalIgnoreCase))
                    ?? Stages.FirstOrDefault();
            }
            else if (SelectedStage is null || !Stages.Contains(SelectedStage))
            {
                SelectedStage = Stages.FirstOrDefault();
            }
        }
        finally
        {
            _suppressStageFilter = false;
        }

        ApplyStageFilter(preserveStepPath);
    }

    public void ClearNestedSubsection() => SelectedNestedSubsection = null;

    public bool IsWholePlanSelection(string path)
        => _fullHierarchy.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

    public HierarchyStepViewModel? FindByPath(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? null
            : StepHierarchy.Flatten(_fullHierarchy)
                .FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

    /// Selects the leaf a foreign page (Inspect) asked to reveal, widening filters if it is hidden.
    public void ApplySelectionFromInspect(string? stepPath)
    {
        if (string.IsNullOrWhiteSpace(stepPath))
        {
            return;
        }

        var leaf = StepHierarchy.Flatten(_fullHierarchy)
            .FirstOrDefault(s => s.Children.Count == 0
                && string.Equals(s.Path, stepPath, StringComparison.OrdinalIgnoreCase));
        if (leaf is null)
        {
            return;
        }

        SelectScopeForStep(leaf);
        if (!StepRows.Any(r => ReferenceEquals(r, leaf)))
        {
            StepStatusFilter = StepFilter.All;
        }

        SelectedStep = leaf;
        SyncSelectedStepListItem();
        RebuildVisibleStepList();
    }

    public void JumpToCurrent()
    {
        var currentPath = _getCurrentStepPath();
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return;
        }

        var match = StepHierarchy.Flatten(_fullHierarchy)
            .FirstOrDefault(s => string.Equals(s.Path, currentPath, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        SelectScopeForStep(match);
        if (!StepRows.Any(r => ReferenceEquals(r, match)))
        {
            StepStatusFilter = StepFilter.All;
        }

        SelectedStep = match;
        SyncSelectedStepListItem();
        RaiseRequestScroll();
    }

    public void RaiseRequestScroll() => RequestScrollToSelectedStep?.Invoke(this, EventArgs.Empty);

    /// Selects the first failed leaf in scope and reveals its detail — used after a failing verdict lands.
    public void MaybeAutoFocusFail()
    {
        var scope = ActiveScopeStep;
        IEnumerable<HierarchyStepViewModel> roots = scope is null ? _fullHierarchy : [scope];
        var firstFail = HierarchyRollup.EnumerateLeaves(roots)
            .FirstOrDefault(l => StatusChip.FromStatus(l.StatusText, l.Verdict) == "Fail");
        if (firstFail is null)
        {
            return;
        }

        SelectedStep = firstFail;
        SyncSelectedStepListItem();
        _openSelectedDetail();
    }

    private void CycleFail(bool forward)
    {
        var scope = ActiveScopeStep;
        IEnumerable<HierarchyStepViewModel> roots = scope is null ? _fullHierarchy : [scope];
        var fails = HierarchyRollup.EnumerateLeaves(roots)
            .Where(l => StatusChip.FromStatus(l.StatusText, l.Verdict) == "Fail")
            .ToList();
        if (fails.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedStep is null ? -1 : fails.FindIndex(f => ReferenceEquals(f, SelectedStep));
        int nextIndex;
        if (currentIndex < 0)
        {
            nextIndex = forward ? 0 : fails.Count - 1;
        }
        else
        {
            nextIndex = forward
                ? (currentIndex + 1) % fails.Count
                : (currentIndex - 1 + fails.Count) % fails.Count;
        }

        SelectedStep = fails[nextIndex];
        SyncSelectedStepListItem();
        _openSelectedDetail();
    }

    private void FilterFail()
    {
        var entire = Stages.FirstOrDefault(s => s.Step is null);
        if (entire is not null)
        {
            SelectedStage = entire;
        }

        StepStatusFilter = StepFilter.Fail;
    }

    private void SelectScopeForStep(HierarchyStepViewModel leaf)
    {
        var containingStage = Stages.FirstOrDefault(s => s.Step is not null && StepHierarchy.IsWithin(s.Step, leaf))
            ?? Stages.FirstOrDefault(s => s.Step is null);
        if (containingStage is not null && !ReferenceEquals(SelectedStage, containingStage))
        {
            SelectedStage = containingStage;
        }

        var containingSubsection = Subsections.FirstOrDefault(s =>
            s.Step is not null && StepHierarchy.IsWithin(s.Step, leaf));
        if (!ReferenceEquals(SelectedSubsection, containingSubsection))
        {
            SelectedSubsection = containingSubsection;
        }

        var containingNested = NestedSubsections.FirstOrDefault(s =>
            s.Step is not null && StepHierarchy.IsWithin(s.Step, leaf));
        if (!ReferenceEquals(SelectedNestedSubsection, containingNested))
        {
            SelectedNestedSubsection = containingNested;
        }
    }

    private static IEnumerable<HierarchyStepViewModel> EnumerateStages(HierarchyStepViewModel root)
    {
        if (!root.IsStage && root.Children.Count == 0)
        {
            yield break;
        }

        if (root.Children.Count > 0 && root.Children.Any(c => c.IsStage || c.Children.Count > 0))
        {
            foreach (var child in root.Children.Where(c => c.IsStage || c.Children.Count > 0))
            {
                yield return child;
            }

            yield break;
        }

        yield return root;
    }
}
