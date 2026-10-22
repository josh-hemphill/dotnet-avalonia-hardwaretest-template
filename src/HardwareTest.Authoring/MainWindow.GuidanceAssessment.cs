namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private long _guidanceAssessmentGeneration;
    private bool _guidanceAssessmentRunning;
    private bool _guidanceAssessmentPending;
    private GuidanceInputIdentity? _guidanceAssessmentIdentity;
    private string _guidanceEnvironmentIdentity = string.Empty;
    private Guid _guidanceEnvironmentSession;
    private sealed record GuidanceInputIdentity(Guid Session, AuthoringDocumentSnapshot? Snapshot, long? Revision,
        string? Plan, string? SavedPlan, string? SavedSidecar, string? Root, string Home, string Packages, string Environment);

    private GuidanceInputIdentity CurrentGuidanceIdentity()
    {
        var session = _viewModel.WorkspaceSessionId;
        if (_guidanceEnvironmentSession != session)
        {
            _guidanceEnvironmentSession = session;
            _guidanceEnvironmentIdentity = string.Empty;
        }
        // Read only published package values; pending notifications alone do not invalidate guidance.
        static string Values(IEnumerable<string> values) => string.Concat(values.Select(value => $"{value.Length}:{value}"));
        if (!_viewModel.EnvironmentInspectionPending)
            _guidanceEnvironmentIdentity = Values(_viewModel.EnvironmentPackages.SelectMany(package => new[]
                { package.Package, package.RequiredVersion, package.InstalledVersion, package.State, package.Satisfied.ToString() }));
        var document = _viewModel.SelectedDocument;
        return new(session, document?.Snapshot, document?.Revision, _viewModel.SelectedProgram?.PlanId,
            document?.SavedPlanIdentity, document?.SavedSidecarIdentity, _viewModel.Workspace?.Root,
            _viewModel.OpenTapHomeOverride, Values(_viewModel.Workspace?.Manifest.Dependencies.Select(package => package.Package) ?? []),
            _guidanceEnvironmentIdentity);
    }

    private Func<Action, Task> _runGuidanceAssessment = action => Task.Run(action);

    private sealed record GuidanceAssessmentRequest(AuthoringDocumentSnapshot Snapshot, string? SavedPlanIdentity,
        string? SavedSidecarIdentity, string Root, string[] PlanPaths, string[] Packages, string HomeOverride, bool Dirty);
    private sealed record GuidanceAssessmentResult(bool Complete, bool SourceMatches, bool SourceStillBaseline,
        string[] Blockers, string? Error = null);

    private void RequestGuidanceAssessment()
    {
        var identity = CurrentGuidanceIdentity();
        if (identity == _guidanceAssessmentIdentity) return;
        _guidanceAssessmentIdentity = identity;
        _guidanceAssessmentGeneration++;
        _guidanceAssessmentPending = true;
        _guidanceFeedback.Text = "Checking saved plan…";
        _guidanceNext.Text = "You can keep editing while readiness is checked.";
        _guidancePrimary.IsEnabled = false;
        _guidancePrimaryAction = null;
        _guidanceDetails.IsVisible = false;
        _guidanceWarning.IsVisible = false;
        if (!_guidanceAssessmentRunning) _ = RunGuidanceAssessmentAsync();
    }

    private async Task RunGuidanceAssessmentAsync()
    {
        _guidanceAssessmentRunning = true;
        try
        {
            while (_guidanceAssessmentPending && _guidanceVisible && GuidanceAllowed && GuidanceTargetCurrent())
            {
                _guidanceAssessmentPending = false;
                if (_viewModel.Workspace is not { } workspace || _viewModel.SelectedDocument is not { } document) return;
                var generation = _guidanceAssessmentGeneration;
                var identity = _guidanceAssessmentIdentity;
                var session = _viewModel.WorkspaceSessionId;
                var plan = _viewModel.SelectedProgram!.PlanId;
                var revision = document.Revision;
                // Snapshot is already isolated and immutable. No validation, cloning, or I/O runs on this thread.
                var request = new GuidanceAssessmentRequest(document.Snapshot, document.SavedPlanIdentity, document.SavedSidecarIdentity,
                    workspace.Root, workspace.TapPlanPaths.ToArray(), workspace.Manifest.Dependencies.Select(item => item.Package).ToArray(),
                    _viewModel.OpenTapHomeOverride, document.IsDirty);
                GuidanceAssessmentResult? result = null;
                try { await _runGuidanceAssessment(() => result = AssessGuidance(request)); }
                catch (Exception error) { result = new(false, false, false, [], AuthoringWorkspaceViewModel.PersistenceError(error)); }
                // Revisions also guard Undo/Redo. Saved baseline identities guard a save without a content edit.
                if (!_guidanceVisible || !GuidanceAllowed || !GuidanceTargetCurrent()) continue;
                if (identity != CurrentGuidanceIdentity())
                {
                    RequestGuidanceAssessment();
                    continue;
                }
                if (generation != _guidanceAssessmentGeneration || !_guidanceVisible || !GuidanceAllowed || !GuidanceTargetCurrent()
                    || session != _viewModel.WorkspaceSessionId || plan != _viewModel.SelectedProgram?.PlanId
                    || !ReferenceEquals(document, _viewModel.SelectedDocument) || revision != document.Revision
                    || !ReferenceEquals(request.Snapshot, document.Snapshot)
                    || request.SavedPlanIdentity != document.SavedPlanIdentity || request.SavedSidecarIdentity != document.SavedSidecarIdentity)
                    continue;
                ApplyGuidanceAssessment(result!);
            }
        }
        finally { _guidanceAssessmentRunning = false; }
    }

    private static GuidanceAssessmentResult AssessGuidance(GuidanceAssessmentRequest request)
    {
        try
        {
            var draft = request.Snapshot.Restore();
            // Only detached values reach the worker. Home resolution never observes a live manifest.
            var workspace = new AuthoringWorkspace(request.Root, new AuthoringManifest(), request.PlanPaths);
            var validHome = WorkspacePackPlan.TryResolveHomePath(workspace, request.HomeOverride, out var homePath, out var homeError);
            var blockers = AuthoringIssueService.GetIssues(draft, validHome ? new OpenTapHome(homePath!) : null, homeError, request.Packages)
                .Select(issue => issue.Message).ToArray();
            var store = new AuthoringDocumentStore(request.Root);
            var loaded = AuthoringMeasureSections.Normalize(store.Load(draft.PlanId));
            if (loaded.IsReadOnly || loaded.Document is not { } source)
                throw new AuthoringWorkspaceException(loaded.Error ?? "Saved authoring source is missing or uses an unsupported schema.");
            var saved = AuthoringDocumentSnapshot.Capture(source.ToDraft());
            var matches = request.Snapshot.ContentEquals(saved);
            var baseline = saved.PlanIdentity == request.SavedPlanIdentity && saved.SidecarIdentity == request.SavedSidecarIdentity;
            var path = request.PlanPaths.SingleOrDefault(file => Path.GetFileNameWithoutExtension(file) == draft.PlanId);
            var complete = false;
            if (!source.RequiresCompilation && path is not null)
            {
                var planHash = AuthoringDocumentStore.ComputeHash(store.ValidatePath(path));
                var sidecarHash = AuthoringDocumentStore.ComputeHash(store.ValidatePath(Path.ChangeExtension(path, ".program.json")));
                complete = matches && !request.Dirty && blockers.Length == 0 && planHash is not null && sidecarHash is not null
                    && source.CompiledPlanHash == planHash && source.CompiledSidecarHash == sidecarHash;
            }
            return new(complete, matches, baseline, blockers);
        }
        catch (Exception error)
        {
            return new(false, false, false, [], AuthoringWorkspaceViewModel.PersistenceError(error));
        }
    }

    private void ApplyGuidanceAssessment(GuidanceAssessmentResult result)
    {
        _guidancePrimary.IsEnabled = true;
        if (result.Error is { } error)
            PresentGuidance("Saved input is unavailable or unsafe", "Restore readable authoring source and compiled files inside this workspace, then resume guidance.",
                "Review recovery issues", GuidanceIssues, [error]);
        else if (result.Complete)
            PresentGuidance("First voltage test complete", "Preview the saved result with example data or an imported recording.",
                "Preview result", () => WorkspaceTabs.SelectedIndex = 5, []);
        else if (!result.SourceMatches && !result.SourceStillBaseline)
            PresentGuidance("Saved source differs from this draft", "Reopen the workspace to load the saved version, or reconcile current edits before Save/check.",
                "Review issues", GuidanceIssues, result.Blockers);
        else if (result.Blockers.Length > 0)
            PresentGuidance($"{result.Blockers.Length} item(s) need attention", "Resolve the listed issues before saving and checking this plan.",
                "Review issues", GuidanceIssues, result.Blockers);
        else if (!result.SourceMatches)
            PresentGuidance("Unsaved draft changes", "Save your current edits and compile the plan to check readiness.",
                "Save and check draft", GuidanceSave, []);
        else
            PresentGuidance("Draft needs a save and check", "Save the authoring source and compile the plan to check readiness.",
                "Save and check draft", GuidanceSave, []);
    }
}
