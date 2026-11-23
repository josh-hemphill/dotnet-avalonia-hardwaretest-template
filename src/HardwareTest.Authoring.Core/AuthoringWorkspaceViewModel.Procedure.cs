namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public bool HasImportedProcedure => SelectedProgram?.ImportedPlanXml is not null;

    public ProcedureEditorSession OpenProcedureEditor()
    {
        var workspace = Workspace ?? throw new AuthoringWorkspaceException("Open a workspace.");
        var draft = SelectedProgram ?? throw new AuthoringWorkspaceException("Select a procedure.");
        var path = TryExistingTapPlanPath(draft.PlanId) ?? ResolveTapPlanPath(draft.PlanId);
        var home = ResolveCompilationHome()?.Root;
        return new(draft, path, workspace.Manifest, home);
    }

    public void ApplyProcedureEdit(ProgramDraft original, ProgramDraft edited)
    {
        if (SelectedProgram is null || original.PlanId != edited.PlanId || SelectedProgram.PlanId != original.PlanId
            || !AuthoringDocumentSnapshot.Capture(original).ContentEquals(AuthoringDocumentSnapshot.Capture(SelectedProgram)))
        {
            throw new AuthoringWorkspaceException("The selected procedure changed. Reopen its editor before applying the edit.");
        }

        if (CommitDocument(edited, "Edit imported procedure and data", procedureEdit: true))
        {
            PresentSession(_documents[edited.PlanId]);
            Status = "Procedure draft updated. Use Save all to publish it.";
        }
    }
}
