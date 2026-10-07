namespace HardwareTest.Authoring;

/// Loads saved authoring sources before importing compiled-only programs.
public static class AuthoringSourceWorkspaceLoader
{
    public static DraftWorkspace Load(string root, IPlanCompiler? compiler = null)
    {
        compiler ??= new PlanCompiler();
        var files = AuthoringWorkspaceLoader.Load(root);
        var store = new AuthoringDocumentStore(files.Root);
        // Filenames establish source ownership and inclusion before any source is read.
        var sourceIds = store.ListDocumentIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                throw new AuthoringWorkspaceException($"Workspace catalog conflict: authoring.json and {store.GetWorkspacePath()} differ. A partial Save All or external edit requires recovery. Review both files and any available .bak files, restore the intended catalog consistently, then reopen; neither file was overwritten.");
            loaded = loaded with { Files = loaded.Files with { Manifest = workspaceDocument.Manifest } };
        }
        foreach (var id in store.ListDocumentIds())
        {
            var included = AuthoringBuildInclusion.Includes(loaded.Files.Manifest, id);
            var result = store.Load(id);
            // Supported excluded sources remain authoritative for editing. Unsupported
            // excluded sources are unavailable individually, never a compiled fallback.
            if (result.Error is { } error)
            {
                if (included) throw new AuthoringWorkspaceException(error);
                continue;
            }
            if (result.IsReadOnly) { if (included) readOnly = true; continue; }
            if (result.Document is { } document) programs[id] = document.ToDraft();
        }
        return loaded with { Files = loaded.Files with { IsReadOnly = readOnly }, Programs = programs.Values.OrderBy(program => program.PlanId, StringComparer.OrdinalIgnoreCase).ToArray() };
    }

}
