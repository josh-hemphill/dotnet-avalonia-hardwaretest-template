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
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.SelectProgram("sample"); vm.Apply();
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
        var source = AuthoringDocumentDto.FromDraft(MockDmmDraftFixture.Create("external-draft")); source.RequiresCompilation = true; store.Save(source);
        Assert.False(vm.HasUncompiledSources);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        Assert.True(vm.HasUncompiledSources); Assert.True(vm.CanPack); // Build compiles the complete saved source-only program.
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSourceExportGuard.EnsureCurrent(vm.Workspace!)); vm.StopRecovery();
    }

    [Fact]
    public void GuiValidationClearsDeletedUnknownSourceBlocker()
    {
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.Apply();
        var store = new AuthoringDocumentStore(root);
        var document = AuthoringDocumentDto.FromDraft(MockDmmDraftFixture.Create("external-deleted"));
        document.RequiresCompilation = true; store.Save(document);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate()); Assert.True(vm.HasUncompiledSources);
        store.DeleteSource(document.PlanId);
        vm.Validate();
        Assert.False(vm.HasUncompiledSources); Assert.Empty(vm.CompiledConflictProgramIds); Assert.True(vm.CanPack);
        AuthoringSourceExportGuard.EnsureCurrent(vm.Workspace!); vm.StopRecovery();
    }

    [Fact]
    public void GuiValidationClearsExternallyRepairedSourceFlagsAndCompiledConflict()
    {
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.Apply();
        var id = vm.SelectedProgram!.PlanId;
        var path = vm.Workspace!.TapPlanPaths.Single(p => Path.GetFileNameWithoutExtension(p) == id);
        var store = new AuthoringDocumentStore(root); var document = store.Load(id).Document!;
        document.RequiresCompilation = true; store.Save(document);
        File.AppendAllText(path, "\n<!-- external repair -->");
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        Assert.True(vm.HasUncompiledSources); Assert.Contains(id, vm.CompiledConflictProgramIds);
        document.RequiresCompilation = false;
        document.CompiledPlanHash = AuthoringDocumentStore.ComputeHash(path);
        document.CompiledSidecarHash = AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)); store.Save(document);
        vm.Validate();
        Assert.False(vm.HasUncompiledSources); Assert.Empty(vm.CompiledConflictProgramIds); Assert.True(vm.CanPack);
        AuthoringSourceExportGuard.EnsureCurrent(vm.Workspace); vm.StopRecovery();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuiReadinessRefreshPreservesDurableRetainedSourceCompilationRequirement(bool deleteSource)
    {
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.SelectProgram("sample"); vm.Apply();
        var id = vm.SelectedProgram!.PlanId;
        var path = vm.Workspace!.TapPlanPaths.Single(p => Path.GetFileNameWithoutExtension(p) == id);
        File.AppendAllText(path, "\n<!-- external -->");
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        vm.ReconcileCompiled(id, useCompiledContent: false);
        if (deleteSource) new AuthoringDocumentStore(root).DeleteSource(id);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        Assert.True(vm.HasUncompiledSources); Assert.Empty(vm.CompiledConflictProgramIds); Assert.Equal(!deleteSource, vm.CanPack);
        vm.SaveProgram(id); vm.Validate(); Assert.False(vm.HasUncompiledSources); vm.StopRecovery();
    }

    [Fact]
    public void GuiReadinessRefreshRetainsKnownSourceWithoutCompiledBaselineAfterDeletion()
    {
        var root = Workspace(); var initial = new AuthoringWorkspaceViewModel(); initial.Open(root); initial.SelectProgram("sample"); initial.Apply();
        var id = initial.SelectedProgram!.PlanId; initial.StopRecovery();
        var store = new AuthoringDocumentStore(root); var source = store.Load(id).Document!;
        source.RequiresCompilation = false; source.CompiledPlanHash = null; source.CompiledSidecarHash = null;
        source.Sidecar.DisplayName = "Current saved source"; store.Save(source);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        Assert.True(vm.HasUncompiledSources);
        store.DeleteSource(id);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        Assert.True(vm.HasUncompiledSources); Assert.False(vm.CanPack);
        vm.ReconcileCompiled(id, useCompiledContent: false);
        vm.SaveProgram(id); vm.Validate();
        Assert.False(vm.HasUncompiledSources);
        Assert.Equal("Current saved source", store.Load(id).Document!.Sidecar.DisplayName); vm.StopRecovery();
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
