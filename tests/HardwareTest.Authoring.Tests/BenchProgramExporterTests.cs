using System.Text.Json;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class BenchProgramExporterTests : IDisposable
{
    private readonly string _root = AuthoringBuildSnapshotTests.Workspace();
    private readonly string _destination = AuthoringBuildSnapshotTests.Temp();
    private const string PlanId = "joined-review-voltage";

    [Fact]
    public void Prepared_pair_is_discovered_validated_and_preserves_saved_authoring_bytes()
    {
        var workspace = SavedProgram();
        var original = File.ReadAllBytes(PlanPath);
        var sidecar = File.ReadAllBytes(PlanCompiler.SidecarPath(PlanPath));
        var result = BenchProgramExporter.Export(workspace, PlanId, _destination);
        Assert.Equal(result.Receipt, BenchProgramHandoff.ValidateProgram(result.PlanPath));
        var entry = Assert.Single(ProgramCatalog.Enumerate([_destination]), candidate => candidate.Id == PlanId);
        Assert.Equal(result.PlanPath, entry.Path);
        Assert.Equal(original, File.ReadAllBytes(result.PlanPath));
        Assert.Equal(sidecar, File.ReadAllBytes(PlanCompiler.SidecarPath(result.PlanPath)));
        Assert.Equal(original, File.ReadAllBytes(PlanPath));
        Assert.Equal(sidecar, File.ReadAllBytes(PlanCompiler.SidecarPath(PlanPath)));
        Assert.Equal(7, result.Receipt.SourceRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mixed_or_changed_pair_cannot_satisfy_its_receipt(bool sidecar)
    {
        var result = BenchProgramExporter.Export(SavedProgram(), PlanId, _destination);
        var target = sidecar ? PlanCompiler.SidecarPath(result.PlanPath) : result.PlanPath;
        File.AppendAllText(target, " ");
        var bytes = File.ReadAllBytes(target);
        Assert.Throws<InvalidOperationException>(() => BenchProgramHandoff.ValidateProgram(result.PlanPath));
        Assert.Equal(bytes, File.ReadAllBytes(target));
    }

    [Fact]
    public void Replacement_requires_an_intact_owned_bundle_and_publishes_new_exact_receipt()
    {
        var workspace = SavedProgram();
        var first = BenchProgramExporter.Export(workspace, PlanId, _destination);
        workspace = SavedProgram("Changed saved title");
        var second = BenchProgramExporter.Export(workspace, PlanId, _destination);
        Assert.Equal(first.Directory, second.Directory);
        Assert.NotEqual(first.Receipt.ExportId, second.Receipt.ExportId);
        Assert.NotEqual(first.Receipt.SidecarSha256, second.Receipt.SidecarSha256);
        Assert.Equal(second.Receipt, BenchProgramHandoff.ValidateProgram(second.PlanPath));
        Assert.DoesNotContain(Directory.EnumerateDirectories(_destination), path => Path.GetFileName(path).StartsWith('.'));
    }

    [Fact]
    public void Unrelated_destination_and_changed_owner_preserve_previous_bytes()
    {
        var workspace = SavedProgram();
        var first = BenchProgramExporter.Export(workspace, PlanId, _destination);
        var receiptPath = Path.Combine(first.Directory, BenchProgramHandoff.ReceiptFileName);
        var receipt = File.ReadAllBytes(receiptPath);
        Assert.Throws<AuthoringWorkspaceException>(() => BenchProgramExporter.Export(workspace, PlanId, _destination, () => false));
        var ownerChecks = 0;
        Assert.Throws<AuthoringWorkspaceException>(() => BenchProgramExporter.Export(workspace, PlanId, _destination, () => ++ownerChecks < 2));
        Assert.Equal(2, ownerChecks);
        File.WriteAllText(Path.Combine(first.Directory, "unrelated.txt"), "Keep this file.");
        Assert.Throws<AuthoringWorkspaceException>(() => BenchProgramExporter.Export(workspace, PlanId, _destination));
        Assert.Equal(receipt, File.ReadAllBytes(receiptPath));
        Assert.Equal("Keep this file.", File.ReadAllText(Path.Combine(first.Directory, "unrelated.txt")));
    }

    [Fact]
    public void Future_receipt_and_uncompiled_saved_source_are_refused_without_replacing_good_data()
    {
        var workspace = SavedProgram();
        var first = BenchProgramExporter.Export(workspace, PlanId, _destination);
        var path = Path.Combine(first.Directory, BenchProgramHandoff.ReceiptFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(first.Receipt with { SchemaVersion = 2 }, BenchHandoffJsonContext.Default.BenchProgramReceipt));
        var bytes = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => BenchProgramHandoff.ValidateProgram(first.PlanPath));
        Assert.Throws<InvalidOperationException>(() => BenchProgramExporter.Export(workspace, PlanId, _destination));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        var store = new AuthoringDocumentStore(_root);
        var source = store.Load(PlanId).Document!;
        source.RequiresCompilation = true;
        store.Save(source);
        Assert.Throws<AuthoringWorkspaceException>(() => BenchProgramExporter.Export(workspace, PlanId, _destination));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("sample")]
    [InlineData("board-demo")]
    [InlineData("../outside")]
    public void Factory_or_escaping_ids_are_refused_before_any_publication(string id)
    {
        Assert.Throws<InvalidOperationException>(() => BenchProgramExporter.Export(SavedProgram(), id, _destination));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_destination));
    }

    private string PlanPath => Path.Combine(_root, PlanId + ".TapPlan");

    private AuthoringWorkspace SavedProgram(string title = "Joined voltage review")
    {
        var draft = AuthoringRecipeCatalog.Apply(MockDmmDraftFixture.Create(PlanId), AuthoringRecipeIds.Acquire);
        draft.Sidecar.DisplayName = title;
        new PlanCompiler().Save(draft, PlanPath);
        new AuthoringDocumentStore(_root).Save(AuthoringDocumentDto.FromDraft(draft, revision: 7,
            compiledPlanHash: AuthoringDocumentStore.ComputeHash(PlanPath),
            compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(PlanPath))));
        return AuthoringWorkspaceLoader.Load(_root);
    }

    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();
}
