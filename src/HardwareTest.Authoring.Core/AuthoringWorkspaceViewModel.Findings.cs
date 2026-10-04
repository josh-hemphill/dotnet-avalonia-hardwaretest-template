using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private long _findingRevision;
    private FindingCheck? _lastFindingCheck;
    private bool _lastFindingCheckStale;
    public string IssuesCheckState => _lastFindingCheck is null ? "Saved plans have not been checked in this workspace."
        : $"Checked revisions: {string.Join(", ", _lastFindingCheck.Programs.Values.Select(program => $"{program.PlanId} r{program.Revision}"))} · {(_lastFindingCheckStale ? "Stale — validate again" : "Current")}";
    private readonly Dictionary<string, string?> _importedPlanHashes = new(StringComparer.OrdinalIgnoreCase);
    private sealed record FindingCheck(Guid Session, long Generation, string Identity,
        IReadOnlyDictionary<string, CheckedProgram> Programs);
    private sealed record CheckedProgram(string PlanId, long Revision, AuthoringCompileResult Compilation);

    public string IssuesSummary => $"{EditingIssues.Count} editing issues · {FindingRows.Count(row => row.Finding.Severity == PlanContractSeverity.Error)} checked errors · "
        + $"{FindingRows.Count(row => row.Finding.Severity == PlanContractSeverity.Warning)} checked warnings · "
        + $"{FindingRows.Count(row => row.IsStale)} stale checked findings";

    private string FindingIdentity()
    {
        var content = ContentFingerprint();
        var revisions = string.Join(";", _documents.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value.Revision}"));
        var files = Workspace!.TapPlanPaths.OrderBy(path => path).SelectMany(path => new[] { path, PlanCompiler.SidecarPath(path) })
            .Select(path => $"{path}:{(File.Exists(path) ? AuthoringBuildService.Hash(File.ReadAllBytes(path)) : "missing")}");
        var store = new AuthoringDocumentStore(Workspace.Root);
        var sources = store.ListDocumentIds()
            .Select(store.GetDocumentPath).OrderBy(path => path)
            .Select(path => $"{path}:{AuthoringBuildService.Hash(File.ReadAllBytes(path))}");
        var catalogs = new[] { Path.Combine(Workspace.Root, AuthoringWorkspaceLoader.ManifestFileName), store.GetWorkspacePath() }
            .Select(path => $"{path}:{(File.Exists(path) ? AuthoringBuildService.Hash(File.ReadAllBytes(path)) : "missing")}");
        return AuthoringBuildService.Hash(System.Text.Encoding.UTF8.GetBytes(content + revisions + string.Join(";", files.Concat(sources).Concat(catalogs))));
    }

    private FindingCheck CaptureFindingCheck()
    {
        var identity = FindingIdentity();
        var programs = new Dictionary<string, CheckedProgram>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in Programs)
        {
            var path = Workspace!.TapPlanPaths.SingleOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), draft.PlanId, StringComparison.OrdinalIgnoreCase));
            if (path is null || !File.Exists(path) || !_documents.TryGetValue(draft.PlanId, out var session)) continue;
            var bytes = File.ReadAllBytes(path);
            var hash = AuthoringBuildService.Hash(bytes);
            using var stream = new MemoryStream(bytes, writable: false);
            // Source-backed programs must have a matching compiled artifact. Imported programs retain compiler-loaded IDs.
            var verified = _sourceDocuments.TryGetValue(draft.PlanId, out var source)
                ? !source.RequiresCompilation && source.CompiledPlanHash == hash
                : _importedPlanHashes.TryGetValue(draft.PlanId, out var importedHash) && importedHash == hash;
            programs[path] = new(draft.PlanId, session.Revision, new(draft.PlanId, hash,
                verified ? AuthoringBuildService.CompileMap(AuthoringFormulaDeployment.Project(draft), stream) : [],
                AuthoringFormulaDeployment.ExcludedNodes(draft)));
        }
        return new(_workspaceSession, _findingRevision, identity, programs);
    }

    private void AcceptFindings(PlanContractBatchReport report, FindingCheck check)
    {
        if (check.Session != _workspaceSession) return;
        var stale = VerifyFindingInputs(() => check.Generation != _findingRevision || check.Identity != FindingIdentity());
        _lastFindingCheck = check;
        _lastFindingCheckStale = stale;
        OnPropertyChanged(nameof(IssuesCheckState));
        Findings = stale ? [] : report.Plans.SelectMany(plan => plan.Findings).ToArray();
        FindingRows = report.Plans.SelectMany(plan => plan.Findings.Select(finding =>
        {
            check.Programs.TryGetValue(plan.TargetPath, out var program);
            var target = finding.Target;
            Guid? node = null;
            if (program is not null && (target?.ProgramId is null || target.ProgramId == program.PlanId))
            {
                var maps = program.Compilation.SourceMap.Where(map => target?.CompiledStepId is { } step
                    ? map.StepId == step : target?.NodeId is { } id && map.NodeId == id).ToArray();
                if (maps.Length == 1 && maps[0].NodeId is { } mapped && (target?.NodeId is null || target.NodeId == mapped)) node = mapped;
            }
            var draft = Programs.SingleOrDefault(draft => draft.PlanId == program?.PlanId);
            var destination = draft is null ? null : AuthoringFindingNavigation.Resolve(draft, node, target);
            return new AuthoringFindingRow(program?.PlanId ?? Path.GetFileNameWithoutExtension(plan.TargetPath), plan.TargetPath, finding, program is not null)
            {
                CheckedRevision = program?.Revision,
                IsStale = stale,
                NodeId = node,
                SessionId = check.Session,
                CheckedIdentity = check.Identity,
                NavigationLabel = destination?.Label ?? "Open program settings",
                NavigationReason = destination?.Reason ?? "No verified source field is available; opens program settings."
            };
        })).ToArray();
        OnPropertyChanged(nameof(IssuesSummary));
    }

    public PlanContractTarget? NavigateFinding(AuthoringFindingRow row)
    {
        string? identity = null;
        if (Workspace is not null && !row.IsStale && row.SessionId == _workspaceSession)
        {
            try { identity = VerifyFindingInputs(FindingIdentity); }
            catch (AuthoringWorkspaceException)
            {
                return null;
            }
        }
        if (Workspace is null || row.IsStale || row.SessionId != _workspaceSession || row.CheckedIdentity != identity
            || !FindingRows.Contains(row) || !Programs.Any(program => program.PlanId == row.ProgramId))
        {
            if (Workspace is not null && row.SessionId == _workspaceSession) InvalidateContractFindings();
            ReportError("This finding is stale or its program was removed. Validate again before navigating.");
            return null;
        }
        var draft = Programs.Single(program => program.PlanId == row.ProgramId);
        var destination = AuthoringFindingNavigation.Resolve(draft, row.NodeId, row.Finding.Target);
        SelectProgram(row.ProgramId);
        if (destination.Target.NodeId is { } id)
        {
            var index = _sequenceItems.ToList().FindIndex(item => item.NodeId == id);
            SelectSequence(index);
        }
        Status = destination.Reason;
        return destination.Target;
    }

    private void InvalidateFindingsAfterSave()
    {
        if (_lastFindingCheck is not { } check || check.Session != _workspaceSession || _lastFindingCheckStale) return;
        try
        {
            if (check.Identity == VerifyFindingInputs(FindingIdentity)) return;
        }
        catch (AuthoringWorkspaceException)
        {
            // A partially published or unreadable artifact cannot retain a current checked state.
            return;
        }
        InvalidateContractFindings();
    }

    public PlanContractTarget? NavigateEditingIssue(AuthoringEditingIssue issue)
    {
        if (!EditingIssues.Contains(issue) || !Programs.Any(program => program.PlanId == issue.PlanId)) return null;
        SelectProgram(issue.PlanId);
        var matches = _sequenceItems.Select((item, index) => (item, index)).Where(pair => pair.item.NodeId == issue.NodeId).ToArray();
        if (matches.Length != 1) return new(ProgramId: issue.PlanId, Section: "ProgramSettings");
        SelectSequence(matches[0].index);
        return new(ProgramId: issue.PlanId, NodeId: issue.NodeId, Section: issue.Section, Field: issue.Field);
    }
}
