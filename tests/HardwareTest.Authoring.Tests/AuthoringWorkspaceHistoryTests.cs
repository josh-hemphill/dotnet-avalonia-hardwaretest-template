using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringWorkspaceHistoryTests
{
    [Fact]
    public void Undo_and_redo_restore_manifest_and_all_sidecars_atomically()
    {
        var history = new AuthoringWorkspaceHistory();
        var before = State("old", "old-a", "old-b");
        var after = State("new", "new-a", "new-b");
        Assert.True(history.Commit("Change catalog", before, after));
        var restored = history.Undo(after);
        Assert.Equal("old", restored.Manifest.DisplayName);
        Assert.Equal(["old-a", "old-b"], restored.Drafts.Select(draft => draft.Sidecar.DisplayName));
        Assert.True(before.ContentEquals(restored));
        Assert.True(after.ContentEquals(history.Redo(restored)));
    }

    [Fact]
    public void Noop_preserves_redo_and_new_branch_replaces_it()
    {
        var history = new AuthoringWorkspaceHistory();
        var before = State("old", "a", "b");
        var after = State("new", "a", "b");
        history.Commit("Change", before, after);
        var restored = history.Undo(after);
        Assert.False(history.Commit("Noop", restored, State("old", "a", "b")));
        Assert.True(history.CanRedo(restored));
        var branch = State("branch", "a", "b");
        Assert.True(history.Commit("Branch", restored, branch));
        Assert.False(history.CanRedo(branch));
        Assert.True(before.ContentEquals(history.Undo(branch)));
    }

    [Fact]
    public void Stale_identity_blocks_history_without_moving_cursor()
    {
        var history = new AuthoringWorkspaceHistory();
        var before = State("old", "a", "b");
        var after = State("new", "a", "b");
        history.Commit("Change", before, after);
        var stale = State("new", "a", "external");
        Assert.False(history.CanUndo(stale));
        Assert.Contains("stale", Assert.Throws<InvalidOperationException>(() => history.Undo(stale)).Message);
        Assert.Contains("clear", Assert.Throws<InvalidOperationException>(() => history.Commit("External", stale, before)).Message);
        Assert.True(history.CanUndo(after));
        history.Undo(after);
        Assert.Throws<InvalidOperationException>(() => history.Redo(stale));
        Assert.True(history.CanRedo(before));
        history.Clear();
        Assert.False(history.CanRedo(before));
        Assert.True(history.Commit("Fresh", stale, before));
    }

    [Fact]
    public void Captured_state_and_restored_properties_are_isolated_from_mutation()
    {
        var manifest = new AuthoringManifest { Catalogs = new() { ReportKinds = ["original"] } };
        var draft = Draft("a", "original");
        draft.Sidecar.ReportKinds = ["original"];
        var state = AuthoringWorkspaceState.Capture(manifest, [draft]);
        manifest.Catalogs.ReportKinds[0] = "changed";
        draft.Sidecar.ReportKinds[0] = "changed";
        state.Manifest.Catalogs!.ReportKinds[0] = "restored mutation";
        state.Drafts[0].Sidecar.ReportKinds![0] = "restored mutation";
        Assert.Equal("original", state.Manifest.Catalogs!.ReportKinds[0]);
        Assert.Equal("original", state.Drafts[0].Sidecar.ReportKinds![0]);
    }

    [Fact]
    public void Program_id_and_sequence_id_changes_are_part_of_workspace_identity()
    {
        var draft = Draft("a", "a") with { Measure = [new RawStepNode("type", "<step />")] };
        var before = AuthoringWorkspaceState.Capture(new(), [draft]);
        Assert.False(before.ContentEquals(AuthoringWorkspaceState.Capture(new(), [draft with { PlanId = "renamed" }])));
        Assert.False(before.ContentEquals(AuthoringWorkspaceState.Capture(new(),
            [draft with { Measure = [new RawStepNode("type", "<step />")] }])));
    }

    private static AuthoringWorkspaceState State(string title, string first, string second)
        => AuthoringWorkspaceState.Capture(new() { DisplayName = title }, [Draft("a", first), Draft("b", second)]);

    private static ProgramDraft Draft(string id, string title)
        => new(id, new ProgramSidecar { DisplayName = title }, [], [], [], new CleanupPolicy(false, Array.Empty<string>()));
}
