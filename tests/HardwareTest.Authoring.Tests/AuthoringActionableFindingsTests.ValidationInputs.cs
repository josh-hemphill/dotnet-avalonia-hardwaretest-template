using System.Text.Json.Nodes;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed partial class AuthoringActionableFindingsTests
{
    [Theory]
    [InlineData(false, false, "program")]
    [InlineData(false, true, "program")]
    [InlineData(true, false, "program")]
    [InlineData(true, true, "program")]
    [InlineData(false, false, "manifest")]
    [InlineData(false, true, "manifest")]
    [InlineData(true, false, "manifest")]
    [InlineData(true, true, "manifest")]
    [InlineData(false, false, "workspace")]
    [InlineData(false, true, "workspace")]
    [InlineData(true, false, "workspace")]
    [InlineData(true, true, "workspace")]
    public async Task Locked_inputs_before_validation_invalidate_previous_rows_or_empty_report(bool async, bool empty, string input)
    {
        PrepareCheckedInputs(empty);
        ConfigureValidationChild();
        var path = CheckedInputPath(input);
        var original = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                if (async) await _vm.RunOperationAsync(AuthoringOperationKind.Validate);
                else _vm.Validate();
            });
            AssertCheckedStateStale();
            Assert.IsType<AuthoringWorkspaceException>(error);
            Assert.Contains("verify", _vm.Error);
            Assert.False(_vm.OperationBusy);
        }
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(Path.Combine(_root, "fixture-child.json")));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unsupported_saved_source_invalidates_previous_check_before_validation(bool async, bool empty)
    {
        PrepareCheckedInputs(empty);
        ConfigureValidationChild();
        var path = CheckedInputPath("program");
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["schemaVersion"] = 999;
        File.WriteAllText(path, json.ToJsonString());
        var unsupported = File.ReadAllBytes(path);
        var error = await Record.ExceptionAsync(async () =>
        {
            if (async) await _vm.RunOperationAsync(AuthoringOperationKind.Validate);
            else _vm.Validate();
        });
        AssertCheckedStateStale();
        Assert.IsType<AuthoringWorkspaceException>(error);
        Assert.Contains("SOURCE_READ_ONLY", error!.Message);
        Assert.Equal(unsupported, File.ReadAllBytes(path));
        Assert.False(File.Exists(Path.Combine(_root, "fixture-child.json")));
    }

    [Theory]
    [InlineData("manifest", false)]
    [InlineData("manifest", true)]
    [InlineData("workspace", false)]
    [InlineData("workspace", true)]
    public void Catalog_saved_bytes_must_remain_verifiable_before_navigation(string input, bool locked)
    {
        PrepareCheckedInputs(false);
        var row = _vm.FindingRows.First();
        var path = CheckedInputPath(input);
        var original = File.ReadAllBytes(path);
        if (!locked) File.AppendAllText(path, "\n");
        using (var held = locked ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None) : null)
        {
            Assert.Null(_vm.NavigateFinding(row));
            AssertCheckedStateStale();
            Assert.NotNull(_vm.Error);
        }
        Assert.Equal(locked ? original : original.Concat(new byte[] { 10 }).ToArray(), File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("manifest", false, false)]
    [InlineData("manifest", false, true)]
    [InlineData("manifest", true, false)]
    [InlineData("manifest", true, true)]
    [InlineData("workspace", false, false)]
    [InlineData("workspace", false, true)]
    [InlineData("workspace", true, false)]
    [InlineData("workspace", true, true)]
    public async Task Catalog_saved_bytes_changed_during_validation_cannot_become_current(string input, bool locked, bool empty)
    {
        PrepareCheckedInputs(empty);
        File.WriteAllText(Path.Combine(_root, "fixture-result-wait"), "");
        ConfigureValidationChild();
        var path = CheckedInputPath(input);
        var original = File.ReadAllBytes(path);
        var operation = _vm.RunOperationAsync(AuthoringOperationKind.Validate);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(_root, "fixture-prepared")))
            {
                if (operation.IsCompleted) await operation;
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(20);
            }
            if (!locked) File.AppendAllText(path, "\n");
            using (var held = locked ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None) : null)
            {
                File.WriteAllText(Path.Combine(_root, "fixture-release"), "");
                var error = await Record.ExceptionAsync(async () => await operation);
                AssertCheckedStateStale();
                if (locked)
                {
                    Assert.IsType<AuthoringWorkspaceException>(error);
                    Assert.Contains("verify", _vm.Error);
                    Assert.Equal("Authoring operation failed", _vm.Status);
                }
                else
                {
                    Assert.Null(error);
                    Assert.Contains("earlier revision", _vm.Status);
                }
            }
            Assert.Equal(locked ? original : original.Concat(new byte[] { 10 }).ToArray(), File.ReadAllBytes(path));
        }
        finally
        {
            File.WriteAllText(Path.Combine(_root, "fixture-release"), "");
            await Record.ExceptionAsync(async () => await operation);
        }
    }

    private void PrepareCheckedInputs(bool empty)
    {
        if (empty)
        {
            RestoreCleanSample();
            _vm.Open(_root);
            _vm.StopRecovery();
        }
        _vm.SelectProgram("sample");
        _vm.SaveSidecar();
        new AuthoringDocumentStore(_root).SaveWorkspace(_vm.Workspace!.Manifest);
        _vm.Validate();
        Assert.Contains("Current", _vm.IssuesCheckState);
        Assert.Equal(empty, _vm.FindingRows.Count == 0);
    }

    private string CheckedInputPath(string input) => input switch
    {
        "manifest" => Path.Combine(_root, AuthoringWorkspaceLoader.ManifestFileName),
        "workspace" => new AuthoringDocumentStore(_root).GetWorkspacePath(),
        _ => new AuthoringDocumentStore(_root).GetDocumentPath("sample")
    };

    private void ConfigureValidationChild() => _vm.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(
        Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")), action => action());

    private void AssertCheckedStateStale()
    {
        Assert.Contains("Stale", _vm.IssuesCheckState);
        Assert.All(_vm.FindingRows, item => Assert.True(item.IsStale));
        Assert.Empty(_vm.Findings);
    }
}
