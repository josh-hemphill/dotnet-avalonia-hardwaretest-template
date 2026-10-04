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
    public void FutureAndCorruptCommittedBytesRemainVisibleAndUntouched(string content)
    {
        var store = new AuthoringDocumentStore(_root);
        var path = store.GetDocumentPath("plan");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        var bytes = File.ReadAllBytes(path);
        var loaded = store.Load("plan");
        Assert.True(loaded.IsReadOnly);
        Assert.NotNull(loaded.Error);
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
