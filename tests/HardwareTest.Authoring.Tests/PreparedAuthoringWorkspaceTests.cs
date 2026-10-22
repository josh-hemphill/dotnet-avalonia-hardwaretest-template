using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class PreparedAuthoringWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-prepared-source-" + Guid.NewGuid().ToString("N"));
    private readonly AuthoringWorkspaceViewModel _vm = new();
    private readonly AuthoringDocumentStore _store;

    public PreparedAuthoringWorkspaceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "plans"));
        File.WriteAllText(Path.Combine(_root, "authoring.json"), "{\"schemaVersion\":2,\"displayName\":\"Prepared source\"}");
        _vm.Open(_root);
        _vm.CreateDemoProgram("source");
        _store = new(_root);
        _store.Save(AuthoringDocumentDto.FromDraft(_vm.SelectedProgram!, revision: 7));
    }

    public void Dispose()
    {
        _vm.StopRecovery();
        Directory.Delete(_root, true);
    }

    [Fact]
    public void Changed_source_between_loading_and_metadata_preparation_fails_without_replacing_session()
    {
        var loaded = AuthoringSourceWorkspaceLoader.Load(_root);
        var originalSession = _vm.WorkspaceSessionId;
        var originalProgram = _vm.SelectedProgram;
        var changed = _store.Load("source").Document!;
        changed.Sidecar.DisplayName = "External newer source";
        changed.Revision = 8;
        _store.Save(changed);
        var bytes = File.ReadAllBytes(_store.GetDocumentPath("source"));
        var error = Assert.Throws<AuthoringWorkspaceException>(() =>
            new PreparedAuthoringWorkspace(loaded, WorkspacePackPlan.Describe(loaded.Files), []));
        Assert.Contains("changed while the workspace was loading", error.Message);
        Assert.Equal(originalSession, _vm.WorkspaceSessionId);
        Assert.Same(originalProgram, _vm.SelectedProgram);
        Assert.Equal(bytes, File.ReadAllBytes(_store.GetDocumentPath("source")));
    }

    [Fact]
    public void Prepared_revision_and_saved_baseline_share_the_same_source_value()
    {
        var prepared = _vm.PrepareOpen(_root);
        var session = Assert.Single(prepared.Documents);
        var source = Assert.Single(prepared.SourceState.Sources).Value;
        Assert.Equal(7, session.Revision);
        Assert.Equal(source.Revision, session.Revision);
        Assert.True(session.Snapshot.ContentEquals(AuthoringDocumentSnapshot.Capture(source.ToDraft())));
        Assert.Equal(session.Snapshot.PlanIdentity, session.SavedPlanIdentity);
        Assert.Equal(session.Snapshot.SidecarIdentity, session.SavedSidecarIdentity);
    }

    [Fact]
    public void Prepared_workspace_is_consumed_only_after_unsaved_guard_and_cannot_share_sessions_twice()
    {
        var prepared = _vm.PrepareOpen(_root);
        Assert.Throws<AuthoringWorkspaceException>(() => _vm.CommitOpen(prepared));
        _vm.CommitOpen(prepared, discardUnsavedChanges: true);
        var session = _vm.WorkspaceSessionId;
        var document = _vm.SelectedDocument;
        var error = Assert.Throws<AuthoringWorkspaceException>(() => _vm.CommitOpen(prepared));
        Assert.Contains("already been opened", error.Message);
        Assert.Equal(session, _vm.WorkspaceSessionId);
        Assert.Same(document, _vm.SelectedDocument);
    }
}
