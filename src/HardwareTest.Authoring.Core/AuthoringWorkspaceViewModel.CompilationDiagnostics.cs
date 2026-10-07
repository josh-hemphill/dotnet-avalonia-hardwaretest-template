namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private readonly Dictionary<string, SavedCompilationDiagnostic> _savedCompilationDiagnostics = new(StringComparer.OrdinalIgnoreCase);
    private sealed record SavedCompilationDiagnostic(string SourceHash, string Message);

    private void RecordSavedCompilationDiagnostic(string planId, string? diagnostic, AuthoringDocumentStore store)
    {
        _savedCompilationDiagnostics.Remove(planId);
        if (diagnostic is not null && AuthoringDocumentStore.ComputeHash(store.GetDocumentPath(planId)) is { } hash)
            _savedCompilationDiagnostics[planId] = new(hash, diagnostic);
    }

    private IEnumerable<string> CurrentSavedCompilationDiagnostics()
    {
        var store = new AuthoringDocumentStore(Workspace!.Root);
        foreach (var (planId, diagnostic) in _savedCompilationDiagnostics.ToArray())
        {
            if (!_sourceDocuments.TryGetValue(planId, out var source) || !source.RequiresCompilation
                || diagnostic.SourceHash != AuthoringDocumentStore.ComputeHash(store.GetDocumentPath(planId)))
            {
                _savedCompilationDiagnostics.Remove(planId);
                continue;
            }
            yield return $"{planId}: {diagnostic.Message}";
        }
    }
}
