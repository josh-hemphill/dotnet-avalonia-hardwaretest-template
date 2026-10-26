using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class SectionPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "section-persistence-" + Guid.NewGuid().ToString("N"));
    public SectionPersistenceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private static ProgramDraft Draft(params MeasureNode[] nodes) => MockDmmDraftFixture.Create("sections") with { Measure = nodes };

    [Fact]
    public void SourceRoundTripPreservesEmptyNestedSectionsAndIdentity()
    {
        var draft = Draft(new SectionNode("Power system", [new RepeatNode(2, [new SectionNode("Empty subsystem", [])])]));
        var store = new AuthoringDocumentStore(_root);
        store.Save(AuthoringDocumentDto.FromDraft(draft, 7));
        var result = store.Load(draft.PlanId);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AuthoringDocumentDto.CurrentSchemaVersion, result.Document!.SchemaVersion);
        Assert.True(AuthoringDocumentSnapshot.Capture(draft).ContentEquals(AuthoringDocumentSnapshot.Capture(result.Document.ToDraft())));
        var renamed = draft with { Measure = [((SectionNode)draft.Measure[0]) with { Name = "Renamed" }] };
        Assert.False(AuthoringDocumentSnapshot.Capture(draft).ContentEquals(AuthoringDocumentSnapshot.Capture(renamed)));
    }

    [Fact]
    public void VersionOneSourceMigratesWithoutRewritingUntilSavedAndWorkspaceStaysVersionOne()
    {
        var draft = Draft();
        var document = AuthoringDocumentDto.FromDraft(draft, 4);
        document.SchemaVersion = 1;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, AuthoringDocumentJsonContext.Default.AuthoringDocumentDto);
        var store = new AuthoringDocumentStore(_root);
        var path = store.GetDocumentPath(draft.PlanId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        var loaded = store.Load(draft.PlanId);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Equal(AuthoringDocumentDto.CurrentSchemaVersion, loaded.Document!.SchemaVersion);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(1, new AuthoringWorkspaceDto { Manifest = new AuthoringManifest() }.SchemaVersion);
        store.Save(loaded.Document);
        using var saved = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal(AuthoringDocumentDto.CurrentSchemaVersion, saved.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void FutureSourceAndWorkspaceVersionsRemainReadOnly()
    {
        var store = new AuthoringDocumentStore(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(store.GetDocumentPath("sections"))!);
        var sourceBytes = System.Text.Encoding.UTF8.GetBytes($"{{\"schemaVersion\":{AuthoringDocumentDto.CurrentSchemaVersion + 1},\"future\":true}}");
        File.WriteAllBytes(store.GetDocumentPath("sections"), sourceBytes);
        Assert.True(store.Load("sections").IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => store.Save(AuthoringDocumentDto.FromDraft(Draft(), 1)));
        Assert.Equal(sourceBytes, File.ReadAllBytes(store.GetDocumentPath("sections")));
        var workspaceBytes = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"future\":true}");
        File.WriteAllBytes(store.GetWorkspacePath(), workspaceBytes);
        Assert.True(store.LoadWorkspace().IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => store.SaveWorkspace(new AuthoringManifest()));
        Assert.Equal(workspaceBytes, File.ReadAllBytes(store.GetWorkspacePath()));
    }

    [Fact]
    public void CompiledRoundTripPreservesNamedNestedAndEmptySectionsAndProtectedEnvelope()
    {
        var section = new SectionNode("Setup", [new SectionNode("Power", []), new RepeatNode(2, [new SectionNode("RF subsystem", [])])]);
        var draft = Draft(section);
        var path = Path.Combine(_root, "sections.TapPlan");
        var compiler = new PlanCompiler();
        compiler.Save(draft, path);
        var plan = TestPlan.Load(path);
        Assert.Equal("Setup", plan.ChildTestSteps[0].Name);
        Assert.Equal("Cleanup", plan.ChildTestSteps[^1].Name);
        var imported = compiler.Load(path);
        var restored = Assert.IsType<SectionNode>(Assert.Single(imported.Measure));
        Assert.Equal(section.NodeId, restored.NodeId);
        Assert.Equal("Setup", restored.Name);
        Assert.Empty(Assert.IsType<SectionNode>(restored.Children[0]).Children);
        Assert.Equal("RF subsystem", Assert.IsType<SectionNode>(Assert.Single(Assert.IsType<RepeatNode>(restored.Children[1]).Children)).Name);
        Assert.Equal(draft.Setup.Count, imported.Setup.Count);
    }

    [Fact]
    public void CrossSectionFormulaCompilesButRepeatBoundaryRemainsIsolated()
    {
        var producer = Raw(new PublishTimedSampleStep { Channel = "input", Value = 2, ElapsedMs = 0 });
        var consumer = new MetricNode(new("Mean", "result", "scalar", "V", new(null, null, 0), null,
            new AlgorithmSource(AuthoringFunctionIds.BasicChannelAverage, ["input"], new Dictionary<string, string>())));
        var draft = Draft(new SectionNode("Acquire", [producer]), new SectionNode("Check", [consumer]));
        var path = Path.Combine(_root, "sections.TapPlan");
        new PlanCompiler().Save(draft, path);
        var average = Assert.Single(PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<ChannelAverageStep>());
        Assert.Equal(producer.NodeId, average.ProducerStepId);
        var isolated = draft with { Measure = [draft.Measure[0], new RepeatNode(2, [draft.Measure[1]])] };
        Assert.Contains("MISSING_CHANNEL", Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(isolated, path)).Message);
    }

    [Fact]
    public void DisabledSectionAndImportedNestedGroupRemainOpaque()
    {
        var disabled = new TestGroupStep { Name = "Disabled section", AuthoringSection = true, Enabled = false };
        disabled.ChildTestSteps.Add(new PublishTimedSampleStep { Channel = "hidden", Value = 1, ElapsedMs = 0 });
        var opaque = new TestGroupStep { Name = "Imported wrapper" };
        opaque.ChildTestSteps.Add(new PublishTimedSampleStep { Channel = "raw", Value = 1, ElapsedMs = 0 });
        var draft = Draft(new SectionNode("System", [Raw(disabled), Raw(opaque), new RepeatNode(2, [Raw(new TestGroupStep { Name = "Loop wrapper" })])]));
        var path = Path.Combine(_root, "sections.TapPlan");
        var compiler = new PlanCompiler();
        compiler.Save(draft, path);
        var section = Assert.IsType<SectionNode>(Assert.Single(compiler.Load(path).Measure));
        Assert.IsType<RawStepNode>(section.Children[0]);
        Assert.IsType<RawStepNode>(section.Children[1]);
        Assert.IsType<RawStepNode>(Assert.Single(Assert.IsType<RepeatNode>(section.Children[2]).Children));
    }

    private RawStepNode Raw(ITestStep step)
    {
        AuthoringPluginSearch.Search();
        var plan = new TestPlan(); plan.ChildTestSteps.Add(step);
        var path = Path.Combine(_root, "raw.TapPlan"); plan.Save(path);
        var xml = XDocument.Load(path).Descendants().First(e => e.Name.LocalName == "TestStep");
        return new RawStepNode(step.GetType().FullName!, xml.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
    }
}
