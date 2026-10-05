using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringDestructiveScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-scope-" + Guid.NewGuid().ToString("N"));
    public AuthoringDestructiveScopeTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        Directory.CreateDirectory(_root);
        File.Copy(Path.Combine(dir!.FullName, "plans", "opentap", "authoring.json"), ManifestPath);
    }
    private string ManifestPath => Path.Combine(_root, "authoring.json");
    private AuthoringWorkspaceViewModel Open(IPlanCompiler? compiler = null)
    {
        var vm = new AuthoringWorkspaceViewModel(compiler); vm.Open(_root); return vm;
    }
    private static void AddField(AuthoringWorkspaceViewModel vm)
    {
        vm.NewRequiredField = "fixtureId"; vm.AddRequiredField();
    }

    [Fact]
    public void Membership_clones_only_selected_sidecar_and_leaves_catalog_and_other_program_unchanged()
    {
        var vm = Open(); vm.CreateProgram("a"); vm.CreateProgram("b"); Assert.True(vm.SaveAll().Succeeded);
        var b = vm.SelectedProgram!; vm.SelectProgram("a"); var a = vm.SelectedProgram!;
        var manifest = File.ReadAllBytes(ManifestPath);
        vm.SetRequiredFieldIncluded("fixtureId", true); vm.SetReportKindIncluded("custom", true);
        Assert.NotSame(a.Sidecar, vm.SelectedProgram!.Sidecar);
        Assert.DoesNotContain("fixtureId", RequiredFieldIds.FromSidecar(a.Sidecar));
        Assert.DoesNotContain("custom", a.Sidecar.ReportKinds ?? []);
        Assert.Same(b, vm.Programs.Single(p => p.PlanId == "b"));
        Assert.Equal(new DirtyProgramSummary("a", false, true), Assert.Single(vm.DirtyPrograms));
        Assert.False(vm.WorkspaceCatalogDirty); Assert.Equal(manifest, File.ReadAllBytes(ManifestPath));
    }

    [Theory]
    [InlineData(CatalogDeletionKind.RequiredField)]
    [InlineData(CatalogDeletionKind.ReportKind)]
    [InlineData(CatalogDeletionKind.ProgramKind)]
    public void Preparing_named_impact_without_applying_changes_nothing_and_only_names_users(CatalogDeletionKind kind)
    {
        var vm = Open(); vm.CreateProgram("a");
        switch (kind)
        {
            case CatalogDeletionKind.RequiredField: AddField(vm); break;
            case CatalogDeletionKind.ReportKind: vm.NewReportKind = "custom"; vm.AddReportKind(); break;
            case CatalogDeletionKind.ProgramKind: vm.NewProgramKind = "custom"; vm.AddProgramKind(); break;
        }
        vm.CreateProgram("b");
        // Program creation may inherit catalog suggestions; explicitly remove membership for the unaffected program.
        switch (kind)
        {
            case CatalogDeletionKind.RequiredField: vm.SetRequiredFieldIncluded("fixtureId", false); break;
            case CatalogDeletionKind.ReportKind: vm.SetReportKindIncluded("custom", false); break;
            case CatalogDeletionKind.ProgramKind: vm.ProgramKind = "dut"; break;
        }
        Assert.True(vm.SaveAll().Succeeded);
        var before = Directory.EnumerateFiles(_root).ToDictionary(p => p, File.ReadAllBytes);
        var a = vm.Programs.Single(p => p.PlanId == "a"); var b = vm.SelectedProgram;
        var impact = vm.PrepareCatalogDeletion(kind, kind == CatalogDeletionKind.RequiredField ? "fixtureId" : "custom");
        Assert.Equal("a", Assert.Single(impact.AffectedPrograms).PlanId);
        Assert.Contains("workspace", impact.OperationName); Assert.Contains("Workspace", impact.Scope);
        Assert.NotEmpty(Assert.Single(impact.AffectedPrograms).Nodes);
        Assert.False(vm.HasUnsavedChanges); Assert.Same(a, vm.Programs.Single(p => p.PlanId == "a")); Assert.Same(b, vm.SelectedProgram);
        Assert.All(before, p => Assert.Equal(p.Value, File.ReadAllBytes(p.Key)));
        vm.ApplyCatalogDeletion(impact);
        Assert.Same(b, vm.SelectedProgram); Assert.Equal(new DirtyProgramSummary("a", false, true), Assert.Single(vm.DirtyPrograms));
        Assert.True(vm.WorkspaceCatalogDirty); Assert.All(before, p => Assert.Equal(p.Value, File.ReadAllBytes(p.Key)));
        Assert.True(vm.SaveAll().Succeeded);
        var reload = Open();
        var options = kind switch { CatalogDeletionKind.RequiredField => reload.RequiredFieldOptions, CatalogDeletionKind.ReportKind => reload.ReportKindOptions, _ => reload.ProgramKindOptions };
        Assert.DoesNotContain(impact.Target, options);
    }

    [Theory]
    [InlineData("program")]
    [InlineData("apply")]
    [InlineData("sidecar")]
    public void Selected_save_cannot_persist_catalog_and_global_only_state_blocks_pack_and_validation(string save)
    {
        var vm = Open(); vm.CreateProgram("a"); Assert.True(vm.SaveAll().Succeeded); var before = File.ReadAllBytes(ManifestPath);
        AddField(vm);
        switch (save) { case "program": vm.SaveProgram("a"); break; case "apply": vm.Apply(); break; default: vm.SaveSidecar(); break; }
        Assert.Empty(vm.DirtyProgramIds); Assert.True(vm.HasUnsavedChanges); Assert.True(vm.WorkspaceCatalogDirty);
        Assert.Equal(before, File.ReadAllBytes(ManifestPath)); Assert.Contains("Save All", vm.ValidationScope); Assert.Contains("Save All", vm.PackGuardText);
        Assert.Contains(vm.UnsavedChangesSummary, line => line.Contains("Workspace catalog"));
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        var blocked = Assert.Throws<PackPreflightException>(() => vm.Pack(Path.Combine(_root, "out")));
        Assert.True(blocked.Report.HasErrors); Assert.Same(blocked.Report, vm.LastPackPreflight);
        Assert.Contains(vm.PackPreflightFindings, f => f.Code == "PACK_CATALOG_DIRTY" && f.IsError);
        var result = vm.SaveAll(); Assert.True(result.Succeeded); Assert.True(result.WorkspaceCatalogSaved); Assert.False(vm.WorkspaceCatalogDirty);
        Assert.Contains("fixtureId", AuthoringWorkspaceLoader.Load(_root).Manifest.Catalogs!.RequiredFields);
    }

    [Fact]
    public void Saving_unaffected_selected_program_cannot_commit_workspace_deletion_or_affected_sidecar()
    {
        var vm = Open(); vm.CreateProgram("a"); AddField(vm); vm.CreateProgram("b"); vm.SetRequiredFieldIncluded("fixtureId", false);
        Assert.True(vm.SaveAll().Succeeded); var manifest = File.ReadAllBytes(ManifestPath);
        var sidecarPath = PlanCompiler.SidecarPath(Path.Combine(_root, "a.TapPlan")); var sidecar = File.ReadAllBytes(sidecarPath);
        vm.ApplyCatalogDeletion(vm.PrepareRequiredFieldDeletion("fixtureId")); vm.SaveProgram("b");
        Assert.Equal("b", vm.SelectedProgram!.PlanId); Assert.Equal(new DirtyProgramSummary("a", false, true), Assert.Single(vm.DirtyPrograms));
        Assert.True(vm.WorkspaceCatalogDirty); Assert.Equal(manifest, File.ReadAllBytes(ManifestPath)); Assert.Equal(sidecar, File.ReadAllBytes(sidecarPath));
        Assert.True(vm.SaveAll().Succeeded); Assert.DoesNotContain("fixtureId", File.ReadAllText(sidecarPath));
    }

    [Fact]
    public void Partial_program_failure_keeps_global_dirty_and_manifest_bytes_until_retry()
    {
        var compiler = new FailingCompiler(); var vm = Open(compiler); vm.CreateProgram("a"); vm.CreateProgram("b"); Assert.True(vm.SaveAll().Succeeded);
        vm.SelectProgram("a"); AddField(vm); vm.SelectProgram("b"); vm.SetRequiredFieldIncluded("fixtureId", true); Assert.True(vm.SaveAll().Succeeded);
        var before = File.ReadAllBytes(ManifestPath); var bPath = PlanCompiler.SidecarPath(Path.Combine(_root, "b.TapPlan")); var bBefore = File.ReadAllBytes(bPath);
        vm.ApplyCatalogDeletion(vm.PrepareRequiredFieldDeletion("fixtureId")); compiler.FailId = "b";
        var result = vm.SaveAll(); Assert.False(result.Succeeded); Assert.Equal(["a"], result.SavedProgramIds); Assert.Equal("b", Assert.Single(result.Failures).PlanId);
        Assert.True(vm.WorkspaceCatalogDirty); Assert.False(result.WorkspaceCatalogSaved); Assert.Equal(before, File.ReadAllBytes(ManifestPath)); Assert.Equal(bBefore, File.ReadAllBytes(bPath));
        Assert.DoesNotContain("fixtureId", File.ReadAllText(PlanCompiler.SidecarPath(Path.Combine(_root, "a.TapPlan"))));
        compiler.FailId = null; Assert.True(vm.SaveAll().Succeeded); Assert.False(vm.HasUnsavedChanges);
        Assert.DoesNotContain("fixtureId", AuthoringWorkspaceLoader.Load(_root).Manifest.Catalogs!.RequiredFields);
    }

    [Fact]
    public void Manifest_failure_retains_catalog_failure_and_global_dirty_even_after_all_program_saves()
    {
        var vm = Open(); vm.StopRecovery(); vm.CreateProgram("a"); Assert.True(vm.SaveAll().Succeeded);
        var manifestBefore = File.ReadAllBytes(ManifestPath);
        var workspaceSourcePath = new AuthoringDocumentStore(_root).GetWorkspacePath(); Assert.False(File.Exists(workspaceSourcePath));
        var sidecarPath = PlanCompiler.SidecarPath(Path.Combine(_root, "a.TapPlan")); AddField(vm);
        var reachedCatalogPublication = false;
        vm.WorkspaceManifestReplacement = (_, _) =>
        {
            Assert.Contains("fixtureId", File.ReadAllText(sidecarPath));
            reachedCatalogPublication = true; throw new IOException("manifest replacement failure");
        };
        var result = vm.SaveAll(); Assert.False(result.Succeeded); Assert.Empty(result.Failures); Assert.Empty(vm.DirtyProgramIds);
        Assert.True(reachedCatalogPublication); Assert.Equal(["a"], result.SavedProgramIds);
        Assert.Equal(manifestBefore, File.ReadAllBytes(ManifestPath)); Assert.False(File.Exists(workspaceSourcePath));
        Assert.True(result.HasUnsavedChanges); Assert.True(vm.WorkspaceCatalogDirty); Assert.Equal(result.WorkspaceCatalogFailure, vm.WorkspaceCatalogSaveFailure);
        Assert.Contains("retry Save All", vm.WorkspaceCatalogSaveFailure); Assert.Contains(vm.WorkspaceCatalogSaveFailure!, vm.Error);
        Assert.Contains(vm.SaveAllResults, s => s.StartsWith("Workspace catalog:"));
        vm.WorkspaceManifestReplacement = null;
        Assert.True(vm.SaveAll().Succeeded); Assert.Null(vm.WorkspaceCatalogSaveFailure); Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Catalog_final_manifest_replacement_failure_preserves_bytes_and_cleans_complete_temp_then_retries()
    {
        var vm = Open(); vm.CreateProgram("a"); Assert.True(vm.SaveAll().Succeeded);
        var before = File.ReadAllBytes(ManifestPath); AddField(vm); vm.SaveProgram("a");
        var sawCompleteTemp = false;
        vm.WorkspaceManifestReplacement = (source, destination) =>
        {
            Assert.Equal(ManifestPath, destination); Assert.Equal(Path.GetDirectoryName(destination), Path.GetDirectoryName(source));
            Assert.Contains("fixtureId", File.ReadAllText(source)); Assert.Equal(before, File.ReadAllBytes(destination));
            sawCompleteTemp = true; throw new IOException("final manifest replacement failure");
        };
        vm.OpenTapHomeOverride = "invalid\0home";
        var result = vm.SaveAll(); Assert.False(result.Succeeded); Assert.True(sawCompleteTemp);
        Assert.Empty(result.Failures); Assert.Empty(vm.DirtyProgramIds); Assert.True(result.HasUnsavedChanges); Assert.True(vm.WorkspaceCatalogDirty);
        Assert.Contains("final manifest replacement failure", vm.WorkspaceCatalogSaveFailure); Assert.Contains("OpenTAP home setting", vm.SavePreviewWarning);
        Assert.Contains(vm.WorkspaceCatalogSaveFailure!, vm.Error); Assert.Contains(vm.SavePreviewWarning!, vm.Error);
        Assert.Equal(before, File.ReadAllBytes(ManifestPath)); Assert.Empty(Directory.EnumerateFiles(_root, "*.saving"));
        vm.WorkspaceManifestReplacement = null;
        var retry = vm.SaveAll(); Assert.True(retry.Succeeded); Assert.True(retry.WorkspaceCatalogSaved); Assert.Null(vm.WorkspaceCatalogSaveFailure);
        Assert.False(vm.HasUnsavedChanges); Assert.NotNull(vm.SavePreviewWarning); Assert.Equal(vm.SavePreviewWarning, vm.Error);
        Assert.Contains("fixtureId", AuthoringWorkspaceLoader.Load(_root).Manifest.Catalogs!.RequiredFields);
    }

    [Fact]
    public void Global_only_dirtiness_resets_only_after_successful_session_replacement()
    {
        var vm = Open(); vm.CreateProgram("a"); Assert.True(vm.SaveAll().Succeeded); var prepared = vm.PrepareOpen(_root); AddField(vm); vm.SaveProgram("a");
        var session = vm.Workspace; var selected = vm.SelectedProgram;
        var openGuard = Assert.Throws<AuthoringWorkspaceException>(() => vm.Open(_root));
        Assert.Contains("workspace catalog changes", openGuard.Message); Assert.Contains("Save All", openGuard.Message);
        Assert.Same(session, vm.Workspace); Assert.Same(selected, vm.SelectedProgram);
        var commitGuard = Assert.Throws<AuthoringWorkspaceException>(() => vm.CommitOpen(prepared));
        Assert.Contains("workspace catalog changes", commitGuard.Message); Assert.Contains("Save All", commitGuard.Message);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.PrepareOpen(Path.Combine(_root, "missing"))); Assert.True(vm.WorkspaceCatalogDirty);
        vm.CommitOpen(prepared, discardUnsavedChanges: true); Assert.False(vm.WorkspaceCatalogDirty); Assert.False(vm.HasUnsavedChanges); Assert.Empty(vm.UnsavedChangesSummary);
    }

    [Fact]
    public void Default_and_existing_catalog_additions_do_not_mark_global_dirty()
    {
        var vm = Open(); vm.CreateProgram("a"); AddField(vm); Assert.True(vm.SaveAll().Succeeded);
        vm.NewRequiredField = "fixtureId"; vm.AddRequiredField(); vm.NewRequiredField = "serial"; vm.AddRequiredField();
        vm.NewReportKind = "status"; vm.AddReportKind(); vm.NewProgramKind = "dut"; vm.AddProgramKind();
        Assert.False(vm.WorkspaceCatalogDirty);
    }

    [Fact]
    public void Unreviewed_legacy_apis_reject_destructive_changes()
    {
        var vm = Open(); vm.CreateProgram("a"); AddField(vm); vm.NewReportKind = "custom"; vm.AddReportKind(); vm.NewProgramKind = "custom"; vm.AddProgramKind();
        Assert.Throws<AuthoringWorkspaceException>(() => vm.RemoveRequiredField("fixtureId")); Assert.Throws<AuthoringWorkspaceException>(() => vm.RemoveReportKind("custom"));
        Assert.Throws<AuthoringWorkspaceException>(() => vm.RemoveProgramKindFromCatalog("custom"));
        vm.NewInstrumentSlot = "OTHER"; vm.AddInstrumentSlot();
        Assert.Throws<AuthoringWorkspaceException>(vm.RemoveSelectedInstrumentSlot); Assert.Contains("OTHER", vm.InstrumentSlots);
    }

    [Theory]
    [InlineData("nested-setting")]
    [InlineData("null-sentinel")]
    [InlineData("raw")]
    [InlineData("sidecar")]
    [InlineData("catalog")]
    [InlineData("workspace")]
    public void Catalog_impact_rejects_changed_recursive_content_or_session_before_mutation(string change)
    {
        var vm = Open(); vm.CreateDemoProgram("a"); AddField(vm); vm.ApplyRecipe(AuthoringRecipeIds.Acquire); vm.ApplyRecipe(AuthoringRecipeIds.Repeat);
        var settings = new Dictionary<string, string> { ["samples"] = "2" };
        var nested = Assert.IsType<RepeatNode>(Assert.Single(vm.SelectedProgram!.Measure)); var metric = Assert.IsType<MetricNode>(Assert.Single(nested.Children));
        vm.ReplaceSelected(vm.SelectedProgram with
        {
            Setup = [new OperatorInputSetup("input", "title", "msg", null, null)],
            Measure = [nested with { Children = [metric with { Metric = metric.Metric with { Source = new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, settings) } }] }, new RawStepNode("Raw", "<a/>")],
        });
        var impact = vm.PrepareRequiredFieldDeletion("fixtureId");
        switch (change)
        {
            case "nested-setting": settings["samples"] = "3"; break;
            case "null-sentinel": vm.ReplaceSelected(vm.SelectedProgram with { Setup = [new OperatorInputSetup("input", "title", "msg", "<null>", null)] }); break;
            case "raw":
                var currentNested = vm.SelectedProgram.Measure[0];
                vm.ReplaceSelected(vm.SelectedProgram with
                {
                    Measure = vm.SelectedProgram.Measure.Select(node => node is RawStepNode raw ? raw with { XmlFragment = "<b/>" } : node).ToArray(),
                });
                Assert.Same(currentNested, vm.SelectedProgram.Measure[0]);
                Assert.Same(settings, Assert.IsType<MeasureSource>(Assert.IsType<MetricNode>(Assert.Single(Assert.IsType<RepeatNode>(vm.SelectedProgram.Measure[0]).Children)).Metric.Source).Settings);
                break;
            case "sidecar": vm.SelectedProgram.Sidecar.DutFamily = "changed"; break;
            case "catalog": vm.Workspace!.Manifest.Catalogs!.RequiredFields.Add("changed"); break;
            case "workspace": vm.CommitOpen(vm.PrepareOpen(_root), discardUnsavedChanges: true); break;
        }
        var before = vm.Programs; var manifest = File.ReadAllBytes(ManifestPath);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.ApplyCatalogDeletion(impact)); Assert.Same(before, vm.Programs); Assert.Equal(manifest, File.ReadAllBytes(ManifestPath));
        if (change != "workspace") Assert.Contains("fixtureId", RequiredFieldIds.FromSidecar(vm.SelectedProgram.Sidecar));
    }

    [Fact]
    public void Readonly_membership_and_prepare_operations_fail_in_Core_without_mutation()
    {
        File.WriteAllText(ManifestPath, File.ReadAllText(ManifestPath).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 999", StringComparison.Ordinal));
        var compiler = new FailingCompiler { InitialProgram = AuthoringRecipeCatalog.CreateProgram("a") }; var vm = Open(compiler); var original = vm.SelectedProgram;
        Assert.True(vm.Workspace!.IsReadOnly); Assert.Throws<AuthoringWorkspaceException>(() => vm.SetRequiredFieldIncluded("fixtureId", true));
        Assert.Throws<AuthoringWorkspaceException>(() => vm.SetReportKindIncluded("custom", true)); Assert.Throws<AuthoringWorkspaceException>(() => vm.PrepareRequiredFieldDeletion("fixtureId"));
        Assert.Throws<AuthoringWorkspaceException>(vm.PrepareSelectedInstrumentRemoval); Assert.Same(original, vm.SelectedProgram); Assert.False(vm.HasUnsavedChanges);
    }

    private static void AddSlots(AuthoringWorkspaceViewModel vm)
    {
        vm.NewInstrumentSlot = "B"; vm.AddInstrumentSlot(); vm.NewInstrumentSlot = "C"; vm.AddInstrumentSlot(); vm.SelectedInstrumentSlot = "DMM";
    }

    [Fact]
    public void Explicit_chosen_replacement_retargets_all_known_references_and_preserves_original_and_null_policies()
    {
        var vm = Open(); vm.CreateDemoProgram("a"); AddSlots(vm); vm.ApplyRecipe(AuthoringRecipeIds.Acquire); vm.ApplyRecipe(AuthoringRecipeIds.Repeat);
        var sidecar = PlanCompiler.CloneSidecar(vm.SelectedProgram!.Sidecar); sidecar.CleanupInstrumentSlots = ["DMM", "C"]; sidecar.IncludeMeasureSlots = true;
        vm.ReplaceSelected(vm.SelectedProgram with { Sidecar = sidecar, Cleanup = new CleanupPolicy(true, ["DMM", "C"], true) });
        var original = vm.SelectedProgram; var impact = vm.PrepareSelectedInstrumentRemoval(); Assert.Equal(["B", "C"], impact.CompatibleReplacementSlots);
        Assert.Contains(impact.AffectedNodes, n => n.Contains("Children")); Assert.Contains(impact.AffectedNodes, n => n.Contains("Sidecar"));
        Assert.Throws<AuthoringWorkspaceException>(() => vm.ApplyInstrumentRemoval(impact, "")); Assert.Throws<AuthoringWorkspaceException>(() => vm.ApplyInstrumentRemoval(impact, "DMM"));
        Assert.Same(original, vm.SelectedProgram); vm.ApplyInstrumentRemoval(impact, "C");
        var next = vm.SelectedProgram!; Assert.Equal(["C"], next.Cleanup.InstrumentSlots); Assert.Equal(["C"], next.Sidecar.CleanupInstrumentSlots!); Assert.True(next.Sidecar.IncludeMeasureSlots);
        Assert.Equal("C", Assert.Single(next.Setup.OfType<IdentitySetup>()).InstrumentSlot);
        Assert.All(AuthoringRecipeCatalog.EnumerateMetrics(next.Measure), m => Assert.Equal("C", Assert.IsType<MeasureSource>(m.Source).InstrumentSlot));
        Assert.Equal(["DMM", "C"], original!.Sidecar.CleanupInstrumentSlots!); Assert.Equal("DMM", Assert.Single(original.Setup.OfType<IdentitySetup>()).InstrumentSlot);
        Assert.Contains("B", next.Instruments.Select(i => i.SlotName)); Assert.Empty(Directory.EnumerateFiles(_root, "*.TapPlan"));
        next.Sidecar.CleanupInstrumentSlots = null; next.Sidecar.IncludeSafeShutdown = null; next.Sidecar.IncludeMeasureSlots = null;
        vm.SelectedInstrumentSlot = "C"; vm.ApplyInstrumentRemoval(vm.PrepareSelectedInstrumentRemoval(), "B");
        Assert.Null(vm.SelectedProgram.Sidecar.CleanupInstrumentSlots); Assert.Null(vm.SelectedProgram.Sidecar.IncludeSafeShutdown); Assert.Null(vm.SelectedProgram.Sidecar.IncludeMeasureSlots);
    }

    [Theory]
    [InlineData("unknown-target")]
    [InlineData("different-replacement")]
    [InlineData("unknown-replacement")]
    [InlineData("unknown-algorithm")]
    [InlineData("legacy-algorithm")]
    [InlineData("raw")]
    [InlineData("case-duplicate-last")]
    public void Instrument_removal_fails_closed_without_mutation_for_unprovable_usage_or_compatibility(string scenario)
    {
        var vm = Open(); vm.CreateDemoProgram("a"); vm.NewInstrumentSlot = "B"; vm.AddInstrumentSlot(); vm.SelectedInstrumentSlot = "DMM";
        var p = vm.SelectedProgram!; var known = p.Instruments[0].TypeId;
        p = scenario switch
        {
            "unknown-target" => p with { Instruments = [p.Instruments[0] with { TypeId = "Unknown" }, p.Instruments[1]] },
            "different-replacement" => p with { Instruments = [p.Instruments[0], p.Instruments[1] with { TypeId = "Other.Type" }] },
            "unknown-replacement" => p with { Instruments = [p.Instruments[0], p.Instruments[1] with { TypeId = "" }] },
            "case-duplicate-last" => p with { Instruments = [new InstrumentRef("DMM", known, "0"), new InstrumentRef("dmm", known, "1")] },
            "raw" => p with { Measure = [new RepeatNode(2, [new RawStepNode("Raw", "<step/>")])] },
            _ => p with
            {
                Measure = [new MetricNode(new MetricDraft("algo", "channel", "scalar", "V", new LimitSpec(0, 1, null), null,
                new AlgorithmSource(scenario == "legacy-algorithm" ? AuthoringFunctionIds.BasicMeanGte : "Unknown.Algorithm", [], new Dictionary<string, string>())))]
            },
        };
        vm.ReplaceSelected(p);
        if (scenario == "legacy-algorithm") vm.SelectedInstrumentSlot = "B"; // Binding is unresolved regardless of which slot was chosen.
        var original = vm.SelectedProgram; Assert.False(vm.CanRemoveSelectedInstrumentSlot); Assert.Throws<AuthoringWorkspaceException>(vm.PrepareSelectedInstrumentRemoval);
        Assert.Same(original, vm.SelectedProgram); Assert.Empty(Directory.EnumerateFiles(_root, "*.TapPlan"));
    }

    [Theory]
    [InlineData("selected-slot")]
    [InlineData("selected-program")]
    [InlineData("nested-setting")]
    public void Instrument_impact_rejects_changed_target_or_content(string change)
    {
        var vm = Open(); vm.CreateDemoProgram("b"); vm.CreateDemoProgram("a"); AddSlots(vm); vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var settings = new Dictionary<string, string> { ["samples"] = "2" }; var metric = Assert.IsType<MetricNode>(Assert.Single(vm.SelectedProgram!.Measure));
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = [new RepeatNode(2, [metric with { Metric = metric.Metric with { Source = new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, settings) } }])] });
        var impact = vm.PrepareSelectedInstrumentRemoval();
        switch (change) { case "selected-slot": vm.SelectedInstrumentSlot = "B"; break; case "selected-program": vm.SelectProgram("b"); break; default: settings["samples"] = "3"; break; }
        var before = vm.Programs; Assert.Throws<AuthoringWorkspaceException>(() => vm.ApplyInstrumentRemoval(impact, "C")); Assert.Same(before, vm.Programs);
        Assert.Contains("DMM", vm.Programs.Single(p => p.PlanId == "a").Instruments.Select(i => i.SlotName));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
    private sealed class FailingCompiler : IPlanCompiler
    {
        private readonly PlanCompiler _inner = new();
        public string? FailId { get; set; }
        public ProgramDraft? InitialProgram { get; init; }
        public DraftWorkspace LoadAll(AuthoringWorkspace workspace) => InitialProgram is { } program ? new(workspace, [program]) : _inner.LoadAll(workspace);
        public ProgramDraft Load(string path) => _inner.Load(path);
        public void Save(ProgramDraft draft, string path) { if (draft.PlanId == FailId) throw new IOException("failed plan"); _inner.Save(draft, path); }
        public void SaveSidecar(string path, ProgramSidecar sidecar) { if (Path.GetFileNameWithoutExtension(path) == FailId) throw new IOException("failed sidecar"); _inner.SaveSidecar(path, sidecar); }
    }
}
