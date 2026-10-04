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

        var store = new AuthoringDocumentStore(Workspace.Root);
        if (store.Load(id).Exists) throw new AuthoringWorkspaceException($"Authoring source for '{id}' already exists; reopen or reconcile it before creating this program.");
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
        var sourceStore = ValidateProgramDeletion(planId, tapPlanPath);
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
        _recovery?.Cancel(Workspace.Root, planId);
        sourceStore.DeleteRecovery(planId);
        sourceStore.DeleteSource(planId);
        _sourceDocuments.Remove(planId); _recoverableDocuments.Remove(planId);
        _compiledConflicts.Remove(planId); _uncompiledDocuments.Remove(planId);
        _documents.Remove(planId);
        RaiseDraftState();
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
        InvalidateContractFindings();
        Status = $"Removed {planId}";
        Error = null;
        RefreshDatasets();
        RaiseSidecarProperties();
    }

    private AuthoringDocumentStore ValidateProgramDeletion(string planId, string tapPlanPath)
    {
        var store = new AuthoringDocumentStore(Workspace!.Root);
        foreach (var path in new[] { tapPlanPath, PlanCompiler.SidecarPath(tapPlanPath), store.GetDocumentPath(planId), store.GetRecoveryPath(planId) })
        {
            store.ValidatePath(path);
            if (Directory.Exists(path)) throw new IOException($"Refusing to delete a directory as a program file: {path}");
            if (File.Exists(path) && new FileInfo(path).IsReadOnly) throw new IOException($"Program file is read-only: {path}");
        }
        var source = store.Load(planId);
        if (source.IsReadOnly || source.Error is not null)
            throw new AuthoringWorkspaceException(source.Error ?? $"Authoring source '{planId}' uses a future schema; its bytes must be preserved.");
        var recovery = store.LoadAtPath(store.GetRecoveryPath(planId));
        if (recovery.IsReadOnly || recovery.Error is not null)
            throw new AuthoringWorkspaceException(recovery.Error ?? $"Recovery source '{planId}' uses a future schema; its bytes must be preserved.");
        return store;
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
