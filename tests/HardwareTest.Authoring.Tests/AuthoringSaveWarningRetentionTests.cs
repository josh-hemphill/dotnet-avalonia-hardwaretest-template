using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringSaveWarningRetentionTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Fact]
    public void Save_all_and_home_correction_preserve_invalid_program_reason_after_later_valid_program_saves()
    {
        var vm = Open();
        AddInvalid(vm, "a-invalid"); vm.CreateProgram("z-valid");
        vm.OpenTapHomeOverride = "invalid\0home";
        var result = vm.SaveAll();
        Assert.True(result.Succeeded); Assert.Equal(["a-invalid", "z-valid"], result.SavedProgramIds);
        Assert.Empty(result.Failures); Assert.False(vm.HasUnsavedChanges); Assert.StartsWith("Saved 2 program(s)", vm.Status);
        Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.SavePreviewWarning!);
        Assert.Contains("OpenTAP home setting", vm.SavePreviewWarning!);
        var path = new AuthoringDocumentStore(vm.Workspace!.Root).GetDocumentPath("a-invalid"); var saved = File.ReadAllBytes(path);
        vm.OpenTapHomeOverride = Path.Combine(vm.Workspace.Root, "valid-missing-home");
        Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.SavePreviewWarning!);
        Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.Error!);
        Assert.DoesNotContain("OpenTAP home setting", vm.SavePreviewWarning!);
        Assert.False(vm.CanPack); Assert.True(vm.HasUncompiledSources); Assert.Equal(saved, File.ReadAllBytes(path));
        vm.SelectProgram("a-invalid"); vm.Threshold = "2"; vm.Apply();
        Assert.Null(vm.SavePreviewWarning); Assert.Null(vm.Error); Assert.False(vm.HasUncompiledSources);
    }

    [Theory]
    [InlineData("program")]
    [InlineData("sidecar")]
    [InlineData("draft")]
    public void Unrelated_successful_saves_retain_the_invalid_saved_program_reason(string mode)
    {
        var vm = Open(); AddInvalid(vm, "a-invalid"); vm.Apply();
        vm.SelectProgram("sample");
        if (mode != "draft") vm.DisplayName = "unrelated successful save";
        if (mode == "program") vm.SaveProgram("sample");
        else if (mode == "sidecar") vm.SaveSidecar();
        else { vm.CreateProgram("z-valid"); vm.Apply(); }
        Assert.False(vm.HasUnsavedChanges); Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.SavePreviewWarning!);
        Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.Error!); Assert.False(vm.CanPack);
        vm.SelectProgram("a-invalid"); vm.Threshold = "2"; vm.Apply();
        Assert.Null(vm.SavePreviewWarning); Assert.Null(vm.Error);
    }

    [Fact]
    public void Resolving_one_document_retains_another_documents_incomplete_numeric_reason()
    {
        var vm = Open(); AddInvalid(vm, "a-invalid"); vm.Apply();
        vm.CreateProgram("b-numeric"); vm.ApplyRecipe(AuthoringRecipeIds.MeanGte); vm.Threshold = "1e-"; vm.Apply();
        Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.SavePreviewWarning!);
        Assert.Contains("Incomplete numeric input", vm.SavePreviewWarning!);
        vm.SelectProgram("a-invalid"); vm.Threshold = "2"; vm.Apply();
        Assert.DoesNotContain(AuthoringCompileCodes.MissingLimits, vm.SavePreviewWarning!);
        Assert.Contains("Incomplete numeric input", vm.SavePreviewWarning!); Assert.Contains("Incomplete numeric input", vm.Error!);
        vm.SelectProgram("b-numeric"); vm.Threshold = "2"; vm.Apply();
        Assert.Null(vm.SavePreviewWarning); Assert.Null(vm.Error);
    }

    [Fact]
    public void Unrelated_save_retains_external_compiled_reconciliation_warning_until_that_document_is_resolved()
    {
        var vm = Open(); vm.SelectProgram("sample"); vm.Apply();
        File.AppendAllText(Path.Combine(vm.Workspace!.Root, "sample.TapPlan"), "\n<!-- external -->");
        vm.DisplayName = "source edit"; vm.Apply();
        vm.CreateProgram("z-valid"); vm.Apply();
        Assert.Contains("External compiled edits require reconciliation", vm.SavePreviewWarning!);
        Assert.Contains("sample", vm.CompiledConflictProgramIds);
        vm.ReconcileCompiled("sample", useCompiledContent: false); vm.SelectProgram("sample"); vm.Apply();
        Assert.Null(vm.SavePreviewWarning); Assert.Null(vm.Error); Assert.Empty(vm.CompiledConflictProgramIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deleting_document_or_replacing_workspace_removes_its_saved_warning(bool replaceWorkspace)
    {
        var vm = Open(); AddInvalid(vm, "a-invalid"); vm.Apply();
        Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.SavePreviewWarning!);
        if (replaceWorkspace) vm.Open(AuthoringBuildSnapshotTests.Workspace());
        else vm.RemoveSelectedProgram();
        Assert.Null(vm.SavePreviewWarning); Assert.Null(vm.Error);
    }

    private static AuthoringWorkspaceViewModel Open()
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        workspace.Manifest.Package.Name = "Saved warning retention"; AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.StopRecovery(); return vm;
    }
    private static void AddInvalid(AuthoringWorkspaceViewModel vm, string id)
    {
        vm.CreateProgram(id); vm.ApplyRecipe(AuthoringRecipeIds.MeanGte); vm.Threshold = string.Empty;
    }
}
