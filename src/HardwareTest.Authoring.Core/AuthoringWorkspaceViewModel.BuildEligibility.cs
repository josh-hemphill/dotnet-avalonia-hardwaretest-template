namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    // Eligibility to try a build is distinct from a successful saved-build receipt.
    private bool HasKnownSavedBuildBlockers
    {
        get
        {
            if (_compiledConflicts.Count != 0) return true;
            foreach (var id in _uncompiledDocuments)
            {
                if (!_sourceDocuments.TryGetValue(id, out var source) || Workspace is null
                    || !File.Exists(new AuthoringDocumentStore(Workspace.Root).GetDocumentPath(id))) return true;
                if (source.State.IncompleteNumericText.Count != 0 || source.State.FormulaIntent.Values.Contains(FormulaDeploymentIntent.Explore)) return true;
                try
                {
                    var draft = source.ToDraft();
                    AuthoringRecipeCatalog.EnsureScalarLimits(draft);
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
