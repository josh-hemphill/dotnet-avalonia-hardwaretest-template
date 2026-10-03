namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public void CreateProgram(string? planId = null)
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace before creating a program.");
        }

        EnsureWritableWorkspace("create a program");
        var id = string.IsNullOrWhiteSpace(planId) ? NextProgramId() : planId.Trim();
        if (Programs.Any(p => string.Equals(p.PlanId, id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AuthoringWorkspaceException($"Program '{id}' already exists in this session.");
        }

        var created = WithCatalogSlots(AuthoringRecipeCatalog.CreateProgram(id), Workspace.Manifest);
        RememberNodeSelection();
        _workspaceHistory.Clear();
        _documents.Add(id, new AuthoringDocumentSession(created, isSaved: false));
        Programs = [.. Programs, created];
        _selectedInstrumentSlot = null;
        AssignSelectedProgram(created);
        InvalidateContractFindings();
        RecomputeDocumentDirty();
        Status = created.Measure.Count == 0
            ? AuthoringChrome.EmptyMeasureHint
            : $"Created {id}";
        Error = null;
        RefreshDatasets();
        RaiseSidecarProperties();
    }

    public void RemoveSelectedProgram()
    {
        if (Workspace is null || SelectedProgram is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace and select a program before removing it.");
        }

        if (Workspace.IsReadOnly)
        {
            throw new AuthoringWorkspaceException("Workspace is read-only; cannot remove a program.");
        }

        var planId = SelectedProgram.PlanId;
        var existingTapPlan = TryExistingTapPlanPath(planId);
        var tapPlanPath = string.IsNullOrWhiteSpace(existingTapPlan)
            ? ResolveTapPlanPath(planId)
            : existingTapPlan;
        TryDeleteFile(tapPlanPath);
        TryDeleteFile(PlanCompiler.SidecarPath(tapPlanPath));
        if (!string.IsNullOrWhiteSpace(existingTapPlan))
        {
            Workspace = Workspace with
            {
                TapPlanPaths = [.. Workspace.TapPlanPaths.Where(path =>
                    !string.Equals(path, existingTapPlan, StringComparison.OrdinalIgnoreCase))],
            };
        }

        var removedIndex = 0;
        for (var i = 0; i < Programs.Count; i++)
        {
            if (string.Equals(Programs[i].PlanId, planId, StringComparison.OrdinalIgnoreCase))
            {
                removedIndex = i;
                break;
            }
        }

        var remaining = Programs
            .Where(program => !string.Equals(program.PlanId, planId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _documents.Remove(planId);
        _workspaceHistory.Clear();
        Programs = remaining;
        _dirtyPlans.Remove(planId);
        _dirtySidecars.Remove(planId);
        RecomputeDocumentDirty();
        _selectedInstrumentSlot = null;
        AssignSelectedProgram(
            remaining.Length == 0
                ? null
                : remaining[Math.Min(removedIndex, remaining.Length - 1)]);
        Findings = [];
        FindingRows = [];
        Status = $"Removed {planId}";
        Error = null;
        RefreshDatasets();
        RaiseSidecarProperties();
    }

    public (string PlanId, string TapPlanPath, string SidecarPath) DescribeSelectedProgramRemoval()
    {
        if (!CanRemoveSelectedProgram || SelectedProgram is null)
        {
            throw new AuthoringWorkspaceException("Open a writable workspace and select a program before removing it.");
        }

        var planId = SelectedProgram.PlanId;
        var tapPlanPath = TryExistingTapPlanPath(planId) ?? ResolveTapPlanPath(planId);
        return (planId, tapPlanPath, PlanCompiler.SidecarPath(tapPlanPath));
    }

    public bool RemoveSelectedProgramIfMatches((string PlanId, string TapPlanPath, string SidecarPath) confirmedTarget)
    {
        if (!CanRemoveSelectedProgram)
        {
            return false;
        }

        var current = DescribeSelectedProgramRemoval();
        if (!string.Equals(current.PlanId, confirmedTarget.PlanId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(current.TapPlanPath, confirmedTarget.TapPlanPath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(current.SidecarPath, confirmedTarget.SidecarPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        RemoveSelectedProgram();
        return true;
    }

}
