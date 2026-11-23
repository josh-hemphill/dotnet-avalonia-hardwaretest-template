namespace HardwareTest.Authoring;

internal static class ProcedureAssetFiles
{
    internal static ProgramDraft Load(ProgramDraft draft, AuthoringManifest manifest, string path)
    {
        if (draft.ImportedPlanXml is null) return draft;
        var root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relative in AuthoringProgramAssets.ForPlan(manifest, draft.PlanId))
        {
            var full = AuthoringProgramAssets.ResolvePath(root, relative);
            if (File.Exists(full)) files.Add(relative, File.ReadAllText(full));
        }
        return draft with { ImportedData = files };
    }

    internal static void CheckDeclaration(ProgramDraft draft, AuthoringManifest manifest)
    {
        var declared = AuthoringProgramAssets.ForPlan(manifest, draft.PlanId).ToHashSet(StringComparer.Ordinal);
        foreach (var path in draft.ImportedData.Keys)
            if (!declared.Contains(path)) throw new AuthoringWorkspaceException($"PROCEDURE_ASSET: Declare '{path}' for '{draft.PlanId}' in programAssets.");
    }

    internal static Dictionary<string, string?>? Hashes(IEnumerable<string> assets, string? plan)
    {
        if (plan is null) return null;
        var root = Path.GetDirectoryName(Path.GetFullPath(plan))!;
        var hashes = assets.ToDictionary(path => path,
            path => AuthoringDocumentStore.ComputeHash(AuthoringProgramAssets.ResolvePath(root, path)), StringComparer.Ordinal);
        return hashes.Count == 0 ? null : hashes;
    }

    internal static bool Changed(AuthoringDocumentDto document, string? plan)
    {
        if (document.CompiledAssetHashes is not { Count: > 0 } original) return false;
        var current = Hashes(original.Keys, plan);
        return current is null || original.Any(pair => current.GetValueOrDefault(pair.Key) != pair.Value);
    }
}
