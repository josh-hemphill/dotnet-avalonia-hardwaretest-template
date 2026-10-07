using System.Text.Json;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringDocumentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "authoring-source-" + Guid.NewGuid().ToString("N"));
    public AuthoringDocumentStoreTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void EveryVariantAndIncompleteAuthoringStateRoundTrips()
    {
        var expression = new MetricNode(new("formula", "formula", "value", "V", null, null, new ExpressionAlgorithm(["raw"], "unsupported(")));
        var draft = new ProgramDraft("plan", new ProgramSidecar { RequiredFields = ["serial"], ReportKinds = ["html"] },
            [new("meter", "type", "USB")],
            [new IdentitySetup("meter"), new OperatorPromptSetup("check", "hello"), new OperatorInputSetup("data", "title", "message", "string", "number")],
            [new RepeatNode(3, [
                new RawStepNode("plugin", "<TestStep><opaque /></TestStep>"),
                new MetricNode(new("raw", "raw", "value", "V", new(null, null, null), new(true, null, 2), new MeasureSource("meter", "read", new Dictionary<string,string>{{"Range", "10"}}))),
                new MetricNode(new("alg", "alg", "value", "V", null, null, new AlgorithmSource("algo", ["raw", "raw"], new Dictionary<string,string>{{"option", "x"}}))),
                expression,
                new MetricNode(new("tf", "tf", "value", "V", null, null, new TransferFunctionAlgorithm("raw", [1, 2], [1, 3], .001, "zoh")))])],
            new(true, ["meter"], true));
        draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(expression.NodeId, "threshold")] = "1e-";
        draft.AuthoringState.FormulaIntent[expression.NodeId] = FormulaDeploymentIntent.Explore;
        var store = new AuthoringDocumentStore(_root);
        var dto = AuthoringDocumentDto.FromDraft(draft, 42, compiledPlanHash: "planhash", compiledSidecarHash: "sidehash");
        store.Save(dto);
        var loaded = store.Load("plan");
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Equal(42, loaded.Document!.Revision);
        Assert.Equal("planhash", loaded.Document.CompiledPlanHash);
        Assert.Equal("sidehash", loaded.Document.CompiledSidecarHash);
        Assert.True(AuthoringDocumentSnapshot.Capture(draft).ContentEquals(AuthoringDocumentSnapshot.Capture(loaded.Document.ToDraft())));
        Assert.Equal("1e-", loaded.Document.State.IncompleteNumericText.Values.Single());
        Assert.Equal(FormulaDeploymentIntent.Explore, loaded.Document.State.FormulaIntent[expression.NodeId]);
        Assert.Equal(dto.SavedAtUtc, loaded.Document.SavedAtUtc);
        Assert.Contains("\"kind\": \"expression\"", File.ReadAllText(store.GetDocumentPath("plan")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("a/b")]
    [InlineData("CON")]
    [InlineData("workspace")]
    [InlineData("trailing.")]
    public void InvalidProgramIdsNeverCreateFiles(string id)
    {
        var store = new AuthoringDocumentStore(_root);
        Assert.Throws<ArgumentException>(() => store.Save(AuthoringDocumentDto.FromDraft(Draft(id))));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":999,\"unknown\": [1,2]}")]
    [InlineData("{\"schemaVersion\":1,\"planId\":\"plan\"}")]
    [InlineData("broken json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"scalar\"")]
    [InlineData("true")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    public void FutureAndCorruptCommittedBytesRemainVisibleAndUntouched(string content)
    {
        var store = new AuthoringDocumentStore(_root);
        var path = store.GetDocumentPath("plan");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        var bytes = File.ReadAllBytes(path);
        var loaded = store.Load("plan");
        Assert.True(loaded.IsReadOnly);
        if (content.Contains("999", StringComparison.Ordinal)) Assert.Null(loaded.Error);
        else Assert.NotNull(loaded.Error);
        Assert.Equal(bytes, loaded.OriginalBytes);
        Assert.Throws<InvalidOperationException>(() => store.Save(AuthoringDocumentDto.FromDraft(Draft("plan"))));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void InterruptedTempAndFailedPublishDoNotSupersedeCommittedSource()
    {
        var store = new AuthoringDocumentStore(_root);
        store.Save(AuthoringDocumentDto.FromDraft(Draft("plan"), 1));
        var path = store.GetDocumentPath("plan");
        var bytes = File.ReadAllBytes(path);
        File.WriteAllText(path + ".interrupted.tmp", "garbage");
        var failing = new AuthoringDocumentStore(_root, new AuthoringAtomicWriter((_, _) => throw new IOException("injected replacement failure")));
        var exception = Assert.Throws<IOException>(() => failing.Save(AuthoringDocumentDto.FromDraft(Draft("plan"), 2)));
        Assert.Contains("injected", exception.Message);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(bytes, File.ReadAllBytes(path + ".bak"));
        Assert.Equal(1, store.Load("plan").Document!.Revision);
        Assert.Single(store.ListDocumentIds());
    }

    [Fact]
    public void SymlinkedSourceDirectoryCannotRedirectWrites()
    {
        if (OperatingSystem.IsWindows()) return;
        var outside = Path.Combine(Path.GetTempPath(), "authoring-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "authoring-drafts"), outside);
            var store = new AuthoringDocumentStore(_root);
            Assert.Throws<IOException>(() => store.Save(AuthoringDocumentDto.FromDraft(Draft("plan"))));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally { Directory.Delete(outside); }
    }

    [Fact]
    public void BackupSymlinkIsRejectedBeforeMutation()
    {
        if (OperatingSystem.IsWindows()) return;
        var store = new AuthoringDocumentStore(_root);
        store.Save(AuthoringDocumentDto.FromDraft(Draft("plan"), 1));
        var path = store.GetDocumentPath("plan");
        var bytes = File.ReadAllBytes(path);
        File.CreateSymbolicLink(path + ".bak", Path.Combine(_root, "unrelated"));
        Assert.Throws<IOException>(() => store.Save(AuthoringDocumentDto.FromDraft(Draft("plan"), 2)));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void WorkspaceSourceRoundTripsAndFutureBytesBlockReplacement()
    {
        var store = new AuthoringDocumentStore(_root);
        var manifest = new AuthoringManifest { SchemaVersion = AuthoringSchemaVersions.Manifest, DisplayName = "test", Catalogs = new() { RequiredFields = ["serial"] } };
        store.SaveWorkspace(manifest, 4);
        var loaded = store.LoadWorkspace();
        Assert.False(loaded.IsReadOnly);
        Assert.Equal("serial", loaded.Document!.Manifest.Catalogs!.RequiredFields.Single());
        Assert.Equal(4, loaded.Document.Revision);
        var path = store.GetWorkspacePath();
        File.WriteAllText(path, "{\"schemaVersion\":999,\"extra\":true}");
        var bytes = File.ReadAllBytes(path);
        Assert.True(store.LoadWorkspace().IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => store.SaveWorkspace(manifest));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"scalar\"")]
    [InlineData("true")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    public void InvalidWorkspaceJsonShapeReportsRecoverableCorruptionWithoutReplacingBytes(string content)
    {
        var store = new AuthoringDocumentStore(_root);
        var path = store.GetWorkspacePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        var bytes = File.ReadAllBytes(path);
        var loaded = store.LoadWorkspace();
        Assert.True(loaded.Exists);
        Assert.True(loaded.IsReadOnly);
        Assert.Null(loaded.Document);
        Assert.NotNull(loaded.Error);
        Assert.Contains("Restore its backup", loaded.Error);
        Assert.Equal(bytes, loaded.OriginalBytes);
        Assert.Throws<InvalidOperationException>(() => store.SaveWorkspace(new AuthoringManifest { SchemaVersion = AuthoringSchemaVersions.Manifest }));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2147483648)]
    [InlineData(1)]
    public void UnsupportedNestedManifestVersionsAreRecoverableAndCannotBeSaved(int version)
    {
        var store = new AuthoringDocumentStore(_root);
        var path = store.GetWorkspacePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new AuthoringWorkspaceDto
        {
            Manifest = new AuthoringManifest { SchemaVersion = version, DisplayName = "invalid" }
        }, AuthoringDocumentJsonContext.Default.AuthoringWorkspaceDto));
        var bytes = File.ReadAllBytes(path);
        var loaded = store.LoadWorkspace();
        Assert.True(loaded.Exists);
        Assert.True(loaded.IsReadOnly);
        Assert.Null(loaded.Document);
        Assert.Contains("unsupported manifest schema", loaded.Error);
        Assert.Equal(bytes, loaded.OriginalBytes);
        Assert.Throws<InvalidOperationException>(() => store.SaveWorkspace(new AuthoringManifest { SchemaVersion = version }));
        Assert.Throws<InvalidOperationException>(() => store.SaveWorkspace(new AuthoringManifest { SchemaVersion = AuthoringSchemaVersions.Manifest }));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(3)]
    public void UnsupportedManifestSaveDoesNotCreateWorkspaceSource(int version)
    {
        var store = new AuthoringDocumentStore(_root);
        Assert.Throws<InvalidOperationException>(() => store.SaveWorkspace(new AuthoringManifest { SchemaVersion = version }));
        Assert.False(File.Exists(store.GetWorkspacePath()));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void NestedManifestVersionsKeepSupportedSourcesEditableAndFutureSourcesReadOnly(int version, bool readOnly)
    {
        var store = new AuthoringDocumentStore(_root);
        var path = store.GetWorkspacePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new AuthoringWorkspaceDto
        {
            Manifest = new AuthoringManifest { SchemaVersion = version, DisplayName = "test" }
        }, AuthoringDocumentJsonContext.Default.AuthoringWorkspaceDto));
        var bytes = File.ReadAllBytes(path);
        var loaded = store.LoadWorkspace();
        Assert.Equal(readOnly, loaded.IsReadOnly);
        Assert.Null(loaded.Error);
        Assert.Equal(version, loaded.Document!.Manifest.SchemaVersion);
        Assert.Equal(bytes, loaded.OriginalBytes);
        if (readOnly)
        {
            Assert.Throws<InvalidOperationException>(() => store.SaveWorkspace(new AuthoringManifest { SchemaVersion = AuthoringSchemaVersions.Manifest }));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        else
        {
            store.SaveWorkspace(new AuthoringManifest { SchemaVersion = version, DisplayName = "retained" });
            Assert.Equal(version, store.LoadWorkspace().Document!.Manifest.SchemaVersion);
        }
    }

    [Fact]
    public void Current_workspace_save_keeps_ordinary_backup_and_preserves_catalog_content()
    {
        var store = new AuthoringDocumentStore(_root);
        var manifest = new AuthoringManifest
        {
            SchemaVersion = AuthoringSchemaVersions.Manifest,
            DisplayName = "original",
            Catalogs = new() { RequiredFields = ["serial"] }
        };
        store.SaveWorkspace(manifest);
        var path = store.GetWorkspacePath();
        var original = File.ReadAllBytes(path);
        manifest.DisplayName = "updated";
        store.SaveWorkspace(manifest);
        Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
        var current = store.LoadWorkspace();
        Assert.False(current.IsReadOnly);
        Assert.Equal(AuthoringSchemaVersions.Manifest, current.Document!.Manifest.SchemaVersion);
        Assert.Equal("updated", current.Document.Manifest.DisplayName);
        Assert.Equal("serial", current.Document.Manifest.Catalogs!.RequiredFields.Single());
    }

    [Fact]
    public void RecoveryPathsEnumerateAndDeleteOnlyTheSelectedSafeDocument()
    {
        var store = new AuthoringDocumentStore(_root);
        store.SaveAtPath(store.GetRecoveryPath("plan"), AuthoringDocumentDto.FromDraft(Draft("plan"), 8));
        Assert.Equal("plan", store.ListRecoveryIds().Single());
        Assert.Equal(8, store.LoadAtPath(store.GetRecoveryPath("plan")).Document!.Revision);
        Assert.False(store.Load("plan").Exists);
        store.DeleteRecovery("plan");
        Assert.Empty(store.ListRecoveryIds());
        Assert.Throws<ArgumentException>(() => store.DeleteRecovery("../outside"));
    }

    [Fact]
    public void SettingsKeepCaseSensitiveDistinctKeysAndCaseInsensitiveLookups()
    {
        foreach (var comparer in new[] { StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase })
        {
            var settings = new Dictionary<string, string>(comparer) { ["Range"] = "10" };
            if (comparer == StringComparer.Ordinal) settings["range"] = "20";
            var draft = Draft("plan") with { Measure = [new MetricNode(new("m", "m", "value", "V", null, null, new MeasureSource("slot", "read", settings)))] };
            var dto = AuthoringDocumentDto.FromDraft(draft);
            var restored = (MeasureSource)((MetricNode)dto.ToDraft().Measure.Single()).Metric.Source;
            Assert.Equal(settings.Count, restored.Settings.Count);
            Assert.Equal(settings["range"], restored.Settings["range"]);
        }
    }

    private static ProgramDraft Draft(string id) => new(id, new(), [], [], [], new(false, Array.Empty<string>()));
}
