using System.Text.Json;
using HardwareTest.Authoring;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class RecordingSessionSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-recording-session-" + Guid.NewGuid().ToString("N"));
    private readonly AuthoringWorkspaceViewModel _vm = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_recording_choice_does_not_cross_program_or_workspace_sessions(bool replaceWorkspace)
    {
        var first = Workspace("first");
        var second = Workspace("second");
        _vm.Open(first); _vm.StopRecovery();
        _vm.SelectProgram(_vm.Programs.Single(program => program.PlanId != "other").PlanId);
        var firstId = _vm.SelectedProgram!.PlanId;
        _vm.SelectDataset(0);
        Assert.Equal("original", _vm.SelectedDataset!.Run.RunId);
        if (replaceWorkspace) { _vm.Open(second); _vm.StopRecovery(); }
        else _vm.SelectProgram("other");
        Assert.NotEmpty(_vm.Datasets);
        Assert.Null(_vm.SelectedDataset);
        Assert.Equal(-1, _vm.SelectedDatasetIndex);
        Assert.StartsWith("Example data", _vm.DataSourceDetails);
        _vm.SelectDataset(0);
        Assert.StartsWith("Recording", _vm.DataSourceDetails);
        if (!replaceWorkspace)
        {
            _vm.SelectProgram(firstId);
            Assert.Null(_vm.SelectedDataset);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refresh_preserves_identity_across_reorder_then_clears_a_missing_or_replaced_run(bool replaceRun)
    {
        var workspace = Workspace("first");
        _vm.Open(workspace); _vm.StopRecovery();
        _vm.SelectProgram(_vm.Programs.Single(program => program.PlanId != "other").PlanId);
        _vm.SelectDataset(0);
        var selectedPath = _vm.SelectedDataset!.Path;
        Recording(workspace, _vm.SelectedProgram!.PlanId, "000-earlier", "different", 91);
        _vm.VisaAddress = "MOCK::EDITED";
        Assert.Equal(1, _vm.SelectedDatasetIndex);
        Assert.Equal(selectedPath, _vm.SelectedDataset!.Path);
        Assert.Equal("original", _vm.SelectedDataset.Run.RunId);
        if (replaceRun) Recording(workspace, _vm.SelectedProgram!.PlanId, "original", "replacement", 92);
        else File.Delete(selectedPath);
        _vm.VisaAddress = "MOCK::AGAIN";
        Assert.Equal(replaceRun ? 2 : 1, _vm.Datasets.Count);
        Assert.Null(_vm.SelectedDataset);
        Assert.Equal(-1, _vm.SelectedDatasetIndex);
    }

    private string Workspace(string name)
    {
        var workspace = Path.Combine(_root, name);
        Directory.CreateDirectory(workspace);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "dirs.proj"))) repo = repo.Parent;
        Assert.NotNull(repo);
        foreach (var file in new[] { "authoring.json", "sample.TapPlan", "sample.program.json" })
            File.Copy(Path.Combine(repo.FullName, "plans", "opentap", file), Path.Combine(workspace, file));
        var compiler = new PlanCompiler();
        var draft = compiler.Load(Path.Combine(workspace, "sample.TapPlan"));
        compiler.Save(draft with { PlanId = "other" }, Path.Combine(workspace, "other.TapPlan"));
        Recording(workspace, draft.PlanId, "original", "original", 2);
        Recording(workspace, "other", "other", "other", 72);
        return workspace;
    }

    private static void Recording(string workspace, string planId, string folder, string runId, double value)
    {
        var destination = Path.Combine(workspace, "recordings", folder);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "run.json"), JsonSerializer.Serialize(new TestRunRecord
        { PlanId = planId, RunId = runId, Samples = [new StoredSample { MetricKey = "VDC", Value = value }] }, AppJsonContext.Default.TestRunRecord));
    }

    public void Dispose()
    {
        _vm.StopRecovery();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
