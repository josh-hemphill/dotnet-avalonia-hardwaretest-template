using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed partial class AuthoringActionableFindingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAll_preflights_second_dirty_program_artifacts_before_publishing_first(bool sidecar)
    {
        await PrepareCheckedInputsAsync(false);
        _vm.SelectProgram("board-demo");
        _vm.SaveSidecar();
        _vm.DisplayName = "first dirty program";
        _vm.SelectProgram("sample");
        _vm.DisplayName = "second dirty program";
        Assert.Equal(new[] { "board-demo", "sample" }, _vm.DirtyProgramIds);
        Assert.Contains("Stale", _vm.IssuesCheckState);
        var original = CaptureAllProgramPublicationBytes();
        var path = Path.Combine(_root, sidecar ? "sample.program.json" : "sample.TapPlan");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = _vm.SaveAll();
            Assert.False(result.Succeeded);
            Assert.Empty(result.SavedProgramIds);
            Assert.Equal(new[] { "board-demo", "sample" }, _vm.DirtyProgramIds);
            Assert.NotNull(_vm.Error);
            Assert.Equal("second dirty program", _vm.DisplayName);
        }
        AssertPublishedBytes(original);
        _vm.SelectProgram("board-demo");
        Assert.Equal("first dirty program", _vm.DisplayName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Single_save_preserves_source_and_dirty_content_when_target_artifact_is_locked(bool apply, bool sidecar)
    {
        await PrepareCheckedInputsAsync(false);
        _vm.DisplayName = "dirty source must remain unsaved";
        var original = CapturePublishedBytes();
        var path = Path.Combine(_root, sidecar ? "sample.program.json" : "sample.TapPlan");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<AuthoringWorkspaceException>(() => { if (apply) _vm.Apply(); else _vm.SaveSidecar(); });
            Assert.True(_vm.HasUnsavedChanges);
            Assert.Equal("dirty source must remain unsaved", _vm.DisplayName);
            Assert.NotNull(_vm.Error);
        }
        AssertPublishedBytes(original);
    }

    private Dictionary<string, byte[]> CaptureAllProgramPublicationBytes()
    {
        var original = CapturePublishedBytes();
        foreach (var path in new[]
        {
            new AuthoringDocumentStore(_root).GetDocumentPath("board-demo"),
            Path.Combine(_root, "board-demo.TapPlan"), Path.Combine(_root, "board-demo.program.json")
        }) original.Add(path, File.ReadAllBytes(path));
        return original;
    }
}
