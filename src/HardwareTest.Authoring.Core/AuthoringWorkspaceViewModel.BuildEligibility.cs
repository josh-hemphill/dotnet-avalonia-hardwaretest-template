namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    // Eligibility to try a build is distinct from a successful saved-build receipt.
    private bool HasKnownSavedBuildBlockers
    {
        get
        {
            if (_buildInspectionDispatch is not null) return BuildInspectionBusy || _buildInspectionBlockers;
            return EvaluateSavedBuildBlockers(Workspace, _sourceDocuments, _compiledConflicts, _uncompiledDocuments);
        }
    }

    private static bool EvaluateSavedBuildBlockers(AuthoringWorkspace? workspace, IReadOnlyDictionary<string, AuthoringDocumentDto> sources, IEnumerable<string> conflicts, IEnumerable<string> uncompiled)
    {
        if (workspace is null) return true;
        if (sources.Values.Any(source => AuthoringBuildInclusion.Includes(workspace.Manifest, source.PlanId)
            && !AuthoringSequence.HasMeasurement(source.ToDraft().Measure))) return true;
        if (conflicts.Any(id => AuthoringBuildInclusion.Includes(workspace.Manifest, id))) return true;
        foreach (var id in uncompiled.Where(id => AuthoringBuildInclusion.Includes(workspace.Manifest, id)))
        {
            if (!sources.TryGetValue(id, out var source) || workspace is null
                || !File.Exists(new AuthoringDocumentStore(workspace.Root).GetDocumentPath(id))) return true;
            try
            {
                var draft = AuthoringFormulaDeployment.Project(source.ToDraft());
                if (draft.AuthoringState.IncompleteNumericText.Count != 0) return true;
                AuthoringRecipeCatalog.EnsureScalarLimits(draft);
                PlanCompiler.ValidateFormulaInputs(draft);
                foreach (var metric in AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure))
                    if (metric.Source is ExpressionAlgorithm expression) _ = FormulaLowerer.Lower(expression, metric.Limits);
                var channels = AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure).Select(m => m.ChannelKey).ToArray();
                if (channels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != channels.Length) return true;
                var path = workspace.TapPlanPaths.FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), id, StringComparison.OrdinalIgnoreCase));
                if (path is not null && (source.CompiledPlanHash is null || source.CompiledSidecarHash is null)) return true;
            }
            catch (Exception ex) when (ex is AuthoringWorkspaceException or InvalidDataException) { return true; }
        }
        return false;
    }
}
