using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringDraftReviewRegressionTests
{
    [Fact]
    public void FailedSidecarExportLeavesDurableSourceMarkedUncompiledOnReopen()
    {
        var root = Workspace();
        var fail = false;
        var compiler = new PlanCompiler(null, (temporary, destination) =>
        {
            if (fail && destination.EndsWith(".program.json", StringComparison.Ordinal)) throw new IOException("sidecar export failed");
            File.Move(temporary, destination, true);
        });
        var vm = new AuthoringWorkspaceViewModel(compiler); vm.Open(root); vm.Apply();
        var id = vm.SelectedProgram!.PlanId;
        var path = vm.Workspace!.TapPlanPaths.Single(p => Path.GetFileNameWithoutExtension(p) == id);
        var original = File.ReadAllBytes(PlanCompiler.SidecarPath(path));
        vm.DisplayName = "Changed source sidecar"; fail = true;
        Assert.Throws<IOException>(() => vm.SaveSidecar());
        Assert.Equal(original, File.ReadAllBytes(PlanCompiler.SidecarPath(path)));
        var store = new AuthoringDocumentStore(root);
        Assert.True(store.Load(id).Document!.RequiresCompilation);
        vm.StopRecovery();
        var reopened = new AuthoringWorkspaceViewModel(); reopened.Open(root); reopened.SelectProgram(id);
        Assert.Equal("Changed source sidecar", reopened.DisplayName);
        Assert.True(reopened.HasUncompiledSources);
        Assert.False(reopened.CanPack);
        Assert.Throws<AuthoringWorkspaceException>(() => reopened.Validate());
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSourceExportGuard.EnsureCurrent(reopened.Workspace!));
        reopened.StopRecovery();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialCatalogSaveCannotSilentlyOverlayOldWorkspaceSource(bool backupFailure)
    {
        var root = Workspace();
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        vm.NewRequiredField = "first"; vm.AddRequiredField();
        Assert.True(vm.SaveAll().Succeeded);
        var store = new AuthoringDocumentStore(root);
        var sourcePath = store.GetWorkspacePath();
        var original = File.ReadAllBytes(sourcePath);
        vm.NewRequiredField = "second"; vm.AddRequiredField();
        if (backupFailure)
        {
            File.Delete(sourcePath + ".bak"); Directory.CreateDirectory(sourcePath + ".bak");
        }
        else vm.WorkspaceSourceReplacement = (_, _) => throw new IOException("workspace source replacement failed");
        var result = vm.SaveAll();
        Assert.False(result.Succeeded);
        Assert.NotNull(result.WorkspaceCatalogFailure);
        Assert.True(vm.WorkspaceCatalogDirty);
        Assert.Equal(original, File.ReadAllBytes(sourcePath));
        Assert.Contains("second", AuthoringWorkspaceLoader.Load(root).Manifest.Catalogs!.RequiredFields);
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => new AuthoringWorkspaceViewModel().Open(root));
        Assert.Contains("Workspace catalog conflict", ex.Message, StringComparison.Ordinal);
        Assert.Contains("backup", ex.Message, StringComparison.OrdinalIgnoreCase);
        vm.StopRecovery();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoToSavedBaselineCancelsPendingOrCommittedRecovery(bool waitForCheckpoint)
    {
        var root = Workspace();
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.Apply();
        var id = vm.SelectedProgram!.PlanId;
        var path = new AuthoringDocumentStore(root).GetRecoveryPath(id);
        vm.DisplayName = "Recoverable edit";
        if (waitForCheckpoint)
        {
            for (var i = 0; i < 100 && !File.Exists(path); i++) await Task.Delay(20);
            Assert.True(File.Exists(path));
        }
        vm.Undo();
        Assert.False(vm.HasUnsavedChanges);
        await Task.Delay(900);
        Assert.False(File.Exists(path));
        vm.StopRecovery();
        var reopened = new AuthoringWorkspaceViewModel(); reopened.Open(root);
        Assert.DoesNotContain(id, reopened.RecoverableProgramIds); reopened.StopRecovery();
    }

    private static string Workspace()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        var root = Path.Combine(Path.GetTempPath(), "authoring-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, "plans", "opentap")))
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        return root;
    }
}
