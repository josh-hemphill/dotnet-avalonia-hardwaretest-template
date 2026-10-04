using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringDeletionAndValidationGuardTests
{
    [Theory]
    [InlineData("future")]
    [InlineData("corrupt")]
    [InlineData("recovery-link")]
    [InlineData("plan-link")]
    public void RejectedDeletionPreservesAllFilesSelectionPathsAndHistory(string reason)
    {
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.Apply();
        var id = vm.SelectedProgram!.PlanId;
        vm.DisplayName = "Unsaved history entry";
        vm.StopRecovery();
        var document = vm.SelectedDocument!;
        var snapshot = document.Snapshot;
        var revision = document.Revision;
        var programs = vm.Programs;
        var files = vm.Workspace!;
        var path = files.TapPlanPaths.Single(p => Path.GetFileNameWithoutExtension(p) == id);
        var sidecar = PlanCompiler.SidecarPath(path);
        var store = new AuthoringDocumentStore(root);
        var source = store.GetDocumentPath(id);
        string? outside = null;
        if (reason == "future") File.WriteAllText(source, "{\"schemaVersion\":999}");
        else if (reason == "corrupt") File.WriteAllText(source, "{broken");
        else
        {
            outside = Path.Combine(Path.GetTempPath(), "preserved-program-" + Guid.NewGuid().ToString("N"));
            if (reason == "plan-link")
            {
                File.Copy(path, outside); File.Delete(path); File.CreateSymbolicLink(path, outside);
            }
            else
            {
                var recovery = store.GetRecoveryPath(id); Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
                File.WriteAllText(outside, "external checkpoint"); File.CreateSymbolicLink(recovery, outside);
            }
        }
        var planBytes = File.ReadAllBytes(path); var sidecarBytes = File.ReadAllBytes(sidecar); var sourceBytes = File.ReadAllBytes(source);
        if (reason is "future" or "corrupt") Assert.Throws<AuthoringWorkspaceException>(() => vm.RemoveSelectedProgram());
        else Assert.Throws<IOException>(() => vm.RemoveSelectedProgram());
        Assert.Equal(planBytes, File.ReadAllBytes(path)); Assert.Equal(sidecarBytes, File.ReadAllBytes(sidecar)); Assert.Equal(sourceBytes, File.ReadAllBytes(source));
        Assert.Same(programs, vm.Programs); Assert.Same(files, vm.Workspace); Assert.Same(document, vm.SelectedDocument);
        Assert.Equal(id, vm.SelectedProgram!.PlanId); Assert.Equal(revision, document.Revision); Assert.True(snapshot.ContentEquals(document.Snapshot)); Assert.True(document.CanUndo);
        if (outside is not null) Assert.True(File.Exists(outside));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuiValidationRechecksExternalCompiledEditsAndExposesReconciliation(bool sidecar)
    {
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.Apply();
        var id = vm.SelectedProgram!.PlanId;
        var path = vm.Workspace!.TapPlanPaths.Single(p => Path.GetFileNameWithoutExtension(p) == id);
        if (sidecar) File.AppendAllText(PlanCompiler.SidecarPath(path), "\n "); else File.AppendAllText(path, "\n<!-- external -->");
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        Assert.Contains(id, vm.CompiledConflictProgramIds); Assert.False(vm.CanPack);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSourceExportGuard.EnsureCurrent(vm.Workspace));
        vm.ReconcileCompiled(id, useCompiledContent: false); vm.Apply();
        Assert.Empty(vm.CompiledConflictProgramIds); vm.Validate(); vm.StopRecovery();
    }

    [Fact]
    public void GuiValidationRechecksNewUncompiledSourceWrittenByAnotherSession()
    {
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.Apply();
        var store = new AuthoringDocumentStore(root);
        var source = AuthoringDocumentDto.FromDraft(AuthoringRecipeCatalog.CreateProgram("external-draft")); source.RequiresCompilation = true; store.Save(source);
        Assert.False(vm.HasUncompiledSources);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        Assert.True(vm.HasUncompiledSources); Assert.False(vm.CanPack);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSourceExportGuard.EnsureCurrent(vm.Workspace!)); vm.StopRecovery();
    }

    private static string Workspace()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        var root = Path.Combine(Path.GetTempPath(), "authoring-deletion-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, "plans", "opentap"))) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        return root;
    }
}
