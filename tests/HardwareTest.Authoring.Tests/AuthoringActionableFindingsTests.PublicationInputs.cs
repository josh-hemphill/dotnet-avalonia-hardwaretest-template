using System.Text.Json.Nodes;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed partial class AuthoringActionableFindingsTests
{
    public static IEnumerable<object[]> InvalidManifestChecks =>
        from async in new[] { false, true }
        from empty in new[] { false, true }
        from change in new[] { "invalid", "future", "changed" }
        select new object[] { async, empty, change };

    [Theory]
    [MemberData(nameof(InvalidManifestChecks))]
    public async Task Validation_refuses_actual_saved_manifest_unsupported_or_changed_catalog(bool async, bool empty, string change)
    {
        PrepareCheckedInputs(empty);
        ConfigureValidationChild();
        var path = CheckedInputPath("manifest");
        ChangeSavedInput(path, change);
        var original = File.ReadAllBytes(path);
        var error = await Record.ExceptionAsync(async () =>
        {
            if (async) await _vm.RunOperationAsync(AuthoringOperationKind.Validate);
            else _vm.Validate();
        });
        Assert.IsType<AuthoringWorkspaceException>(error);
        AssertCheckedStateStale();
        Assert.Contains("verify", _vm.Error);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(Path.Combine(_root, "fixture-child.json")));
    }

    public static IEnumerable<object[]> SaveInputFailures =>
        from operation in new[] { "sidecar", "apply", "all" }
        from input in new[] { "program", "manifest", "workspace" }
        from change in new[] { "lock", "future", "invalid" }
        from empty in new[] { false, true }
        select new object[] { operation, input, change, empty };

    [Theory]
    [MemberData(nameof(SaveInputFailures))]
    public void Save_refuses_unverifiable_checked_inputs_before_any_publication(string operation, string input, string change, bool empty)
    {
        PrepareCheckedInputs(empty);
        var path = CheckedInputPath(input);
        if (change != "lock") ChangeSavedInput(path, change);
        var original = CapturePublishedBytes();
        using (var locked = change == "lock" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None) : null)
        {
            AssertSaveRefused(operation);
            AssertCheckedStateStale();
            Assert.NotNull(_vm.Error);
        }
        AssertPublishedBytes(original);
    }

    public static IEnumerable<object[]> DirtySaveInputFailures =>
        from operation in new[] { "sidecar", "apply", "all" }
        from input in new[] { "program", "manifest", "workspace" }
        select new object[] { operation, input };

    [Theory]
    [MemberData(nameof(DirtySaveInputFailures))]
    public void Save_refusal_keeps_dirty_content_and_prevents_SaveAll_catalog_publication(string operation, string input)
    {
        PrepareCheckedInputs(false);
        _vm.DisplayName = "dirty content must remain";
        if (operation == "all")
        {
            _vm.NewRequiredField = "pending-field";
            _vm.AddRequiredField();
            Assert.True(_vm.WorkspaceCatalogDirty);
        }
        var original = CapturePublishedBytes();
        using (var locked = new FileStream(CheckedInputPath(input), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AssertSaveRefused(operation);
            Assert.True(_vm.HasUnsavedChanges);
            Assert.Equal("dirty content must remain", _vm.DisplayName);
            if (operation == "all") Assert.True(_vm.WorkspaceCatalogDirty);
            Assert.NotNull(_vm.Error);
        }
        AssertPublishedBytes(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Synchronous_validation_byte_change_at_completion_reports_earlier_revision(bool empty)
    {
        PrepareCheckedInputs(empty);
        var path = CheckedInputPath("manifest");
        _vm.ValidateSavedPlans = (paths, options) =>
        {
            var report = PlanContractValidator.Validate(paths, options);
            File.AppendAllText(path, "\n");
            return report;
        };
        _vm.Validate();
        AssertCheckedStateStale();
        Assert.Contains("earlier revision", _vm.Status);
        Assert.Null(_vm.Error);
    }

    [Fact]
    public void No_current_check_preserves_other_future_program_source_isolation()
    {
        var store = new AuthoringDocumentStore(_root);
        var path = store.GetDocumentPath("other");
        store.Save(AuthoringDocumentDto.FromDraft(AuthoringRecipeCatalog.CreateProgram("other")));
        ChangeSavedInput(path, "future");
        var future = File.ReadAllBytes(path);
        _vm.SelectProgram("sample");
        _vm.DisplayName = "editable despite another future source";
        var result = _vm.SaveAll();
        Assert.True(result.Succeeded);
        Assert.Equal("editable despite another future source", _vm.DisplayName);
        Assert.Equal(future, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validation_retains_a_saved_drafts_duplicate_channel_compilation_diagnostic(bool async)
    {
        PrepareCheckedInputs(true);
        var original = _vm.SelectedProgram!.Measure.OfType<MetricNode>().First();
        _vm.ReplaceSelected(_vm.SelectedProgram with { Measure = [original, new MetricNode(original.Metric with { Name = "duplicate" })] });
        _vm.Apply();
        Assert.False(_vm.HasUnsavedChanges);
        Assert.True(_vm.HasUncompiledSources);
        Assert.Contains(AuthoringCompileCodes.DuplicateChannelKey, _vm.Error);
        ConfigureValidationChild();
        var error = await Record.ExceptionAsync(async () =>
        {
            if (async) await _vm.RunOperationAsync(AuthoringOperationKind.Validate);
            else _vm.Validate();
        });
        Assert.IsType<AuthoringWorkspaceException>(error);
        Assert.Contains(AuthoringCompileCodes.DuplicateChannelKey, _vm.Error);
        AssertCheckedStateStale();
        Assert.False(File.Exists(Path.Combine(_root, "fixture-child.json")));
    }

    private static void ChangeSavedInput(string path, string change)
    {
        if (change == "invalid") { File.WriteAllText(path, "{ invalid JSON"); return; }
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        if (change == "future") json["schemaVersion"] = 999;
        else json["displayName"] = "external catalog edit";
        File.WriteAllText(path, json.ToJsonString());
    }

    private Dictionary<string, byte[]> CapturePublishedBytes() => new[]
    {
        CheckedInputPath("manifest"), CheckedInputPath("workspace"), CheckedInputPath("program"),
        Path.Combine(_root, "sample.TapPlan"), Path.Combine(_root, "sample.program.json")
    }.ToDictionary(path => path, File.ReadAllBytes);

    private static void AssertPublishedBytes(Dictionary<string, byte[]> original)
    {
        foreach (var (path, bytes) in original) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    private void AssertSaveRefused(string operation)
    {
        if (operation == "all")
        {
            var result = _vm.SaveAll();
            Assert.False(result.Succeeded);
            Assert.Empty(result.SavedProgramIds);
            Assert.False(result.WorkspaceCatalogSaved);
        }
        else Assert.Throws<AuthoringWorkspaceException>(() => { if (operation == "apply") _vm.Apply(); else _vm.SaveSidecar(); });
    }
}
