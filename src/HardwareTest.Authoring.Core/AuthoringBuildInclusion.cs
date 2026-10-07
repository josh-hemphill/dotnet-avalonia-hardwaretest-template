namespace HardwareTest.Authoring;

/// Package selection is determined from IDs before any source content is read.
public static class AuthoringBuildInclusion
{
    public static bool AllowsProgram(AuthoringManifest manifest, string planId)
        => !string.Equals(manifest.Package.Name, PackageXmlRenderer.TemplatePackageName, StringComparison.Ordinal)
            || PackageXmlRenderer.TemplatePackFiles.Contains(planId + ".TapPlan", StringComparer.OrdinalIgnoreCase);

    public static bool Includes(AuthoringManifest manifest, string planId)
        => !manifest.ExcludedProgramIds.Contains(planId, StringComparer.OrdinalIgnoreCase) && AllowsProgram(manifest, planId);

    public static IReadOnlyList<string> ProgramIds(AuthoringWorkspace workspace)
        => workspace.TapPlanPaths.Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Concat(new AuthoringDocumentStore(workspace.Root).ListDocumentIds())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
}
