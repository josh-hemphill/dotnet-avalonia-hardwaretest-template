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

        CreateProgram(new PlanInitializationRequest(id));
    }

    /// Legacy convenience remains unsaved, with explicit choices using the same constructor.
    public void CreateProgram(PlanInitializationRequest request)
    {
        EnsureWritableWorkspace("create a program");
        var result = new AuthoringPlanInitializer().Construct(InitializationRequest(request));
        OpenInitializedProgram(result, isSaved: false);
    }

    public bool CanInitializePlan => Workspace is { IsReadOnly: false } && !OperationBusy;
    public string SuggestedPlanId => NextProgramId();

    public PlanInitializationResult ReviewPlanInitialization(PlanInitializationRequest request)
    {
        EnsureWritableWorkspace("review a new test plan");
        return new AuthoringPlanInitializer().Construct(InitializationRequest(request));
    }

    public PlanInitializationResult InitializePlan(PlanInitializationRequest request, CancellationToken cancellationToken = default)
    {
        EnsureWritableWorkspace("create a test plan");
        if (OperationBusy) throw new AuthoringWorkspaceException("Wait for the active operation before creating a test plan.");
        var result = new AuthoringPlanInitializer().Create(InitializationRequest(request), cancellationToken);
        OpenInitializedProgram(result, isSaved: true);
        return result;
    }

    private PlanInitializationRequest InitializationRequest(PlanInitializationRequest request)
    {
        if (Workspace is null) throw new AuthoringWorkspaceException("Open a workspace before creating a test plan.");
        if (request.WorkspaceRoot is not null && Path.GetFullPath(request.WorkspaceRoot) != Path.GetFullPath(Workspace.Root))
            throw new AuthoringWorkspaceException("The workspace changed; reopen New test plan.");
        return request with
        {
            WorkspaceRoot = Workspace.Root,
            ExistingPlanIds = Programs.Select(program => program.PlanId).ToArray(),
            Home = HardwareInspectionHome
        };
    }

    private void OpenInitializedProgram(PlanInitializationResult result, bool isSaved)
    {
        var created = result.Draft;
        RememberNodeSelection();
        _workspaceHistory.Clear();
        var session = new AuthoringDocumentSession(created, isSaved: isSaved);
        _documents.Add(created.PlanId, session);
        created = session.Draft;
        Programs = [.. Programs, created];
        if (isSaved)
        {
            var source = AuthoringDocumentDto.FromDraft(created);
            source.RequiresCompilation = true;
            _sourceDocuments.Add(created.PlanId, source);
            _uncompiledDocuments.Add(created.PlanId);
            RaiseDraftState();
        }
        _selectedInstrumentSlot = null;
        AssignSelectedProgram(created);
        var preferred = SequenceItems.ToList().FindIndex(row => row.IsSelectable && row.Section == SequenceSection.Measure);
        if (preferred < 0) preferred = SequenceItems.ToList().FindIndex(row => row.IsSelectable);
        if (preferred >= 0) SelectSequence(preferred);
        RememberNodeSelection();
        InvalidateContractFindings();
        RecomputeDocumentDirty();
        Status = result.NextAction;
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
        _saveCompilationWarnings.Remove(planId); UpdateSavePreviewWarning();
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
        Error = SavePreviewWarning;
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
