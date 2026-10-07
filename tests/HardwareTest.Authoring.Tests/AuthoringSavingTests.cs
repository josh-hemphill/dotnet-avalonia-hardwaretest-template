using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringSavingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-saving-" + Guid.NewGuid().ToString("N"));

    public AuthoringSavingTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        Directory.CreateDirectory(_root);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, "plans", "opentap")))
            File.Copy(file, Path.Combine(_root, Path.GetFileName(file)));
    }

    [Fact]
    public void Save_one_then_all_preserves_drafts_rows_and_selected_sequence()
    {
        var compiler = new RecordingCompiler();
        var vm = Open(compiler);
        vm.SelectProgram("sample");
        vm.DisplayName = "sidecar edit";
        var sample = vm.SelectedProgram;
        var row = vm.SelectedProgramRow;
        vm.CreateDemoProgram("new-program");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        var selected = vm.SelectedProgram;
        var sequence = vm.SelectedSequence;
        var measureIndex = vm.SelectedMeasureIndex;
        var rows = vm.ProgramRows;

        vm.SaveProgram("sample");
        Assert.Equal("new-program", Assert.Single(vm.DirtyPrograms).PlanId);
        Assert.Same(sample, vm.Programs.Single(p => p.PlanId == "sample"));
        Assert.Same(row, vm.ProgramRows.Single(p => p.PlanId == "sample"));
        Assert.Same(selected, vm.SelectedProgram);
        Assert.Same(sequence, vm.SelectedSequence);
        Assert.Equal(measureIndex, vm.SelectedMeasureIndex);
        Assert.Same(rows, vm.ProgramRows);

        var result = vm.SaveAll();
        Assert.True(result.Succeeded);
        Assert.Equal(["new-program"], result.SavedProgramIds);
        Assert.Empty(result.Failures);
        Assert.False(vm.HasUnsavedChanges);
        Assert.Same(selected, vm.SelectedProgram);
        Assert.Same(sequence, vm.SelectedSequence);
        Assert.Contains(vm.Workspace!.TapPlanPaths, path => Path.GetFileName(path) == "new-program.TapPlan");
        Assert.Equal(1, compiler.LoadCount);
        Assert.Equal(["sample"], compiler.SidecarSaves);
        Assert.Equal(["new-program"], compiler.PlanSaves);
    }

    [Theory]
    [InlineData("program")]
    [InlineData("sidecar")]
    [InlineData("plan")]
    [InlineData("all")]
    public void Saved_program_remains_successful_when_optional_preview_refresh_fails(string action)
    {
        var vm = Open();
        vm.DisplayName = "persisted despite preview failure";
        vm.SelectMeasure(0);
        var draft = vm.SelectedProgram;
        var sequence = vm.SelectedSequence;
        var row = vm.SelectedProgramRow;
        var planPath = Path.Combine(_root, "sample.TapPlan");
        var oldPlan = File.ReadAllBytes(planPath);
        vm.OpenTapHomeOverride = "invalid\0home";

        switch (action)
        {
            case "program": vm.SaveProgram("sample"); break;
            case "sidecar": vm.SaveSidecar(); break;
            case "plan": vm.Apply(); break;
            case "all":
                var result = vm.SaveAll();
                Assert.True(result.Succeeded);
                Assert.Equal(["sample"], result.SavedProgramIds);
                Assert.Empty(result.Failures);
                Assert.Equal(["Saved sample"], vm.SaveAllResults);
                break;
        }

        Assert.False(vm.HasUnsavedChanges);
        Assert.Empty(vm.DirtyPrograms);
        Assert.Equal("persisted despite preview failure", new PlanCompiler().Load(planPath).Sidecar.DisplayName);
        if (action != "plan") Assert.Equal(oldPlan, File.ReadAllBytes(planPath));
        Assert.Same(draft, vm.SelectedProgram);
        Assert.Same(sequence, vm.SelectedSequence);
        Assert.Same(row, vm.SelectedProgramRow);
        Assert.Contains("Packaging preview could not refresh", vm.SavePreviewWarning);
        Assert.Contains("OpenTAP home setting", vm.SavePreviewWarning);
        Assert.Equal(vm.SavePreviewWarning, vm.Error);
        Assert.StartsWith("Saved ", vm.Status);
    }

    [Fact]
    public void Save_all_retains_preview_warning_across_programs_and_can_refresh_after_correction()
    {
        var vm = Open();
        vm.DisplayName = "saved sample with preview warning";
        vm.InitializePlan(new("new-preview-program") { Instruments = [] });
        vm.DisplayName += " edited";
        var draft = vm.SelectedProgram;
        vm.OpenTapHomeOverride = "invalid\0home";
        var result = vm.SaveAll();
        Assert.True(result.Succeeded);
        Assert.Equal(["new-preview-program", "sample"], result.SavedProgramIds);
        Assert.Empty(result.Failures);
        Assert.False(vm.HasUnsavedChanges);
        Assert.Same(draft, vm.SelectedProgram);
        Assert.Equal("saved sample with preview warning", new PlanCompiler().Load(Path.Combine(_root, "sample.TapPlan")).Sidecar.DisplayName);
        Assert.True(File.Exists(Path.Combine(_root, "new-preview-program.program.json")));
        Assert.Contains("OpenTAP home setting", vm.SavePreviewWarning);
        Assert.Equal(vm.SavePreviewWarning, vm.Error);

        vm.OpenTapHomeOverride = string.Empty;
        var refreshed = vm.SaveAll();
        Assert.True(refreshed.Succeeded);
        Assert.Empty(refreshed.SavedProgramIds);
        Assert.Null(vm.SavePreviewWarning);
        Assert.Null(vm.Error);
        Assert.Same(draft, vm.SelectedProgram);
    }

    [Fact]
    public void Save_all_reports_persistence_failure_and_preview_warning_without_losing_successes()
    {
        var vm = Open(new RecordingCompiler { FailId = "failed-program" });
        vm.DisplayName = "success alongside failures";
        vm.InitializePlan(new("failed-program") { Instruments = [] });
        vm.DisplayName += " edited";
        var selected = vm.SelectedProgram;
        vm.OpenTapHomeOverride = "invalid\0home";
        var result = vm.SaveAll();
        Assert.False(result.Succeeded);
        Assert.Equal(["sample"], result.SavedProgramIds);
        Assert.Equal("failed-program", Assert.Single(result.Failures).PlanId);
        Assert.Equal("failed-program", Assert.Single(vm.DirtyPrograms).PlanId);
        Assert.Same(selected, vm.SelectedProgram);
        Assert.Equal("success alongside failures", new PlanCompiler().Load(Path.Combine(_root, "sample.TapPlan")).Sidecar.DisplayName);
        Assert.Contains("failed-program: save failed", vm.Error);
        Assert.Contains(vm.SavePreviewWarning!, vm.Error);
        Assert.Contains("OpenTAP home setting", vm.SavePreviewWarning);
    }

    [Fact]
    public void Sidecar_only_and_plan_flags_clear_independently()
    {
        var compiler = new RecordingCompiler();
        var vm = Open(compiler);
        vm.DisplayName = "edited";
        Assert.Equal(new DirtyProgramSummary("sample", false, true), Assert.Single(vm.DirtyPrograms));
        vm.SaveProgram("sample");
        Assert.Empty(compiler.PlanSaves);
        vm.SelectMeasure(0);
        vm.ChannelKey = "edited-channel";
        vm.SaveSidecar();
        Assert.Equal(new DirtyProgramSummary("sample", true, false), Assert.Single(vm.DirtyPrograms));
        vm.SaveAll();
        Assert.False(vm.HasUnsavedChanges);
        Assert.Equal(["sample"], compiler.PlanSaves);
    }

    [Fact]
    public void Partial_failure_keeps_failed_new_draft_and_saves_other_program()
    {
        var compiler = new RecordingCompiler { FailId = "new-program" };
        var vm = Open(compiler);
        vm.DisplayName = "saved other";
        vm.CreateDemoProgram("new-program");
        var selected = vm.SelectedProgram;
        var result = vm.SaveAll();
        Assert.False(result.Succeeded);
        Assert.True(result.HasUnsavedChanges);
        Assert.Equal(["sample"], result.SavedProgramIds);
        Assert.Equal("new-program", Assert.Single(result.Failures).PlanId);
        Assert.Equal(new DirtyProgramSummary("new-program", false, true), Assert.Single(vm.DirtyPrograms));
        Assert.Same(selected, vm.SelectedProgram);
        Assert.DoesNotContain(vm.Workspace!.TapPlanPaths, path => Path.GetFileName(path) == "new-program.TapPlan");
        Assert.Contains("new-program", vm.Error);
    }

    [Fact]
    public void Final_sidecar_failure_rolls_back_bytes_and_retains_dirty_draft()
    {
        var path = Path.Combine(_root, "sample.TapPlan");
        var sidecar = Path.Combine(_root, "sample.program.json");
        var beforePlan = File.ReadAllBytes(path);
        var beforeSidecar = File.ReadAllBytes(sidecar);
        var sawChangedPlan = false;
        var compiler = new PlanCompiler(null, (source, destination) =>
        {
            if (destination == sidecar)
            {
                sawChangedPlan = !beforePlan.SequenceEqual(File.ReadAllBytes(path));
                throw new IOException("replacement failure");
            }
            File.Move(source, destination, overwrite: true);
        });
        var vm = Open(compiler);
        vm.SelectMeasure(0);
        vm.ChannelKey = "edited-channel";
        var selected = vm.SelectedProgram;
        Assert.False(vm.SaveAll().Succeeded);
        Assert.True(sawChangedPlan);
        Assert.Equal(beforePlan, File.ReadAllBytes(path));
        Assert.Equal(beforeSidecar, File.ReadAllBytes(sidecar));
        Assert.Same(selected, vm.SelectedProgram);
        Assert.Equal(new DirtyProgramSummary("sample", true, false), Assert.Single(vm.DirtyPrograms));
    }

    [Fact]
    public void New_plan_final_replacement_failure_keeps_draft_and_no_known_or_permanent_path()
    {
        var path = Path.Combine(_root, "failed-new.TapPlan");
        var sidecar = Path.Combine(_root, "failed-new.program.json");
        var compiler = new PlanCompiler(null, (source, destination) =>
        {
            if (destination == sidecar)
            {
                Assert.True(File.Exists(path));
                throw new IOException("replacement failure");
            }
            File.Move(source, destination, overwrite: true);
        });
        var vm = Open(compiler);
        vm.InitializePlan(new("failed-new") { Instruments = [] });
        vm.DisplayName += " edited";
        var draft = vm.SelectedProgram;
        Assert.False(vm.SaveAll().Succeeded);
        Assert.Same(draft, vm.SelectedProgram);
        Assert.Equal(new DirtyProgramSummary("failed-new", false, true), Assert.Single(vm.DirtyPrograms));
        Assert.DoesNotContain(path, vm.Workspace!.TapPlanPaths);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(sidecar));
        Assert.True(File.Exists(new AuthoringDocumentStore(_root).GetDocumentPath("failed-new")));
    }

    [Fact]
    public void Sidecar_failure_preserves_bytes_and_flag()
    {
        var sidecar = Path.Combine(_root, "sample.program.json");
        var before = File.ReadAllBytes(sidecar);
        var compiler = new PlanCompiler(null, (_, _) => throw new IOException("replacement failure"));
        var vm = Open(compiler);
        vm.DisplayName = "keep edit";
        Assert.False(vm.SaveAll().Succeeded);
        Assert.Equal(before, File.ReadAllBytes(sidecar));
        Assert.Equal("keep edit", vm.DisplayName);
        Assert.Equal(new DirtyProgramSummary("sample", false, true), Assert.Single(vm.DirtyPrograms));
    }

    [Fact]
    public void Dirty_direct_open_and_unapproved_commit_refuse_replacement_and_failed_prepare_keeps_drafts()
    {
        var vm = Open();
        var prepared = vm.PrepareOpen(_root);
        vm.DisplayName = "keep edit";
        var selected = vm.SelectedProgram;
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Open(_root));
        Assert.Throws<AuthoringWorkspaceException>(() => vm.CommitOpen(prepared));
        Assert.Throws<AuthoringWorkspaceException>(() => vm.PrepareOpen(Path.Combine(_root, "missing")));
        Assert.Same(selected, vm.SelectedProgram);
        Assert.Equal("keep edit", vm.DisplayName);
        vm.CommitOpen(prepared, discardUnsavedChanges: true);
        Assert.False(vm.HasUnsavedChanges);
        Assert.NotEqual("keep edit", vm.DisplayName);
    }

    [Fact]
    public void Prospective_dataset_load_failure_does_not_replace_dirty_session()
    {
        var vm = Open();
        vm.DisplayName = "keep edited session";
        var selected = vm.SelectedProgram;
        var manifest = AuthoringWorkspaceLoader.Load(_root).Manifest;
        manifest.RecordingsDirectory = "../outside-workspace";
        AuthoringWorkspaceLoader.SaveManifest(_root, manifest);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.PrepareOpen(_root));
        Assert.Same(selected, vm.SelectedProgram);
        Assert.Equal("keep edited session", vm.DisplayName);
        Assert.True(vm.HasUnsavedChanges);
        Assert.Equal("recordings", vm.Workspace!.Manifest.RecordingsDirectory);
    }

    [Fact]
    public void Successful_replacement_clears_previous_findings_and_preflight()
    {
        var vm = Open();
        File.Delete(Path.Combine(_root, "sample.program.json"));
        vm.Validate();
        Assert.NotEmpty(vm.Findings);
        vm.DisplayName = "dirty";
        Assert.Throws<PackPreflightException>(() => vm.Pack(Path.Combine(_root, "output")));
        Assert.NotNull(vm.LastPackPreflight);
        vm.CommitOpen(vm.PrepareOpen(_root), discardUnsavedChanges: true);
        Assert.Empty(vm.Findings);
        Assert.Empty(vm.FindingRows);
        Assert.Null(vm.LastPackPreflight);
        Assert.Empty(vm.PackPreflightFindings);
    }

    [Fact]
    public void Read_only_workspace_rejects_edits_and_saves_without_dirtying_content()
    {
        var manifest = Path.Combine(_root, "authoring.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 999", StringComparison.Ordinal));
        var vm = Open();
        Assert.True(vm.Workspace!.IsReadOnly);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.DisplayName = "read only edit");
        Assert.Throws<AuthoringWorkspaceException>(vm.Apply);
        Assert.Throws<AuthoringWorkspaceException>(vm.SaveSidecar);
        Assert.True(vm.SaveAll().Succeeded);
        Assert.False(vm.HasUnsavedChanges);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.SaveProgram("unknown"));
    }

    private readonly List<AuthoringWorkspaceViewModel> _owned = [];

    private AuthoringWorkspaceViewModel Open(IPlanCompiler? compiler = null)
    {
        var vm = new AuthoringWorkspaceViewModel(compiler);
        _owned.Add(vm);
        vm.Open(_root);
        vm.SelectProgram("sample");
        return vm;
    }

    public void Dispose()
    {
        foreach (var vm in _owned) vm.StopRecovery();
        Task.WhenAll(_owned.Select(vm => vm.StopRecoveryAsync())).GetAwaiter().GetResult();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingCompiler : IPlanCompiler
    {
        private readonly PlanCompiler _inner = new();
        public string? FailId { get; init; }
        public List<string> PlanSaves { get; } = [];
        public List<string> SidecarSaves { get; } = [];
        public int LoadCount { get; private set; }
        public void Save(ProgramDraft draft, string path)
        {
            if (draft.PlanId == FailId) throw new IOException("save failed");
            _inner.Save(draft, path);
            PlanSaves.Add(draft.PlanId);
        }
        public ProgramDraft Load(string path) => _inner.Load(path);
        public DraftWorkspace LoadAll(AuthoringWorkspace workspace) { LoadCount++; return _inner.LoadAll(workspace); }
        public void SaveSidecar(string path, ProgramSidecar sidecar) { _inner.SaveSidecar(path, sidecar); SidecarSaves.Add(Path.GetFileNameWithoutExtension(path)); }
    }
}
