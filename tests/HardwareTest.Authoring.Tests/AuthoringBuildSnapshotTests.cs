using System.Text.Json;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringBuildSnapshotTests : IDisposable
{
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> OwnedFixtures = new();
    public void Dispose() => CleanupOwnedFixtures();
    internal static void CleanupOwnedFixtures()
    {
        while (OwnedFixtures.TryTake(out var directory))
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.TraceWarning($"Owned build test fixture cleanup failed: {directory}: {error.Message}"); }
        }
    }
    [Theory]
    [InlineData("manifest")]
    [InlineData("source")]
    [InlineData("dependency")]
    [InlineData("home")]
    public void Changed_saved_inputs_reject_publication_and_preserve_previous_output(string input)
    {
        var root = Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root);
        var home = Home(workspace);
        var source = new PlanCompiler().Load(Path.Combine(root, "sample.TapPlan"));
        new AuthoringDocumentStore(root).Save(AuthoringDocumentDto.FromDraft(source, revision: 7, compiledPlanHash: AuthoringDocumentStore.ComputeHash(Path.Combine(root, "sample.TapPlan")), compiledSidecarHash: AuthoringDocumentStore.ComputeHash(Path.Combine(root, "sample.program.json"))));
        var dependency = Path.Combine(root, "extra.TapPackage");
        File.WriteAllText(dependency, "captured plugin bytes");
        workspace.Manifest.PluginProjects.Add(dependency);
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home, Offline = true });
        var output = Temp();
        var old = Path.Combine(output, "previous.TapPackage");
        File.WriteAllText(old, "old");
        var target = input switch
        {
            "manifest" => Path.Combine(root, "authoring.json"),
            "source" => new AuthoringDocumentStore(root).GetDocumentPath("sample"),
            "dependency" => dependency,
            _ => Path.Combine(home.Root, "tap.runtimeconfig.json"),
        };
        File.AppendAllText(target, " ");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(request, output));
        Assert.Contains("BUILD_INPUT_CHANGED", error.Message);
        Assert.Equal("old", File.ReadAllText(old));
        Assert.False(File.Exists(Path.Combine(output, AuthoringBuildService.ReceiptFileName)));
    }

    [Fact]
    public void Saved_source_build_has_revision_exact_hashes_maps_and_no_original_writes_or_draft_aliases()
    {
        var root = Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root);
        var home = Home(workspace);
        var compiler = new PlanCompiler();
        var draft = compiler.Load(Path.Combine(root, "sample.TapPlan"));
        var store = new AuthoringDocumentStore(root);
        store.Save(AuthoringDocumentDto.FromDraft(draft, revision: 19, compiledPlanHash: AuthoringDocumentStore.ComputeHash(Path.Combine(root, "sample.TapPlan")), compiledSidecarHash: AuthoringDocumentStore.ComputeHash(Path.Combine(root, "sample.program.json"))));
        var before = File.ReadAllBytes(store.GetDocumentPath("sample"));
        var planBefore = File.ReadAllBytes(Path.Combine(root, "sample.TapPlan"));
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home, Offline = true });
        workspace.Manifest.Package.Version = "draft-mutated";
        draft.Sidecar.DisplayName = "draft-mutated";
        var output = Temp();
        var result = AuthoringBuildService.Execute(request, output);
        Assert.Equal("0.1.0", result.Manifest.Version);
        Assert.Equal(before, File.ReadAllBytes(store.GetDocumentPath("sample")));
        Assert.Equal(planBefore, File.ReadAllBytes(Path.Combine(root, "sample.TapPlan")));
        Assert.Contains(result.Receipt.Sources, s => s.PlanId == "sample" && s.SavedRevision == 19 && s.SourceSha256 is not null);
        Assert.Contains(result.Receipt.ExcludedPlans, id => id == "board-demo");
        Assert.DoesNotContain(result.Receipt.IncludedPlans, id => id == "board-demo");
        Assert.Contains(result.Receipt.Compilation.Single(c => c.PlanId == "sample").SourceMap, s => s.Kind == "authoring" && s.NodeId == s.StepId);
        Assert.Contains(result.Receipt.RequiredChecks, f => f.Code == "PACK_COMPAT_SCOPE");
        Assert.All(result.Receipt.Outputs, artifact => Assert.Equal(artifact.Sha256,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(output, artifact.Path))))));
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, AuthoringBuildService.ReceiptFileName)));
        Assert.Equal(1, receipt.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void Cancellation_and_partial_publication_restore_old_files()
    {
        var root = Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = Home(workspace), Offline = true });
        var output = Temp();
        File.WriteAllText(Path.Combine(output, "a"), "old-a");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => AuthoringBuildService.Execute(request, output, cancel.Token));
        var staged = Temp();
        File.WriteAllText(Path.Combine(staged, "a"), "new-a");
        File.WriteAllText(Path.Combine(staged, "b"), "new-b");
        var moves = 0;
        Assert.Throws<IOException>(() => AuthoringBuildService.Publish(staged, output, default, (from, to) =>
        {
            if (++moves == 2) throw new IOException("injected publication failure");
            File.Move(from, to, true);
        }));
        Assert.Equal("old-a", File.ReadAllText(Path.Combine(output, "a")));
        Assert.False(File.Exists(Path.Combine(output, "b")));
    }

    [Fact]
    public void Selected_home_links_cannot_escape_containment_or_change_target()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root);
        var home = Home(workspace);
        var first = Path.Combine(home.Root, "first.txt");
        var second = Path.Combine(home.Root, "second.txt");
        File.WriteAllText(first, "same");
        File.WriteAllText(second, "same");
        var link = Path.Combine(home.Root, "declared.txt");
        File.CreateSymbolicLink(link, first);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home, Offline = true });
        File.Delete(link);
        File.CreateSymbolicLink(link, second);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Recheck(request));
        File.Delete(link);
        File.CreateSymbolicLink(link, Path.Combine(root, "sample.TapPlan"));
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home }));
        Assert.Contains("BUILD_CONTAINMENT", error.Message);
    }

    [Fact]
    public void Nested_link_target_ancestors_cannot_escape_selected_home()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = Home(workspace);
        var external = Temp(); File.WriteAllText(Path.Combine(external, "actual.dll"), "external payload");
        var package = Path.Combine(home.Root, "Packages", "Nested"); Directory.CreateDirectory(package);
        Directory.CreateSymbolicLink(Path.Combine(package, "escape"), external);
        File.CreateSymbolicLink(Path.Combine(home.Root, "declared.dll"), Path.Combine(package, "escape", "actual.dll"));
        Assert.Contains("BUILD_CONTAINMENT", Assert.Throws<AuthoringWorkspaceException>(() =>
            AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home })).Message);
    }

    [Fact]
    public void Raw_link_parent_components_resolve_after_external_directory_targets()
    {
        if (OperatingSystem.IsWindows()) return;
        var workspace = AuthoringWorkspaceLoader.Load(Workspace());
        var home = Home(workspace);
        var outside = Temp();
        var external = Temp();
        var directoryTarget = Path.Combine(external, "deep", "child");
        Directory.CreateDirectory(directoryTarget);
        Directory.CreateSymbolicLink(Path.Combine(outside, "redirect"), directoryTarget);
        var homeName = Path.GetFileName(home.Root);
        var externalPayload = Path.Combine(external, homeName, "decoy.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(externalPayload)!);
        File.WriteAllText(externalPayload, "escaped bytes");
        File.WriteAllText(Path.Combine(home.Root, "decoy.dll"), "contained decoy");
        var link = Path.Combine(home.Root, "declared.dll");
        File.CreateSymbolicLink(link, Path.Combine(outside, "redirect", "..", "..", homeName, "decoy.dll"));
        Assert.Equal("escaped bytes", File.ReadAllText(link));
        Assert.Contains("BUILD_CONTAINMENT", Assert.Throws<AuthoringWorkspaceException>(() =>
            AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home })).Message);
    }

    [Fact]
    public void Incomplete_and_future_source_never_fall_back_to_existing_plan()
    {
        var root = Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root);
        var home = Home(workspace);
        var draft = new PlanCompiler().Load(Path.Combine(root, "sample.TapPlan"));
        draft.AuthoringState.IncompleteNumericText["test"] = "-";
        var store = new AuthoringDocumentStore(root);
        store.Save(AuthoringDocumentDto.FromDraft(draft));
        var output = Temp();
        Assert.Contains("BUILD_INCOMPLETE", Assert.Throws<AuthoringWorkspaceException>(() => WorkspacePacker.Pack(workspace, output, new PackOptions { Home = home })).Message);
        File.WriteAllText(store.GetDocumentPath("sample"), "{\"schemaVersion\":999}");
        Assert.Contains("BUILD_SOURCE", Assert.Throws<AuthoringWorkspaceException>(() => WorkspacePacker.Pack(workspace, output, new PackOptions { Home = home })).Message);
        Assert.Empty(Directory.GetFiles(output));
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("source")]
    [InlineData("dependency")]
    [InlineData("home")]
    public void Inputs_changed_after_preparation_are_rejected_at_the_publication_boundary(string input)
    {
        var root = Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = Home(workspace);
        var sourcePath = Path.Combine(root, "sample.TapPlan");
        var store = new AuthoringDocumentStore(root);
        store.Save(AuthoringDocumentDto.FromDraft(new PlanCompiler().Load(sourcePath), 31,
            compiledPlanHash: AuthoringDocumentStore.ComputeHash(sourcePath),
            compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(sourcePath))));
        var plugin = Path.Combine(root, "external.TapPackage"); File.WriteAllText(plugin, "captured package");
        workspace.Manifest.PluginProjects.Add(plugin); AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home, Offline = true });
        using var prepared = AuthoringBuildService.Prepare(request);
        var target = input switch
        {
            "manifest" => Path.Combine(root, "authoring.json"),
            "source" => store.GetDocumentPath("sample"),
            "dependency" => plugin,
            _ => Path.Combine(home.Root, "tap.runtimeconfig.json")
        };
        File.AppendAllText(target, " ");
        var output = Temp(); File.WriteAllText(Path.Combine(output, "ship-manifest.json"), "previous receipt");
        Assert.Contains("BUILD_INPUT_CHANGED", Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Publish(prepared, output)).Message);
        Assert.Equal("previous receipt", File.ReadAllText(Path.Combine(output, "ship-manifest.json")));
        Assert.Single(Directory.GetFiles(output));
    }

    [Fact]
    public void GUI_and_CLI_compile_the_same_saved_source_and_publish_receipts()
    {
        var root = Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = Home(workspace);
        var path = Path.Combine(root, "sample.TapPlan"); var store = new AuthoringDocumentStore(root);
        var source = AuthoringDocumentDto.FromDraft(new PlanCompiler().Load(path), 44,
            compiledPlanHash: AuthoringDocumentStore.ComputeHash(path), compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)));
        source.RequiresCompilation = true; store.Save(source);
        var savedBytes = File.ReadAllBytes(store.GetDocumentPath("sample"));
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        Assert.True(vm.HasUncompiledSources); Assert.True(vm.CanPack);
        var gui = Temp(); vm.Pack(gui, new PackOptions { Home = home, Offline = true }); vm.StopRecovery();
        var cli = Temp(); var error = new StringWriter(); var log = new StringWriter();
        Assert.Equal(0, AuthoringCli.Run(["--pack", root, "--out", cli, "--opentap-home", home.Root, "--offline"], log, error));
        using var guiReceipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(gui, AuthoringBuildService.ReceiptFileName)));
        using var cliReceipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(cli, AuthoringBuildService.ReceiptFileName)));
        Assert.Equal(guiReceipt.RootElement.GetProperty("includedPlans").ToString(), cliReceipt.RootElement.GetProperty("includedPlans").ToString());
        Assert.Equal(guiReceipt.RootElement.GetProperty("sources").ToString(), cliReceipt.RootElement.GetProperty("sources").ToString());
        Assert.Equal(savedBytes, File.ReadAllBytes(store.GetDocumentPath("sample")));
        Assert.Contains(home.Root, log.ToString()); Assert.Equal(home.Root, vm.LastPackPreflight!.Home!.Root);
    }

    [Fact]
    public void External_compiled_conflict_requires_explicit_reconciliation_before_saved_build()
    {
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.SelectProgram("sample"); vm.Apply();
        var id = vm.SelectedProgram!.PlanId; var path = vm.Workspace!.TapPlanPaths.Single(p => Path.GetFileNameWithoutExtension(p) == id);
        var home = Home(vm.Workspace); File.AppendAllText(path, "\n<!-- external compiled edit -->");
        var sourceBefore = File.ReadAllBytes(new AuthoringDocumentStore(root).GetDocumentPath(id));
        var output = Temp(); File.WriteAllText(Path.Combine(output, "ship-manifest.json"), "old");
        Assert.Contains("BUILD_SOURCE_CONFLICT", Assert.Throws<AuthoringWorkspaceException>(() => WorkspacePacker.Pack(vm.Workspace, output, new PackOptions { Home = home })).Message);
        Assert.Equal(sourceBefore, File.ReadAllBytes(new AuthoringDocumentStore(root).GetDocumentPath(id)));
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate()); vm.ReconcileCompiled(id, useCompiledContent: false);
        Assert.True(vm.CanPack); var reconciledSource = File.ReadAllBytes(new AuthoringDocumentStore(root).GetDocumentPath(id));
        var externalPlan = File.ReadAllBytes(path);
        vm.Pack(output, new PackOptions { Home = home }); vm.StopRecovery();
        Assert.Equal(reconciledSource, File.ReadAllBytes(new AuthoringDocumentStore(root).GetDocumentPath(id)));
        Assert.Equal(externalPlan, File.ReadAllBytes(path));
        Assert.True(File.Exists(Path.Combine(output, AuthoringBuildService.ReceiptFileName)));
    }

    [Fact]
    public void Cancellation_during_preflight_preserves_previous_artifacts()
    {
        var root = Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = Home(workspace);
        using var cancel = new CancellationTokenSource();
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions
        {
            Home = home,
            PreflightCompleted = _ => cancel.Cancel()
        });
        var output = Temp(); File.WriteAllText(Path.Combine(output, "old.TapPackage"), "old");
        Assert.Throws<OperationCanceledException>(() => AuthoringBuildService.Execute(request, output, cancel.Token));
        Assert.Single(Directory.GetFiles(output)); Assert.Equal("old", File.ReadAllText(Path.Combine(output, "old.TapPackage")));
    }

    [Theory]
    [InlineData("{invalid-json")]
    [InlineData("{\"schemaVersion\":999}")]
    [InlineData("unknown-field")]
    public void Unsupported_or_corrupt_source_shape_fails_explicitly_without_compiled_fallback(string content)
    {
        var root = Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = Home(workspace);
        var store = new AuthoringDocumentStore(root);
        if (content == "unknown-field")
        {
            var source = AuthoringDocumentDto.FromDraft(new PlanCompiler().Load(Path.Combine(root, "sample.TapPlan")));
            store.Save(source);
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(store.GetDocumentPath("sample")))!;
            json["unknownFutureField"] = true; content = json.ToJsonString();
        }
        else Directory.CreateDirectory(Path.GetDirectoryName(store.GetDocumentPath("sample"))!);
        File.WriteAllText(store.GetDocumentPath("sample"), content);
        var output = Temp(); File.WriteAllText(Path.Combine(output, "old.TapPackage"), "old");
        Assert.Contains("BUILD_SOURCE", Assert.Throws<AuthoringWorkspaceException>(() => WorkspacePacker.Pack(workspace, output, new PackOptions { Home = home })).Message);
        Assert.Equal(content, File.ReadAllText(store.GetDocumentPath("sample")));
        Assert.Single(Directory.GetFiles(output));
    }

    [Fact]
    public void Receipt_and_compile_maps_defensively_own_supplied_collection_data()
    {
        var entries = new List<AuthoringCompileMapEntry> { new(Guid.NewGuid(), null, "generated") };
        var compiled = new AuthoringCompileResult("program", "hash", entries); entries.Clear();
        Assert.Single(compiled.SourceMap);
        var included = new List<string> { "program" };
        var receipt = new AuthoringBuildReceipt(1, "id", DateTimeOffset.UtcNow, included, [], [], [], [], [], [compiled], []);
        included.Clear(); Assert.Single(receipt.IncludedPlans);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)receipt.IncludedPlans).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<AuthoringCompileMapEntry>)compiled.SourceMap).Clear());
    }

    [Fact]
    public void Build_result_and_receipt_copy_manifest_dependency_and_file_lists()
    {
        var dependencies = new List<ShipDependency> { new("OpenTAP", "^9.32.2") };
        var files = new List<string> { "Demo.TapPackage" };
        var manifest = new ShipManifest("Demo", "1.0.0", files, dependencies);
        var receipt = new AuthoringBuildReceipt(1, "id", DateTimeOffset.UtcNow, [], [], [], [], dependencies, [], [], []);
        var result = new AuthoringBuildResult(manifest, receipt);
        dependencies.Clear();
        files.Clear();

        Assert.Equal("OpenTAP", Assert.Single(result.Manifest.Dependencies).Package);
        Assert.Equal("OpenTAP", Assert.Single(receipt.Dependencies).Package);
        Assert.Equal("Demo.TapPackage", Assert.Single(result.Manifest.Files));
        Assert.Throws<NotSupportedException>(() => ((IList<ShipDependency>)result.Manifest.Dependencies).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ShipDependency>)receipt.Dependencies).Clear());
    }

    [Fact]
    public void Environment_change_after_capture_rejects_the_build()
    {
        var root = Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = Home(workspace) });
        var before = Environment.GetEnvironmentVariable("DOTNET_ROLL_FORWARD");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ROLL_FORWARD", before == "Major" ? "LatestMajor" : "Major");
            Assert.Contains("BUILD_ENVIRONMENT_CHANGED", Assert.Throws<AuthoringWorkspaceException>(() =>
                AuthoringBuildService.Execute(request, Temp())).Message);
        }
        finally { Environment.SetEnvironmentVariable("DOTNET_ROLL_FORWARD", before); }
    }

    [Fact]
    public void Prepared_output_or_receipt_mutation_cannot_publish()
    {
        var root = Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = Home(workspace) });
        using var prepared = AuthoringBuildService.Prepare(request);
        File.AppendAllText(Path.Combine(prepared.StagingRoot, "output", AuthoringBuildService.ReceiptFileName), " ");
        var output = Temp(); File.WriteAllText(Path.Combine(output, "old"), "old");
        Assert.Contains("BUILD_STAGED_CHANGED", Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Publish(prepared, output)).Message);
        Assert.Single(Directory.GetFiles(output));
    }

    [Fact]
    public void Compiler_failure_preserves_previous_outputs_and_saved_baselines()
    {
        var root = Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = Home(workspace);
        var path = Path.Combine(root, "sample.TapPlan"); var draft = new PlanCompiler().Load(path);
        var metric = AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure).First(m => m.Source is AlgorithmSource algorithm && algorithm.AlgorithmId == AuthoringFunctionIds.BasicMeanGte);
        var invalid = draft with { Measure = [new MetricNode(metric with { Limits = null })] };
        var store = new AuthoringDocumentStore(root);
        store.Save(AuthoringDocumentDto.FromDraft(invalid, 51, compiledPlanHash: AuthoringDocumentStore.ComputeHash(path),
            compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path))));
        var saved = File.ReadAllBytes(store.GetDocumentPath("sample")); var compiled = File.ReadAllBytes(path);
        var output = Temp(); File.WriteAllText(Path.Combine(output, "old.TapPackage"), "previous");
        Assert.Contains(AuthoringCompileCodes.MissingLimits, Assert.Throws<AuthoringWorkspaceException>(() => WorkspacePacker.Pack(workspace, output, new PackOptions { Home = home })).Message);
        Assert.Equal(saved, File.ReadAllBytes(store.GetDocumentPath("sample"))); Assert.Equal(compiled, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(output));
    }

    [Fact]
    public void Cancellation_between_output_moves_rolls_back_publication()
    {
        var staged = Temp(); var output = Temp();
        File.WriteAllText(Path.Combine(staged, "a"), "new"); File.WriteAllText(Path.Combine(staged, "b"), "new");
        File.WriteAllText(Path.Combine(output, "a"), "old");
        using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => AuthoringBuildService.Publish(staged, output, cancel.Token,
            (from, to) => { File.Move(from, to, true); cancel.Cancel(); }));
        Assert.Equal("old", File.ReadAllText(Path.Combine(output, "a"))); Assert.False(File.Exists(Path.Combine(output, "b")));
    }

    [Fact]
    public void Staged_plan_mutation_after_checks_during_packaging_cannot_publish_unchecked_bytes()
    {
        var root = Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        using var checker = new PackagingMutationChecker();
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = Home(workspace), Compat = checker });
        var output = Temp(); File.WriteAllText(Path.Combine(output, "old.TapPackage"), "old");
        Assert.Contains("BUILD_STAGED_CHANGED", Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(request, output)).Message);
        Assert.True(checker.Changed); Assert.Single(Directory.GetFiles(output));
    }

    private sealed class PackagingMutationChecker : ITuiCompatChecker, IDisposable
    {
        private FileSystemWatcher? _watcher;
        private int _changed;
        public bool Changed => _changed != 0;
        public TuiCompatReport Compare(AuthoringWorkspace workspace, OpenTapHome authoringHome, OpenTapHome tuiHome)
        {
            var plan = workspace.TapPlanPaths.Single(path => Path.GetFileNameWithoutExtension(path) == "sample");
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(plan)!, WorkspacePacker.PackageXmlFileName)
            { NotifyFilter = NotifyFilters.LastWrite };
            _watcher.Changed += (_, _) =>
            {
                if (Interlocked.Exchange(ref _changed, 1) == 0) File.AppendAllText(plan, "\n<!-- changed during package creation -->");
            };
            _watcher.EnableRaisingEvents = true;
            return new([], []); // Injected unit seam; this supplies no external TUI evidence.
        }
        public void Dispose() => _watcher?.Dispose();
    }

    internal static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "build-snapshot-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        OwnedFixtures.Add(path);
        return path;
    }
    internal static string Workspace()
    {
        var repository = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repository, "Directory.Build.props"))) repository = Path.GetDirectoryName(repository)!;
        var root = Temp();
        foreach (var file in Directory.GetFiles(Path.Combine(repository, "plans", "opentap"))) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        return root;
    }
    internal static OpenTapHome Home(AuthoringWorkspace workspace) => new OpenTapHomeBootstrapper().Bootstrap(workspace,
        new BootstrapOptions { HomeDirectory = Temp(), Offline = true });
}
