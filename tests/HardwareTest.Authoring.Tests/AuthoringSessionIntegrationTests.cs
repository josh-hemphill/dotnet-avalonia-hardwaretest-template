using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringSessionIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-session-" + Guid.NewGuid().ToString("N"));

    public AuthoringSessionIntegrationTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        Directory.CreateDirectory(_root);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, "plans", "opentap")))
            File.Copy(file, Path.Combine(_root, Path.GetFileName(file)));
    }

    private AuthoringWorkspaceViewModel Open(RecordingCompiler? compiler = null)
    {
        var vm = new AuthoringWorkspaceViewModel(compiler ?? new RecordingCompiler());
        vm.Open(_root);
        vm.SelectProgram("sample");
        return vm;
    }

    [Fact]
    public void Recipe_history_restores_final_added_and_wrapped_node_selection()
    {
        var vm = Open();
        vm.CreateDemoProgram("selection");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var first = vm.SelectedSequence!.NodeId;
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var added = vm.SelectedSequence!.NodeId;
        Assert.NotEqual(first, added);
        vm.Undo();
        Assert.Equal(first, vm.SelectedSequence!.NodeId);
        vm.Redo();
        Assert.Equal(added, vm.SelectedSequence!.NodeId);
        vm.ApplyRecipe(AuthoringRecipeIds.Repeat);
        var wrapped = vm.SelectedSequence!.NodeId;
        vm.Undo();
        Assert.Equal(added, vm.SelectedSequence!.NodeId);
        vm.Redo();
        Assert.Equal(wrapped, vm.SelectedSequence!.NodeId);
    }

    [Fact]
    public void Saved_content_comparison_and_independent_history_restore_stable_selection()
    {
        var vm = Open();
        vm.SelectMeasure(0);
        var selectedNode = vm.SelectedSequence!.NodeId;
        var original = vm.DisplayName;
        vm.DisplayName = "renamed";
        Assert.True(vm.CanUndo);
        Assert.True(vm.HasUnsavedChanges);
        vm.CreateDemoProgram("other");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var otherNode = vm.SelectedSequence!.NodeId;
        vm.DisplayName = "other edited";
        vm.SelectProgram("sample");
        Assert.Equal(selectedNode, vm.SelectedSequence!.NodeId);
        vm.Undo();
        Assert.Equal(original, vm.DisplayName);
        Assert.DoesNotContain(vm.DirtyPrograms, p => p.PlanId == "sample");
        vm.SelectProgram("other");
        Assert.Equal(otherNode, vm.SelectedSequence!.NodeId);
        Assert.Equal("other edited", vm.DisplayName);
        vm.Undo();
        Assert.Equal("other", vm.DisplayName);
        Assert.True(vm.HasUnsavedChanges); // creation is unsaved even when all edits are undone
        vm.SelectProgram("sample");
        vm.Redo();
        Assert.Equal("renamed", vm.DisplayName);
    }

    [Fact]
    public void Save_retains_history_and_failed_save_preserves_last_successful_baseline()
    {
        var compiler = new RecordingCompiler();
        var vm = Open(compiler);
        vm.DisplayName = "saved name";
        vm.SaveSidecar();
        Assert.False(vm.HasUnsavedChanges);
        Assert.True(vm.CanUndo);
        vm.Undo();
        Assert.True(vm.HasUnsavedChanges);
        compiler.FailSave = true;
        Assert.Throws<IOException>(() => vm.SaveSidecar());
        Assert.True(vm.HasUnsavedChanges);
        vm.Redo();
        Assert.Equal("saved name", vm.DisplayName);
        Assert.False(vm.HasUnsavedChanges);
        vm.SelectMeasure(0);
        vm.YUnit = "changed unit";
        compiler.FailSave = false;
        vm.SaveSidecar();
        Assert.True(Assert.Single(vm.DirtyPrograms).PlanDirty);
        vm.Apply();
        Assert.False(vm.HasUnsavedChanges);
        vm.Undo();
        Assert.True(vm.HasUnsavedChanges);
        vm.Redo();
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Catalog_history_restores_manifest_and_program_as_one_transaction_and_guards_staleness()
    {
        var vm = Open();
        vm.NewRequiredField = "fixtureId";
        vm.AddRequiredField();
        Assert.True(vm.CanUndoWorkspace);
        Assert.False(vm.CanUndo);
        Assert.Contains("fixtureId", vm.RequiredFieldOptions);
        Assert.Contains("fixtureId", RequiredFieldIds.FromSidecar(vm.SelectedProgram!.Sidecar));
        vm.UndoWorkspace();
        Assert.DoesNotContain("fixtureId", vm.RequiredFieldOptions);
        Assert.DoesNotContain("fixtureId", RequiredFieldIds.FromSidecar(vm.SelectedProgram!.Sidecar));
        Assert.False(vm.HasUnsavedChanges);
        vm.RedoWorkspace();
        Assert.True(vm.WorkspaceCatalogDirty);
        vm.DisplayName = "intervening edit";
        Assert.False(vm.CanUndoWorkspace);
        Assert.Throws<InvalidOperationException>(() => vm.UndoWorkspace());
        Assert.Equal("intervening edit", vm.DisplayName);
        vm.Undo();
        Assert.True(vm.CanUndoWorkspace);
        vm.UndoWorkspace();
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Catalog_save_baseline_and_program_deletion_do_not_resurrect_files()
    {
        var vm = Open();
        vm.NewReportKind = "custom";
        vm.AddReportKind();
        Assert.True(vm.SaveAll().Succeeded);
        Assert.False(vm.HasUnsavedChanges);
        vm.UndoWorkspace();
        Assert.True(vm.WorkspaceCatalogDirty);
        vm.RedoWorkspace();
        Assert.False(vm.HasUnsavedChanges);
        vm.RemoveSelectedProgram();
        Assert.False(vm.CanUndoWorkspace);
        Assert.DoesNotContain(vm.Programs, p => p.PlanId == "sample");
        Assert.False(File.Exists(Path.Combine(_root, "sample.TapPlan")));
    }

    [Fact]
    public void Read_only_workspace_rejects_field_and_sequence_edits()
    {
        var vm = Open(new RecordingCompiler { ReadOnly = true });
        var before = vm.DisplayName;
        Assert.Throws<AuthoringWorkspaceException>(() => vm.DisplayName = "edit");
        Assert.Throws<AuthoringWorkspaceException>(() => vm.ApplyRecipe(AuthoringRecipeIds.Acquire));
        Assert.Throws<AuthoringWorkspaceException>(() => vm.CreateProgram("new"));
        Assert.Equal(before, vm.DisplayName);
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanUndoWorkspace);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Exposed_mutable_draft_cannot_be_overwritten_by_history_and_sidecar_save_accepts_actual_content()
    {
        var vm = Open();
        vm.DisplayName = "tracked edit";
        vm.SelectedProgram!.Sidecar.DisplayName = "external edit";
        Assert.False(vm.CanUndo);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Undo());
        Assert.Equal("external edit", vm.DisplayName);
        vm.SaveSidecar();
        Assert.False(vm.HasUnsavedChanges);
        Assert.Equal("external edit", new PlanCompiler().Load(Path.Combine(_root, "sample.TapPlan")).Sidecar.DisplayName);
        vm.SelectedProgram!.Sidecar.DisplayName = "later external edit";
        Assert.False(vm.CanUndo);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Undo());
        Assert.Equal("later external edit", vm.DisplayName);
    }

    [Fact]
    public void Exposed_content_is_compared_to_saved_baselines_by_operation_guards()
    {
        var vm = Open();
        vm.SelectedProgram!.Sidecar.DisplayName = "external edit";
        Assert.True(vm.HasUnsavedChanges);
        Assert.True(Assert.Single(vm.DirtyPrograms).SidecarDirty);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Open(_root));
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        Assert.False(vm.CanPack);
        Assert.Throws<PackPreflightException>(() => vm.Pack(Path.Combine(_root, "output")));
        Assert.True(vm.SaveAll().Succeeded);
        Assert.False(vm.HasUnsavedChanges);
    }

    private sealed class RecordingCompiler : IPlanCompiler
    {
        private readonly PlanCompiler _inner = new();
        public bool FailSave { get; set; }
        public bool ReadOnly { get; set; }
        public ProgramDraft Load(string path) => _inner.Load(path);
        public DraftWorkspace LoadAll(AuthoringWorkspace workspace)
            => _inner.LoadAll(workspace with { IsReadOnly = ReadOnly });
        public void Save(ProgramDraft draft, string path)
        {
            if (FailSave) throw new IOException("Save failed");
            _inner.Save(draft, path);
        }
        public void SaveSidecar(string path, ProgramSidecar sidecar)
        {
            if (FailSave) throw new IOException("Save failed");
            _inner.SaveSidecar(path, sidecar);
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
