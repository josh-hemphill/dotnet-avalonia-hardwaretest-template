using System.Text.Json;

namespace HardwareTest.Authoring;

/// Compiled checks and exports must not silently use artifacts older than saved authoring sources.
public static class AuthoringSourceExportGuard
{
    public static IReadOnlyList<PackPreflightFinding> GetIssues(AuthoringWorkspace workspace,
        IReadOnlySet<string>? includedProgramIds = null)
    {
        var store = new AuthoringDocumentStore(workspace.Root);
        var issues = new List<PackPreflightFinding>();
        var workspaceSource = store.LoadWorkspace();
        if (workspaceSource.IsReadOnly || workspaceSource.Error is not null)
            issues.Add(new("WORKSPACE_SOURCE_READ_ONLY", "Workspace source requires a supported schema or explicit repair before export.", true, store.GetWorkspacePath()));
        else if (workspaceSource.Document is { } source &&
            !WorkspaceCatalogMatches(source.Manifest, workspace.Manifest))
            issues.Add(new("WORKSPACE_SOURCE_CONFLICT", "Reconcile authoring.json and workspace source before checking or exporting the catalog.", true, store.GetWorkspacePath()));
        foreach (var id in store.ListDocumentIds())
        {
            var path = workspace.TapPlanPaths.FirstOrDefault(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p), id, StringComparison.OrdinalIgnoreCase));
            if (includedProgramIds is not null && !includedProgramIds.Contains(id)) continue;
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

    internal static bool WorkspaceCatalogMatches(AuthoringManifest left, AuthoringManifest right)
    {
        return JsonSerializer.Serialize(left, AuthoringJsonContext.Default.AuthoringManifest)
            == JsonSerializer.Serialize(right, AuthoringJsonContext.Default.AuthoringManifest);
    }

    public static void EnsureCurrent(AuthoringWorkspace workspace)
    {
        var issues = GetIssues(workspace);
        if (issues.Count > 0) throw new AuthoringWorkspaceException(string.Join(Environment.NewLine, issues.Select(i => i.DisplayText)));
    }
}
