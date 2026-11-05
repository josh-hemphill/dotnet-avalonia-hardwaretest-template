using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public partial class StepTreeViewModel
{
    private HierarchyStepViewModel? _referenceAcceptanceStage;
    private HierarchyStepViewModel? _referenceAcceptanceParent;

    public bool IsReadOnlyEvidenceSelection => SelectedStep?.IsReadOnlyEvidence == true;

    /// Acceptance is evaluated by the operator host after instrument execution. Mirror its
    /// saved per-rule results as read-only leaves so all counts and failure navigation agree.
    private void ReplaceReferenceAcceptanceResults(IReadOnlyList<StepResultRecord> results)
    {
        var previousId = IsReadOnlyEvidenceSelection ? SelectedStep!.Id : null;
        var clearScope = ActiveScopeStep?.IsReadOnlyEvidence == true;
        if (_referenceAcceptanceStage is { } previous)
        {
            if (_referenceAcceptanceParent is { } parent) parent.Children.Remove(previous);
            else { _fullHierarchy.Remove(previous); Hierarchy.Remove(previous); }
            foreach (var stage in Stages.Where(stage => stage.Step?.IsReadOnlyEvidence == true).ToArray()) Stages.Remove(stage);
        }
        _referenceAcceptanceStage = null;
        _referenceAcceptanceParent = null;
        if (previousId is not null) SelectedStep = null;
        if (clearScope) ClearScope();
        if (results.Count > 0)
        {
            var node = new OpenTapStepNode
            {
                Id = "reference:acceptance-stage",
                Name = "Reference acceptance",
                Path = "Reference acceptance",
                IsStage = true,
                KeyValue = "Read-only acceptance evidence. Rerun the measured test or the full program to evaluate again.",
                Children = results.Select(result => new OpenTapStepNode
                {
                    Id = result.StepId,
                    Path = result.StepPath,
                    Name = result.StepPath["Reference acceptance/".Length..],
                    StatusText = result.Passed ? "Pass" : "Fail",
                    Verdict = result.Passed ? "Pass" : "Fail",
                    KeyValue = result.Message,
                }).ToList(),
            };
            var evidence = new HierarchyStepViewModel(node);
            _referenceAcceptanceStage = evidence;
            // Keep a single authored program root's verdict consistent with final acceptance.
            // The added nodes belong only to the UI mirror, never to the executable host plan.
            if (_fullHierarchy.Count == 1 && _fullHierarchy[0].Children.Count > 0)
            {
                _referenceAcceptanceParent = _fullHierarchy[0];
                _referenceAcceptanceParent.Children.Add(evidence);
            }
            else { _fullHierarchy.Add(evidence); Hierarchy.Add(evidence); }
            Stages.Add(new StageItemViewModel(evidence, evidence.Name));
            if (previousId is not null)
                SelectedStep = evidence.Children.FirstOrDefault(child => child.Id == previousId);
        }
        this.RaisePropertyChanged(nameof(HasHierarchyOverview));
        this.RaisePropertyChanged(nameof(IsReadOnlyEvidenceSelection));
    }
}
