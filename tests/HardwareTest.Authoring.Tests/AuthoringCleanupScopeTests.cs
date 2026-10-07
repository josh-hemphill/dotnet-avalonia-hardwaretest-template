using System.Text.Json;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringCleanupScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-cleanup-scope-" + Guid.NewGuid().ToString("N"));
    public AuthoringCleanupScopeTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        Directory.CreateDirectory(_root);
        File.Copy(Path.Combine(dir!.FullName, "plans", "opentap", "authoring.json"), Path.Combine(_root, "authoring.json"));
    }

    [Theory]
    [InlineData("slot")]
    [InlineData("measure")]
    [InlineData("shutdown")]
    [InlineData("membership")]
    public void Cleanup_edits_clone_selected_sidecar_preserve_prior_draft_and_other_program_and_stage_files(string operation)
    {
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root); vm.CreateDemoProgram("a"); vm.NewInstrumentSlot = "OTHER"; vm.AddInstrumentSlot(); vm.CreateDemoProgram("b"); Assert.True(vm.SaveAll().Succeeded);
        var other = vm.SelectedProgram; vm.SelectProgram("a"); SelectCleanup(vm); var prior = vm.SelectedProgram!;
        var priorSidecar = JsonSerializer.Serialize(prior.Sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar); var priorCleanup = prior.Cleanup;
        var bytes = Directory.EnumerateFiles(_root).ToDictionary(p => p, File.ReadAllBytes);
        Edit(vm, operation);
        var next = vm.SelectedProgram!; Assert.NotSame(prior.Sidecar, next.Sidecar); Assert.Same(priorCleanup, prior.Cleanup);
        Assert.Equal(priorSidecar, JsonSerializer.Serialize(prior.Sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar)); Assert.Same(other, vm.Programs.Single(p => p.PlanId == "b"));
        Assert.Equal(new DirtyProgramSummary("a", true, true), Assert.Single(vm.DirtyPrograms)); Assert.False(vm.WorkspaceCatalogDirty);
        foreach (var file in bytes) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        switch (operation)
        {
            case "slot": Assert.Equal("OTHER", next.Cleanup.InstrumentSlot); Assert.Equal(["OTHER"], next.Sidecar.CleanupInstrumentSlots!); break;
            case "measure": Assert.True(next.Cleanup.IncludeMeasureSlots); Assert.True(next.Sidecar.IncludeMeasureSlots); break;
            case "shutdown": Assert.False(next.Cleanup.IncludeSafeShutdown); Assert.Null(next.Sidecar.IncludeSafeShutdown); break;
            default: Assert.Contains("OTHER", next.Cleanup.InstrumentSlots); Assert.Contains("OTHER", next.Sidecar.CleanupInstrumentSlots!); break;
        }
        vm.SaveProgram("a"); Assert.False(vm.HasUnsavedChanges);
    }

    [Theory]
    [InlineData("slot")]
    [InlineData("measure")]
    [InlineData("shutdown")]
    [InlineData("membership")]
    public void All_cleanup_entrypoints_reject_readonly_workspaces_without_mutation_or_files(string operation)
    {
        var manifest = Path.Combine(_root, "authoring.json"); File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 999", StringComparison.Ordinal));
        var vm = new AuthoringWorkspaceViewModel(new ReadonlyCompiler()); vm.Open(_root); SelectCleanup(vm); var prior = vm.SelectedProgram!;
        var sidecar = JsonSerializer.Serialize(prior.Sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar); var before = File.ReadAllBytes(manifest);
        Assert.True(vm.Workspace!.IsReadOnly); Assert.Contains("read-only", Assert.Throws<AuthoringWorkspaceException>(() => Edit(vm, operation)).Message);
        Assert.Same(prior, vm.SelectedProgram); Assert.Equal(sidecar, JsonSerializer.Serialize(prior.Sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar)); Assert.False(vm.HasUnsavedChanges); Assert.Equal(before, File.ReadAllBytes(manifest));
    }

    private static void SelectCleanup(AuthoringWorkspaceViewModel vm) => vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Cleanup));
    private static void Edit(AuthoringWorkspaceViewModel vm, string operation)
    {
        switch (operation) { case "slot": vm.CleanupInstrumentSlot = "OTHER"; break; case "measure": vm.IncludeMeasureSlots = true; break; case "shutdown": vm.IncludeSafeShutdown = false; break; default: vm.SetCleanupSlotIncluded("OTHER", true); break; }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
    private sealed class ReadonlyCompiler : IPlanCompiler
    {
        public DraftWorkspace LoadAll(AuthoringWorkspace workspace) => new(workspace, [AuthoringRecipeCatalog.CreateProgram("a")]);
        public ProgramDraft Load(string path) => throw new NotSupportedException();
        public void Save(ProgramDraft draft, string path) => throw new NotSupportedException();
        public void SaveSidecar(string path, ProgramSidecar sidecar) => throw new NotSupportedException();
    }
}
