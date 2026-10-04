namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private FindingCheck PrepareFindingCheck() => VerifyFindingInputs(() =>
    {
        RefreshSourceReadiness();
        AuthoringSourceExportGuard.EnsureCurrent(Workspace!);
        if (HasUncompiledSources || _compiledConflicts.Count > 0)
            throw new AuthoringWorkspaceException("Compile saved drafts and reconcile external edits before validating compiled plans.");
        return CaptureFindingCheck();
    });

    private T VerifyFindingInputs<T>(Func<T> verify)
    {
        try { return verify(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or AuthoringWorkspaceException)
        {
            InvalidateContractFindings();
            var message = error is AuthoringWorkspaceException
                ? $"Saved validation inputs could not be verified: {error.Message}"
                : "The checked source or artifact could not be read to verify validation. Validate again.";
            ReportError(message);
            throw new AuthoringWorkspaceException(message, error);
        }
    }
}
