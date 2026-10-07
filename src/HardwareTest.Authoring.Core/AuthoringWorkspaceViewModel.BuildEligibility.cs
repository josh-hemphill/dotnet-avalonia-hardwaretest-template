namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    // Eligibility to try a build is distinct from a successful saved-build receipt.
    private bool HasKnownSavedBuildBlockers
    {
        get
        {
            if (Workspace is null) return true;
            if (_compiledConflicts.Any(id => AuthoringBuildInclusion.Includes(Workspace.Manifest, id))) return true;
            foreach (var id in _uncompiledDocuments.Where(id => AuthoringBuildInclusion.Includes(Workspace.Manifest, id)))
            {
                if (!_sourceDocuments.TryGetValue(id, out var source) || Workspace is null
                    || !File.Exists(new AuthoringDocumentStore(Workspace.Root).GetDocumentPath(id))) return true;
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
                    var path = TryExistingTapPlanPath(id);
                    if (path is not null && (source.CompiledPlanHash is null || source.CompiledSidecarHash is null)) return true;
                }
                catch (Exception ex) when (ex is AuthoringWorkspaceException or InvalidDataException) { return true; }
            }
            return false;
        }
    }
}
