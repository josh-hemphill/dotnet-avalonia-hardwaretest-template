using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringActionableFindingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-findings-" + Guid.NewGuid().ToString("N"));
    private readonly AuthoringWorkspaceViewModel _vm = new();

    public AuthoringActionableFindingsTests()
    {
        Directory.CreateDirectory(_root);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "dirs.proj"))) repo = repo.Parent;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repo!.FullName, "plans", "opentap")))
            File.Copy(file, Path.Combine(_root, Path.GetFileName(file)));
        var plan = Path.Combine(_root, "sample.TapPlan");
        File.WriteAllText(plan, File.ReadAllText(plan).Replace("<HardwareTest.Presentation.DisplayRole>timeseries</HardwareTest.Presentation.DisplayRole>", "<HardwareTest.Presentation.DisplayRole>passband</HardwareTest.Presentation.DisplayRole>", StringComparison.Ordinal));
        _vm.Open(_root);
        _vm.StopRecovery();
    }

    [Fact]
    public void Missing_limits_uses_structured_step_and_verified_mapping()
    {
        _vm.Validate();
        var row = Assert.Single(_vm.FindingRows, row => row.ProgramId == "sample" && row.Code == PlanContractValidator.Codes.MissingLimits);
        Assert.NotNull(row.Finding.Target?.CompiledStepId);
        Assert.NotNull(row.NodeId);
        Assert.False(row.IsStale);
        Assert.Equal("Open program settings", row.NavigationLabel);
        Assert.Contains("no supported editor control", row.NavigationReason);
        var target = _vm.NavigateFinding(row);
        Assert.Equal("sample", _vm.SelectedProgram!.PlanId);
        Assert.Equal("LimitLow", row.Finding.Target!.Field);
        Assert.Equal("ProgramSettings", target!.Section);
        Assert.Contains("no supported editor control", _vm.Status);
        Assert.Equal(_vm.SelectedDocument!.Revision, row.CheckedRevision);
    }

    [Fact]
    public void Plan_wide_finding_opens_settings_without_inventing_a_node()
    {
        File.Delete(Path.Combine(_root, "sample.program.json"));
        _vm.Validate();
        var row = Assert.Single(_vm.FindingRows, row => row.ProgramId == "sample" && row.Code == PlanContractValidator.Codes.SidecarMissing);
        Assert.Null(row.NodeId);
        Assert.Contains("No verified source field", row.NavigationReason);
        Assert.Equal("ProgramSettings", _vm.NavigateFinding(row)!.Section);
    }

    [Fact]
    public void Editing_undo_and_removed_program_refuse_old_navigation()
    {
        _vm.Validate();
        var row = _vm.FindingRows.First(row => row.NodeId is not null);
        Assert.NotNull(_vm.NavigateFinding(row));
        _vm.SelectSequence(_vm.SequenceItems.ToList().FindIndex(item => item.NodeId == row.NodeId));
        _vm.ChannelKey = "edited";
        Assert.NotEmpty(_vm.FindingRows);
        Assert.All(_vm.FindingRows, item => Assert.True(item.IsStale));
        Assert.Null(_vm.NavigateFinding(row));
        _vm.Undo();
        Assert.Null(_vm.NavigateFinding(row));
        _vm.Validate();
        var current = _vm.FindingRows.First(item => item.ProgramId == "sample");
        _vm.SelectProgram("sample");
        _vm.RemoveSelectedProgram();
        Assert.Null(_vm.NavigateFinding(current));
    }

    [Theory]
    [InlineData("undo")]
    [InlineData("catalog")]
    [InlineData("remove")]
    public async Task Async_check_keeps_checked_revision_stale_after_changes(string change)
    {
        File.WriteAllText(Path.Combine(_root, "fixture-wait"), "");
        _vm.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(
            Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")), action => action());
        var operation = _vm.RunOperationAsync(AuthoringOperationKind.Validate);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(_root, "fixture-child.json")))
            {
                if (operation.IsCompleted) await operation;
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(20);
            }
            _vm.SelectProgram("sample");
            if (change == "undo")
            {
                _vm.DisplayName = "edited while checking";
                _vm.Undo();
            }
            else if (change == "catalog")
            {
                _vm.NewRequiredField = "traceability";
                _vm.AddRequiredField();
            }
            else _vm.RemoveSelectedProgram();
        }
        finally { File.WriteAllText(Path.Combine(_root, "fixture-release"), ""); }
        await operation;
        if (change != "remove") Assert.NotEmpty(_vm.FindingRows);
        Assert.Contains("Stale", _vm.IssuesCheckState);
        Assert.Contains("earlier revision", _vm.Status);
        Assert.All(_vm.FindingRows, row => Assert.True(row.IsStale));
        Assert.Empty(_vm.Findings);
        if (_vm.FindingRows.Count > 0) Assert.Null(_vm.NavigateFinding(_vm.FindingRows[0]));
    }

    [Fact]
    public void Formatters_preserve_absent_target_shape_and_emit_optional_structured_metadata()
    {
        var missing = Path.Combine(_root, "absent.TapPlan");
        using var jsonWriter = new StringWriter();
        PlanContractCli.Run([missing], jsonWriter, new PlanContractOptions { Format = PlanContractFormat.Json });
        using var json = JsonDocument.Parse(jsonWriter.ToString());
        var finding = json.RootElement.GetProperty("plans")[0].GetProperty("findings")[0];
        Assert.Equal(new[] { "severity", "code", "message" }, finding.EnumerateObject().Select(property => property.Name));
        using var sarifWriter = new StringWriter();
        PlanContractCli.Run([missing], sarifWriter, new PlanContractOptions { Format = PlanContractFormat.Sarif });
        using var sarif = JsonDocument.Parse(sarifWriter.ToString());
        var result = sarif.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        Assert.False(result.TryGetProperty("properties", out _));
        Assert.False(result.GetProperty("locations")[0].GetProperty("physicalLocation").TryGetProperty("region", out _));
        var batch = new PlanContractBatchReport
        {
            Plans = [new PlanContractReport { TargetPath = missing,
            Findings = [new(PlanContractSeverity.Error, "CODE", "message")] }]
        };
        var text = PlanContractValidator.Format(batch);
        var targeted = new PlanContractBatchReport
        {
            Plans = [new PlanContractReport { TargetPath = missing,
            Findings = [new(PlanContractSeverity.Error, "CODE", "message") { Target = new(Field: "Threshold") }] }]
        };
        Assert.Equal(text, PlanContractValidator.Format(targeted));
        using var structuredWriter = new StringWriter();
        PlanContractCli.Run([Path.Combine(_root, "sample.TapPlan")], structuredWriter, new PlanContractOptions { Format = PlanContractFormat.Json });
        using var structured = JsonDocument.Parse(structuredWriter.ToString());
        var threshold = structured.RootElement.GetProperty("plans")[0].GetProperty("findings").EnumerateArray()
            .Single(item => item.GetProperty("code").GetString() == PlanContractValidator.Codes.MissingLimits);
        Assert.Equal("LimitLow", threshold.GetProperty("target").GetProperty("field").GetString());
    }

    [Fact]
    public void Dirty_validation_has_one_actionable_explanation()
    {
        _vm.SelectProgram("sample");
        _vm.DisplayName = "unsaved";
        var error = Assert.Throws<AuthoringWorkspaceException>(() => _vm.Validate());
        _vm.ReportError(error.Message);
        Assert.Contains("Save all edited programs", _vm.ValidationScope);
        Assert.Null(_vm.Error);
        Assert.Null(_vm.Status);
    }

    [Fact]
    public void Compiler_maps_preserve_repeat_children_and_honest_opaque_generated_exclusions()
    {
        var metric = new MetricNode(_vm.Programs.First().Measure.OfType<MetricNode>().First().Metric);
        var opaque = new RawStepNode("unsupported", "<TestStep />");
        var excluded = new MetricNode(metric.Metric with { Source = new ExpressionAlgorithm(["VDC"], "std(VDC)") });
        var repeat = new RepeatNode(3, [metric, opaque, excluded]);
        var draft = _vm.Programs.First() with { Measure = [repeat] };
        draft.AuthoringState.FormulaIntent[excluded.NodeId] = FormulaDeploymentIntent.Explore;
        var generated = Guid.NewGuid();
        var path = Path.Combine(_root, "map.TapPlan");
        new XDocument(new XElement("TestPlan", new[] { repeat.NodeId, metric.NodeId, opaque.NodeId, generated }
            .Select(id => new XElement("TestStep", new XAttribute("Id", id))))).Save(path);
        var map = AuthoringBuildService.CompileMap(AuthoringFormulaDeployment.Project(draft), path);
        Assert.Equal(metric.NodeId, Assert.Single(map, item => item.StepId == metric.NodeId).NodeId);
        Assert.Equal(repeat.NodeId, Assert.Single(map, item => item.StepId == repeat.NodeId).NodeId);
        Assert.Null(Assert.Single(map, item => item.StepId == opaque.NodeId).NodeId);
        Assert.Null(Assert.Single(map, item => item.StepId == generated).NodeId);
        Assert.DoesNotContain(map, item => item.NodeId == excluded.NodeId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Saving_without_edits_invalidates_an_empty_checked_report(bool apply, bool existingSource)
    {
        RestoreCleanSample();
        _vm.Open(_root);
        _vm.StopRecovery();
        _vm.SelectProgram("sample");
        if (existingSource) _vm.SaveSidecar();
        Assert.Empty(_vm.Validate().Plans.SelectMany(plan => plan.Findings));
        Assert.Contains("Current", _vm.IssuesCheckState);
        Assert.False(_vm.HasUnsavedChanges);
        var sourcePath = new AuthoringDocumentStore(_root).GetDocumentPath("sample");
        var before = AuthoringDocumentStore.ComputeHash(sourcePath);
        if (apply) _vm.Apply(); else _vm.SaveSidecar();
        Assert.NotEqual(before, AuthoringDocumentStore.ComputeHash(sourcePath));
        Assert.Empty(_vm.FindingRows);
        Assert.Contains("Stale", _vm.IssuesCheckState);
    }

    [Fact]
    public void Partially_published_save_keeps_original_failure_and_invalidates_empty_report()
    {
        RestoreCleanSample();
        var vm = new AuthoringWorkspaceViewModel(new FailingSidecarCompiler());
        vm.Open(_root);
        vm.StopRecovery();
        try
        {
            vm.SelectProgram("sample");
            Assert.Empty(vm.Validate().Plans.SelectMany(plan => plan.Findings));
            Assert.Contains("Current", vm.IssuesCheckState);
            Assert.Equal("sidecar publication failed", Assert.Throws<IOException>(vm.SaveSidecar).Message);
            Assert.True(File.Exists(new AuthoringDocumentStore(_root).GetDocumentPath("sample")));
            Assert.Empty(vm.FindingRows);
            Assert.Contains("Stale", vm.IssuesCheckState);
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Destination_policy_requires_a_unique_supported_field_without_changing_selection()
    {
        var originalProgram = _vm.SelectedProgram;
        var originalNode = _vm.SelectedSequence;
        var metric = new MetricNode(_vm.Programs.First().Measure.OfType<MetricNode>().First().Metric with
        { Source = new ExpressionAlgorithm(["VDC"], "mean(VDC)") });
        var draft = _vm.Programs.First() with { Measure = [metric] };
        var target = new PlanContractTarget(NodeId: metric.NodeId, Field: "Threshold", Section: "Configure");
        var supported = AuthoringFindingNavigation.Resolve(draft, metric.NodeId, target);
        Assert.Equal("Go to field", supported.Label);
        Assert.Equal(metric.NodeId, supported.Target.NodeId);
        var unsupported = AuthoringFindingNavigation.Resolve(draft, metric.NodeId, target with { Field = "LimitLow" });
        Assert.Equal("Open program settings", unsupported.Label);
        Assert.Null(unsupported.Target.NodeId);
        var ambiguous = AuthoringFindingNavigation.Resolve(draft with { Measure = [metric, metric] }, metric.NodeId, target);
        Assert.Equal("Open program settings", ambiguous.Label);
        Assert.Null(ambiguous.Target.NodeId);
        Assert.Same(originalProgram, _vm.SelectedProgram);
        Assert.Same(originalNode, _vm.SelectedSequence);
    }

    private void RestoreCleanSample()
    {
        var path = Path.Combine(_root, "sample.TapPlan");
        File.WriteAllText(path, File.ReadAllText(path).Replace("<HardwareTest.Presentation.DisplayRole>passband</HardwareTest.Presentation.DisplayRole>",
            "<HardwareTest.Presentation.DisplayRole>timeseries</HardwareTest.Presentation.DisplayRole>", StringComparison.Ordinal));
        foreach (var other in Directory.EnumerateFiles(_root, "*.TapPlan").Where(file => file != path)) File.Delete(other);
    }

    private sealed class FailingSidecarCompiler : IPlanCompiler
    {
        private readonly PlanCompiler _inner = new();
        public void Save(ProgramDraft draft, string path) => _inner.Save(draft, path);
        public ProgramDraft Load(string path) => _inner.Load(path);
        public DraftWorkspace LoadAll(AuthoringWorkspace workspace) => _inner.LoadAll(workspace);
        public void SaveSidecar(string path, ProgramSidecar sidecar) => throw new IOException("sidecar publication failed");
    }

    public void Dispose()
    {
        _vm.StopOperations();
        _vm.StopRecovery();
        Directory.Delete(_root, true);
    }
}
