using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed partial class AuthoringActionableFindingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validation_retains_the_first_program_compile_blocker_after_a_second_program_saves(bool asynchronous)
    {
        _vm.CreateDemoProgram("a-duplicate");
        _vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var metric = Assert.IsType<MetricNode>(Assert.Single(_vm.SelectedProgram!.Measure));
        _vm.ReplaceSelected(_vm.SelectedProgram with
        {
            Measure = [metric, new MetricNode(metric.Metric with { Name = "duplicate" })],
        });
        _vm.CreateDemoProgram("z-valid");
        _vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var result = _vm.SaveAll();
        Assert.True(result.Succeeded);
        Assert.Equal(["a-duplicate", "z-valid"], result.SavedProgramIds);
        var store = new AuthoringDocumentStore(_root);
        Assert.True(store.Load("a-duplicate").Document!.RequiresCompilation);
        Assert.False(store.Load("z-valid").Document!.RequiresCompilation);
        var paths = new[] { store.GetDocumentPath("a-duplicate"), store.GetDocumentPath("z-valid") };
        var before = paths.Select(File.ReadAllBytes).ToArray();
        if (asynchronous)
        {
            ConfigureValidationChild();
            await Assert.ThrowsAsync<AuthoringWorkspaceException>(() => _vm.RunOperationAsync(AuthoringOperationKind.Validate));
        }
        else Assert.Throws<AuthoringWorkspaceException>(() => _vm.Validate());
        Assert.Contains(AuthoringCompileCodes.DuplicateChannelKey, _vm.Error, StringComparison.Ordinal);
        for (var i = 0; i < paths.Length; i++) Assert.Equal(before[i], File.ReadAllBytes(paths[i]));
        Assert.False(_vm.HasUnsavedChanges);

        _vm.SelectProgram("a-duplicate");
        _vm.ReplaceSelected(_vm.SelectedProgram! with { Measure = [metric] });
        _vm.Apply();
        _vm.Validate();
        Assert.DoesNotContain(AuthoringCompileCodes.DuplicateChannelKey, _vm.Error ?? "", StringComparison.Ordinal);
        Assert.False(_vm.HasUncompiledSources);
    }
    [Theory]
    [InlineData("saved-bytes")]
    [InlineData("reopen")]
    public void Compilation_diagnostic_is_not_reused_after_saved_source_or_workspace_session_changes(string change)
    {
        _vm.CreateDemoProgram("duplicate");
        _vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var metric = Assert.IsType<MetricNode>(Assert.Single(_vm.SelectedProgram!.Measure));
        _vm.ReplaceSelected(_vm.SelectedProgram with
        {
            Measure = [metric, new MetricNode(metric.Metric with { Name = "duplicate" })],
        });
        Assert.True(_vm.SaveAll().Succeeded);
        Assert.Throws<AuthoringWorkspaceException>(() => _vm.Validate());
        Assert.Contains(AuthoringCompileCodes.DuplicateChannelKey, _vm.Error, StringComparison.Ordinal);
        var store = new AuthoringDocumentStore(_root);
        if (change == "saved-bytes")
        {
            var document = store.Load("duplicate").Document!;
            document.Measure = [document.Measure[0]];
            store.Save(document);
        }
        else
        {
            _vm.Open(_root);
            _vm.StopRecovery();
        }
        var before = File.ReadAllBytes(store.GetDocumentPath("duplicate"));
        Assert.Throws<AuthoringWorkspaceException>(() => _vm.Validate());
        Assert.Contains("SOURCE_COMPILE_REQUIRED", _vm.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthoringCompileCodes.DuplicateChannelKey, _vm.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(store.GetDocumentPath("duplicate")));
    }

}
