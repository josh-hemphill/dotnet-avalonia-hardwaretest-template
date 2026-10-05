using System.IO.Compression;
using System.Text.Json;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringEnvironmentBuildTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData("formula")]
    [InlineData("incomplete")]
    public void Inclusion_toggle_survives_save_history_reopen_and_matches_actual_CLI_and_GUI_saved_build(string blocker)
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root);
        workspace.Manifest.Package.Name = "Scoped Build";
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var draft = AuthoringRecipeCatalog.CreateProgram("blocked") with
        {
            Measure = [new MetricNode(new MetricDraft("Unsupported", "result", "scalar", "V", new LimitSpec(null, null, 0), null,
                new ExpressionAlgorithm([], "std(input)")))]
        };
        if (blocker == "incomplete")
        {
            draft = new PlanCompiler().Load(Path.Combine(root, "sample.TapPlan")) with { PlanId = "blocked" };
            var node = draft.Measure.OfType<MetricNode>().FirstOrDefault() ?? draft.Measure.OfType<RepeatNode>().SelectMany(r => r.Children).OfType<MetricNode>().First();
            draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(node.NodeId, "Threshold")] = "abc";
        }
        new AuthoringDocumentStore(root).Save(AuthoringDocumentDto.FromDraft(draft, 11));
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        Assert.False(vm.CanPack);
        vm.SetBuildProgramIncluded("blocked", false);
        Assert.True(vm.WorkspaceCatalogDirty);
        vm.UndoWorkspace(); Assert.DoesNotContain("blocked", vm.Workspace!.Manifest.ExcludedProgramIds);
        vm.RedoWorkspace(); Assert.Contains("blocked", vm.Workspace!.Manifest.ExcludedProgramIds);
        Assert.True(vm.SaveAll().Succeeded);
        vm.Open(root);
        Assert.True(vm.CanPack);
        Assert.Contains("blocked", vm.ExcludedPackPlans);
        var home = AuthoringBuildSnapshotTests.Home(vm.Workspace!); vm.OpenTapHomeOverride = home.Root;
        var output = AuthoringBuildSnapshotTests.Temp();
        vm.Pack(output);
        Assert.Contains("sample", vm.LastBuildReceipt!.IncludedPlans);
        Assert.Contains("blocked", vm.LastBuildReceipt.ExcludedPlans);
        using (var archive = ZipFile.OpenRead(Directory.GetFiles(output, "*.TapPackage").Single()))
            Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains("blocked", StringComparison.OrdinalIgnoreCase));
        var cliOutput = AuthoringBuildSnapshotTests.Temp();
        var stdout = new StringWriter(); var stderr = new StringWriter();
        Assert.Equal(0, AuthoringCli.Run(["--pack", root, "--offline", "--opentap-home", home.Root, "--out", cliOutput], stdout, stderr));
        var receipt = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(cliOutput, AuthoringBuildService.ReceiptFileName)), AuthoringBuildJsonContext.Default.AuthoringBuildReceipt)!;
        Assert.Equal(vm.LastBuildReceipt.IncludedPlans, receipt.IncludedPlans);
        Assert.Equal(vm.LastBuildReceipt.ExcludedPlans, receipt.ExcludedPlans);
        vm.SetBuildProgramIncluded("blocked", true);
        Assert.Contains("Repair incomplete", vm.PackGuardText);
        Assert.True(vm.SaveAll().Succeeded); vm.Open(root);
        Assert.False(vm.CanPack);
        var saved = AuthoringBuildService.CaptureSaved(vm.Workspace!, new PackOptions { Home = home, Offline = true });
        Assert.ThrowsAny<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(saved, AuthoringBuildSnapshotTests.Temp()));
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("future")]
    [InlineData("unknown")]
    public void Actual_saved_build_selects_IDs_before_loading_excluded_source(string kind)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        var store = new AuthoringDocumentStore(root); var path = store.GetDocumentPath("excluded"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, kind == "corrupt" ? "{" : kind == "future" ? "{\"schemaVersion\":999,\"planId\":\"excluded\"}" : "{\"schemaVersion\":1,\"planId\":\"excluded\",\"unknown\":true}");
        var home = AuthoringBuildSnapshotTests.Home(workspace);
        var result = AuthoringBuildService.Execute(AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home, Offline = true }), AuthoringBuildSnapshotTests.Temp());
        Assert.Contains("excluded", result.Receipt.ExcludedPlans); Assert.DoesNotContain(result.Receipt.Sources, s => s.PlanId == "excluded");
        workspace.Manifest.Package.Name = "Everything"; AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home }), AuthoringBuildSnapshotTests.Temp()));
    }

    [Fact]
    public void Real_ranges_versions_and_declared_payload_share_environment_and_preflight_state()
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        workspace.Manifest.Dependencies.Add(new() { Package = "Offline Fixture", Version = "^1.2.0" });
        var directory = Path.Combine(home.Root, "Packages", "Offline Fixture"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "package.xml"), "<Package Name=\"Offline Fixture\" Version=\"1.3.0\"><Files><File Path=\"payload.txt\"/></Files></Package>");
        File.WriteAllText(Path.Combine(directory, "payload.txt"), "real payload");
        Assert.True(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home).Single(p => p.Package == "Offline Fixture").Satisfied);
        Assert.DoesNotContain(WorkspacePacker.Preflight(workspace, new PackOptions { Home = home }).Findings, f => f.Code == "PACK_PACKAGE_MISSING");
        File.Delete(Path.Combine(directory, "payload.txt"));
        Assert.Contains("payload missing", AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home).Single(p => p.Package == "Offline Fixture").State);
        Assert.Contains(WorkspacePacker.Preflight(workspace, new PackOptions { Home = home }).Findings, f => f.Code == "PACK_PACKAGE_MISSING");
        File.WriteAllText(Path.Combine(directory, "payload.txt"), "restored");
        File.WriteAllText(Path.Combine(directory, "package.xml"), "<Package Name=\"Offline Fixture\" Version=\"2.0.0\"/>");
        Assert.False(AuthoringEnvironmentAssessment.Packages(workspace.Manifest, home).Single(p => p.Package == "Offline Fixture").Satisfied);
    }

    [Fact]
    public void Injected_checker_receipt_cannot_mark_production_compatibility_pass()
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        var result = AuthoringBuildService.Execute(AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Compat = new EmptyChecker() }), AuthoringBuildSnapshotTests.Temp());
        Assert.DoesNotContain(result.Receipt.RequiredChecks, f => f.Code == "BUILD_COMPATIBILITY_PASS");
        Assert.Contains(result.Receipt.RequiredChecks, f => f.Code == "BUILD_COMPATIBILITY_INJECTED");
    }
    private sealed class EmptyChecker : ITuiCompatChecker
    {
        public TuiCompatReport Compare(AuthoringWorkspace workspace, OpenTapHome authoringHome, OpenTapHome tuiHome) => new([], []);
    }
}
