namespace HardwareTest.Authoring;

/// Each explicit committed operation contributes exactly one isolated history entry.
public static class AuthoringEditService
{
    public static bool Apply(AuthoringDocumentSession session, string description,
        Func<ProgramDraft, ProgramDraft> edit, Guid? targetNodeId = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.CommitEdit(description, edit, targetNodeId);
    }
}
