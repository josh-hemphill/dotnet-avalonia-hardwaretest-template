namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public ProgramRemovalImpact PrepareSelectedProgramRemoval()
    {
        EnsureWritableWorkspace("review program removal");
        var target = DescribeSelectedProgramRemoval();
        return new(_workspaceSession, Workspace!.Root, target.PlanId, target.TapPlanPath, target.SidecarPath, ContentFingerprint());
    }

    public bool ApplyProgramRemoval(ProgramRemovalImpact reviewedImpact)
    {
        ArgumentNullException.ThrowIfNull(reviewedImpact);
        EnsureWritableWorkspace("remove a program");
        RequireCurrentImpact(reviewedImpact.Session, reviewedImpact.WorkspaceRoot, reviewedImpact.ContentFingerprint);
        var current = DescribeSelectedProgramRemoval();
        if (current.PlanId != reviewedImpact.PlanId || current.TapPlanPath != reviewedImpact.TapPlanPath || current.SidecarPath != reviewedImpact.SidecarPath)
            throw new AuthoringWorkspaceException("The selected program or named files changed; prepare and review a new program removal impact.");
        return RemoveSelectedProgramIfMatches(current);
    }
}
