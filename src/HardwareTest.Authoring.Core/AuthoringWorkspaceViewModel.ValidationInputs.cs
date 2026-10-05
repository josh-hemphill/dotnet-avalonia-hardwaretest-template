using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    internal Func<IReadOnlyList<string>, PlanContractOptions, PlanContractBatchReport> ValidateSavedPlans { get; set; }
        = (paths, options) => PlanContractValidator.Validate(paths, options);

    private FindingCheck PrepareFindingCheck() => VerifyFindingInputs(() =>
    {
        var saved = ReadSupportedSavedWorkspace();
        if (!AuthoringSourceExportGuard.WorkspaceCatalogMatches(saved.Manifest, Workspace!.Manifest))
            throw new AuthoringWorkspaceException("The saved workspace catalog changed; reopen or reconcile it before validating.");
        RefreshSourceReadiness();
        var sourceIssues = AuthoringSourceExportGuard.GetIssues(saved);
        if (HasUncompiledSources || _compiledConflicts.Count > 0)
        {
            var explanations = new[] { "Compile saved drafts and reconcile external edits before validating compiled plans." }
                .Concat(sourceIssues.Select(issue => issue.DisplayText))
                .Concat(CurrentSavedCompilationDiagnostics());
            throw new AuthoringWorkspaceException(string.Join(Environment.NewLine, explanations));
        }
        if (sourceIssues.Count > 0)
            throw new AuthoringWorkspaceException(string.Join(Environment.NewLine, sourceIssues.Select(issue => issue.DisplayText)));
        return CaptureFindingCheck();
    });

    private AuthoringWorkspace ReadSupportedSavedWorkspace()
    {
        var workspace = Workspace ?? throw new AuthoringWorkspaceException("Open a workspace before verifying saved inputs.");
        var saved = AuthoringWorkspaceLoader.Load(workspace.Root);
        if (saved.IsReadOnly)
            throw new AuthoringWorkspaceException("The saved workspace manifest uses an unsupported future-schema version; its bytes must be preserved.");
        var source = new AuthoringDocumentStore(saved.Root).LoadWorkspace();
        if (source.IsReadOnly || source.Error is not null)
            throw new AuthoringWorkspaceException(source.Error ?? "The workspace source uses an unsupported future schema; its bytes must be preserved.");
        return saved;
    }

    private bool HasCurrentFindingCheck => _lastFindingCheck is { } check && check.Session == _workspaceSession && !_lastFindingCheckStale;

    private void VerifySavedInputsForSave(IEnumerable<string> planIds) => VerifyFindingInputs(() =>
    {
        var saved = ReadSupportedSavedWorkspace();
        var store = new AuthoringDocumentStore(saved.Root);
        var targets = planIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var ids = HasCurrentFindingCheck ? targets.Concat(store.ListDocumentIds()) : targets;
        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var source = store.Load(id);
            if (source.IsReadOnly || source.Error is not null)
                throw new AuthoringWorkspaceException(source.Error ?? $"Source '{id}' uses an unsupported future schema; its bytes must be preserved.");
        }
        foreach (var id in targets)
        {
            var path = store.ValidatePath(TryExistingTapPlanPath(id) ?? ResolveTapPlanPath(id));
            _ = AuthoringDocumentStore.ComputeHash(path);
            _ = AuthoringDocumentStore.ComputeHash(store.ValidatePath(PlanCompiler.SidecarPath(path)));
        }
        if (HasCurrentFindingCheck && _lastFindingCheck!.Identity != FindingIdentity()) InvalidateContractFindings();
        return true;
    });

    private void SetFindingValidationStatus(PlanContractBatchReport report)
    {
        Status = _lastFindingCheckStale ? "Validation completed for an earlier revision; validate again."
            : report.HasErrors ? $"{report.ErrorCount} contract error(s)" : $"{report.WarningCount} contract warning(s)";
        Error = !_lastFindingCheckStale && report.HasErrors ? Status : null;
    }

    private T VerifyFindingInputs<T>(Func<T> verify)
    {
        try { return verify(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or AuthoringWorkspaceException)
        {
            InvalidateContractFindings();
            var message = error is AuthoringWorkspaceException
                ? $"Unable to verify saved validation inputs: {error.Message}"
                : "The checked source or artifact could not be read to verify validation. Validate again.";
            ReportError(message);
            throw new AuthoringWorkspaceException(message, error);
        }
    }
}
