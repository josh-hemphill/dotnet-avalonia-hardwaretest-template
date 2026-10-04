namespace HardwareTest.Authoring;

/// Compiled checks and exports must not silently use artifacts older than saved authoring sources.
public static class AuthoringSourceExportGuard
{
    public static IReadOnlyList<PackPreflightFinding> GetIssues(AuthoringWorkspace workspace,
        IReadOnlySet<string>? includedProgramIds = null)
    {
        var store = new AuthoringDocumentStore(workspace.Root);
        var issues = new List<PackPreflightFinding>();
        foreach (var id in store.ListDocumentIds())
        {
            var path = workspace.TapPlanPaths.FirstOrDefault(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p), id, StringComparison.OrdinalIgnoreCase));
            if (path is not null && includedProgramIds is not null && !includedProgramIds.Contains(id)) continue;
            var result = store.Load(id);
            if (result.IsReadOnly || result.Error is not null || result.Document is null)
            {
                issues.Add(new("SOURCE_READ_ONLY", $"Source '{id}' requires a supported schema or explicit repair before export.", true, store.GetDocumentPath(id)));
                continue;
            }
            var document = result.Document;
            if (document.RequiresCompilation || path is null)
                issues.Add(new("SOURCE_COMPILE_REQUIRED", $"Compile saved source '{id}' before checking or exporting its artifacts.", true, store.GetDocumentPath(id)));
            else if (document.CompiledPlanHash != AuthoringDocumentStore.ComputeHash(path)
                || document.CompiledSidecarHash != AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)))
                issues.Add(new("SOURCE_EXTERNAL_CONFLICT", $"Reconcile external compiled changes for '{id}' before checking or exporting.", true, path));
        }
        return issues;
    }

    public static void EnsureCurrent(AuthoringWorkspace workspace)
    {
        var issues = GetIssues(workspace);
        if (issues.Count > 0) throw new AuthoringWorkspaceException(string.Join(Environment.NewLine, issues.Select(i => i.DisplayText)));
    }
}
