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
        var sources = new AuthoringDocumentStore(Workspace.Root).ListDocumentIds()
            .Select(id => new AuthoringDocumentStore(Workspace.Root).GetDocumentPath(id)).OrderBy(path => path)
            .Select(path => $"{path}:{AuthoringBuildService.Hash(File.ReadAllBytes(path))}");
        return AuthoringBuildService.Hash(System.Text.Encoding.UTF8.GetBytes(content + revisions + string.Join(";", files.Concat(sources))));
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
        var stale = check.Generation != _findingRevision || check.Identity != FindingIdentity();
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
            return new AuthoringFindingRow(program?.PlanId ?? Path.GetFileNameWithoutExtension(plan.TargetPath), plan.TargetPath, finding, program is not null)
            {
                CheckedRevision = program?.Revision,
                IsStale = stale,
                NodeId = node,
                SessionId = check.Session,
                CheckedIdentity = check.Identity,
                NavigationReason = node is not null ? "Explicit target resolved through the checked compiler source map."
                    : "No verified source field is available; opens program settings."
            };
        })).ToArray();
        OnPropertyChanged(nameof(IssuesSummary));
    }

    public PlanContractTarget? NavigateFinding(AuthoringFindingRow row)
    {
        if (Workspace is null || row.IsStale || row.SessionId != _workspaceSession || row.CheckedIdentity != FindingIdentity()
            || !FindingRows.Contains(row) || !Programs.Any(program => program.PlanId == row.ProgramId))
        {
            if (Workspace is not null && row.SessionId == _workspaceSession) InvalidateContractFindings();
            ReportError("This finding is stale or its program was removed. Validate again before navigating.");
            return null;
        }
        SelectProgram(row.ProgramId);
        if (row.NodeId is { } id)
        {
            var matches = _sequenceItems.Select((item, index) => (item, index)).Where(pair => pair.item.NodeId == id).ToArray();
            if (matches.Length == 1)
            {
                SelectSequence(matches[0].index);
                var target = row.Finding.Target!;
                if (target.Field is null || target.Field == "Threshold" && ShowThreshold
                    || target.Field == "LimitLow" && ShowBandLimits || target.Field == "ChannelKey" && HasMetricPresentation)
                    return target with { ProgramId = row.ProgramId, NodeId = id };
                Status = "The structured field has no supported editor control; opens program settings.";
                return new(ProgramId: row.ProgramId, Section: "ProgramSettings");
            }
        }
        Status = row.NavigationReason;
        return new(ProgramId: row.ProgramId, Section: "ProgramSettings");
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
