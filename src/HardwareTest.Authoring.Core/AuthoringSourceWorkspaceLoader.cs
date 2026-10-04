namespace HardwareTest.Authoring;

/// Loads saved authoring sources before importing compiled-only programs.
public static class AuthoringSourceWorkspaceLoader
{
    public static DraftWorkspace Load(string root, IPlanCompiler? compiler = null)
    {
        compiler ??= new PlanCompiler();
        var files = AuthoringWorkspaceLoader.Load(root);
        var store = new AuthoringDocumentStore(files.Root);
        var sourceIds = store.ListDocumentIds().Where(id =>
        {
            var source = store.Load(id);
            return source.Document is not null || source.Error is not null;
        }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imports = files with
        {
            TapPlanPaths = files.TapPlanPaths.Where(path =>
            !sourceIds.Contains(Path.GetFileNameWithoutExtension(path))).ToArray()
        };
        var loaded = compiler.LoadAll(imports);
        return OverlaySources(loaded with { Files = files with { IsReadOnly = files.IsReadOnly || loaded.Files.IsReadOnly } });
    }

    private static DraftWorkspace OverlaySources(DraftWorkspace loaded)
    {
        var store = new AuthoringDocumentStore(loaded.Files.Root);
        var programs = loaded.Programs.ToDictionary(p => p.PlanId, StringComparer.OrdinalIgnoreCase);
        var workspaceSource = store.LoadWorkspace();
        if (workspaceSource.Error is { } workspaceError) throw new AuthoringWorkspaceException(workspaceError);
        var readOnly = loaded.Files.IsReadOnly || workspaceSource.IsReadOnly;
        if (workspaceSource.Document is { } workspaceDocument)
        {
            if (!readOnly && !AuthoringSourceExportGuard.WorkspaceCatalogMatches(loaded.Files.Manifest, workspaceDocument.Manifest))
                throw new AuthoringWorkspaceException($"Workspace catalog conflict: authoring.json and {store.GetWorkspacePath()} differ. A partial Save All or external edit requires recovery. Review both files and their .bak/schema backup files, restore the intended catalog consistently, then reopen; neither file was overwritten.");
            loaded = loaded with { Files = loaded.Files with { Manifest = workspaceDocument.Manifest } };
        }
        foreach (var id in store.ListDocumentIds())
        {
            var result = store.Load(id);
            if (result.Error is { } error) throw new AuthoringWorkspaceException(error);
            if (result.IsReadOnly) { readOnly = true; continue; }
            if (result.Document is { } document) programs[id] = document.ToDraft();
        }
        return loaded with { Files = loaded.Files with { IsReadOnly = readOnly }, Programs = programs.Values.OrderBy(program => program.PlanId, StringComparer.OrdinalIgnoreCase).ToArray() };
    }

}
