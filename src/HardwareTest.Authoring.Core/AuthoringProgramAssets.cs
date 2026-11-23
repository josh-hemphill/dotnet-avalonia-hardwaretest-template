namespace HardwareTest.Authoring;

internal static class AuthoringProgramAssets
{
    internal static void Validate(AuthoringManifest manifest)
    {
        if (manifest.ProgramAssets is null)
            throw new AuthoringWorkspaceException("programAssets must be an object keyed by program ID.");
        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (owner, files) in manifest.ProgramAssets)
        {
            if (string.IsNullOrWhiteSpace(owner) || files is null || !owners.Add(owner)
                || files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            {
                throw new AuthoringWorkspaceException($"Invalid programAssets declaration for '{owner}'.");
            }

            foreach (var path in files) ValidatePath(path);
        }
        if (manifest.ProgramAssets.Values.SelectMany(files => files).Distinct(StringComparer.Ordinal).Count()
            != manifest.ProgramAssets.Values.SelectMany(files => files).Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new AuthoringWorkspaceException("Program asset paths must have consistent casing.");
        }
    }

    internal static string[] ForPlan(AuthoringManifest manifest, string planId)
        => manifest.ProgramAssets.FirstOrDefault(pair => string.Equals(pair.Key, planId, StringComparison.OrdinalIgnoreCase)).Value ?? [];

    internal static IEnumerable<string> Included(AuthoringWorkspace workspace)
        => workspace.TapPlanPaths.Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Where(id => AuthoringBuildInclusion.Includes(workspace.Manifest, id))
            .SelectMany(id => ForPlan(workspace.Manifest, id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    internal static string ResolvePath(string root, string relative)
    {
        ValidatePath(relative);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        AuthoringBuildService.EnsureContained(root, AuthoringBuildService.ResolvedPath(path));
        return path;
    }

    private static void ValidatePath(string? path)
    {
        // Require a subdirectory to prevent collision with plans, sidecars and generated package.xml.
        if (string.IsNullOrWhiteSpace(path) || !path.Contains('/')
            || path.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.')
                || part.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))))
        {
            throw new AuthoringWorkspaceException($"Invalid portable asset path '{path}'. Use a path in a plans subdirectory.");
        }
    }
}
