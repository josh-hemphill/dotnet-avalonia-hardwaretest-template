using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringDocumentSessionTests
{
    [Fact]
    public void SnapshotCopiesEveryMutableNestedCollectionAndRejectsUnknownVariants()
    {
        var settings = new Dictionary<string, string> { ["range"] = "1" };
        var inputs = new[] { "input" };
        var numerator = new[] { 1.0, 2.0 };
        var denominator = new[] { 1.0, 3.0 };
        var slots = new[] { "meter" };
        var sidecar = new ProgramSidecar { RequiredFields = ["serial"], ReportKinds = ["html"] };
        var draft = Draft() with
        {
            Sidecar = sidecar,
            Cleanup = new CleanupPolicy(true, slots),
            Measure = [new RepeatNode(2, [
                Metric("measurement", new MeasureSource("meter", "read", settings)),
                Metric("analysis", new AlgorithmSource("a", inputs, settings)),
                Metric("expression", new ExpressionAlgorithm(inputs, "input + 1")),
                Metric("transfer", new TransferFunctionAlgorithm("input", numerator, denominator, 1, "zoh"))])]
        };
        var snapshot = AuthoringDocumentSnapshot.Capture(draft);
        var identity = snapshot.PlanIdentity;
        settings["range"] = "9";
        inputs[0] = "changed";
        numerator[0] = 9;
        denominator[0] = 9;
        slots[0] = "changed";
        sidecar.RequiredFields![0] = "changed";
        sidecar.ReportKinds![0] = "changed";
        var restored = snapshot.Restore();
        Assert.Equal(identity, AuthoringDocumentSnapshot.Capture(restored).PlanIdentity);
        Assert.Equal("serial", restored.Sidecar.RequiredFields![0]);
        restored.Sidecar.RequiredFields[0] = "again";
        Assert.Equal("serial", snapshot.Restore().Sidecar.RequiredFields![0]);
        var children = Assert.IsType<RepeatNode>(restored.Measure[0]).Children;
        Assert.Equal("1", Assert.IsType<MeasureSource>(Assert.IsType<MetricNode>(children[0]).Metric.Source).Settings["range"]);
        Assert.Equal("input", Assert.IsType<ExpressionAlgorithm>(Assert.IsType<MetricNode>(children[2]).Metric.Source).InputChannelKeys[0]);
        Assert.Equal(1, Assert.IsType<TransferFunctionAlgorithm>(Assert.IsType<MetricNode>(children[3]).Metric.Source).Numerator[0]);
        Assert.Throws<NotSupportedException>(() => AuthoringDocumentSnapshot.Capture(draft with { Measure = [new UnknownNode()] }));
        Assert.Throws<NotSupportedException>(() => AuthoringDocumentSnapshot.Capture(draft with { Measure = [Metric("x", new UnknownSource())] }));
    }

    [Fact]
    public void SnapshotAndSessionPreserveCaseInsensitiveSettingsLookups()
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["elapsedms"] = "100" };
        var draft = Draft() with { Measure = [Metric("input", new MeasureSource("meter", "read", settings))] };
        var session = new AuthoringDocumentSession(draft);
        session.ApplyEdit("rename", d => Rename(d, "changed"));
        session.Undo();
        var source = Assert.IsType<MeasureSource>(Assert.IsType<MetricNode>(session.Draft.Measure[0]).Metric.Source);
        Assert.True(source.Settings.TryGetValue("ElapsedMs", out var value));
        Assert.Equal("100", value);
        settings["elapsedms"] = "200";
        Assert.Equal("100", source.Settings["ElapsedMs"]);
    }

    [Fact]
    public void DictionaryInsertionOrderDoesNotChangeContentIdentity()
    {
        var draft = Draft() with { Measure = [Metric("x", new MeasureSource("meter", "read", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }))] };
        var node = Assert.IsType<MetricNode>(draft.Measure[0]);
        var reordered = draft with { Measure = [node with { Metric = node.Metric with { Source = new MeasureSource("meter", "read", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }) } }] };
        Assert.True(AuthoringDocumentSnapshot.Capture(draft).ContentEquals(AuthoringDocumentSnapshot.Capture(reordered)));
    }

    [Fact]
    public void UndoRedoSelectionAndSavedContentAreIndependentPerProgram()
    {
        var draft = Draft();
        var session = new AuthoringDocumentSession(draft);
        var other = new AuthoringDocumentSession(Draft() with { PlanId = "other" });
        session.SelectedNodeId = draft.Measure[0].NodeId;
        Assert.True(session.ApplyEdit("rename", d => Rename(d, "changed")));
        Assert.True(session.IsPlanDirty);
        Assert.Equal(draft.Measure[0].NodeId, session.SelectedNodeId);
        Assert.True(session.Undo());
        Assert.False(session.IsDirty);
        Assert.False(session.ApplyEdit("no change", d => d));
        Assert.True(session.CanRedo);
        Assert.Throws<InvalidOperationException>(() => session.ApplyEdit("failed", _ => throw new InvalidOperationException()));
        Assert.True(session.CanRedo);
        Assert.True(session.Redo());
        session.MarkSaved();
        Assert.False(session.IsDirty);
        Assert.True(session.Undo());
        Assert.True(session.IsDirty);
        Assert.True(session.Redo());
        Assert.False(session.IsDirty);
        Assert.Equal(5, session.Revision);
        Assert.False(other.History.CanUndo);
        Assert.False(other.IsDirty);
    }

    [Fact]
    public void SidecarSaveAdvancesOnlyItsBaselineAndNewProgramRemainsUnsavedAfterUndo()
    {
        var session = new AuthoringDocumentSession(Draft(), isSaved: false);
        session.ApplyEdit("name", d => Rename(d, "new"));
        session.Undo();
        Assert.True(session.IsDirty);
        session.MarkSaved(plan: false);
        Assert.True(session.IsPlanDirty);
        Assert.False(session.IsSidecarDirty);
        session.MarkSaved();
        session.ApplyEdit("sidecar", d => { d.Sidecar.DisplayName = "new label"; return d; });
        Assert.False(session.IsPlanDirty);
        Assert.True(session.IsSidecarDirty);
        session.MarkSaved(plan: false);
        Assert.False(session.IsDirty);
        session.Undo();
        Assert.True(session.IsSidecarDirty);
    }

    [Fact]
    public void CleanupMirrorFieldsTrackExactSidecarContentAndSaveRebasesUndoRedoBoundary()
    {
        var session = new AuthoringDocumentSession(Draft());
        session.ApplyEdit("sidecar cleanup", d => { d.Sidecar.IncludeSafeShutdown = false; return d; });
        Assert.False(session.IsPlanDirty);
        Assert.True(session.IsSidecarDirty);
        var persisted = session.Draft;
        AuthoringCleanup.SyncSidecar(persisted.Sidecar, persisted.Cleanup);
        session.AcceptSavedContent(persisted);
        Assert.False(session.IsDirty);
        Assert.True(session.CanUndo);
        session.Undo();
        Assert.True(session.IsDirty);
        Assert.True(session.CanRedo);
        session.Redo();
        Assert.False(session.IsDirty);
        session.Undo();
        var persistedBeforeRedo = session.Draft;
        AuthoringCleanup.SyncSidecar(persistedBeforeRedo.Sidecar, persistedBeforeRedo.Cleanup);
        session.AcceptSavedContent(persistedBeforeRedo);
        Assert.True(session.CanRedo);
        session.Redo();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void ReadOnlyForeignStaleAndDeletedTargetsCannotModifyHistory()
    {
        var draft = Draft();
        var readOnly = new AuthoringDocumentSession(draft, isReadOnly: true);
        Assert.Throws<InvalidOperationException>(() => readOnly.ApplyEdit("x", d => Rename(d, "x")));
        var session = new AuthoringDocumentSession(draft);
        Assert.Throws<InvalidOperationException>(() => session.ApplyEdit("x", d => d, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => session.ApplyEdit("x", d => d with { PlanId = "foreign" }));
        Assert.False(session.History.CanUndo);
        session.ApplyEdit("x", d => Rename(d, "x"));
        session.RestoreExternal(Rename(session.Draft, "external"), null);
        Assert.False(session.CanUndo);
        Assert.Throws<InvalidOperationException>(() => session.Undo());
        session.ClearHistory();
        Assert.False(session.History.CanUndo);
        session.SelectedNodeId = draft.Measure[0].NodeId;
        session.ApplyEdit("delete", d => d with { Measure = [] }, draft.Measure[0].NodeId);
        Assert.Null(session.SelectedNodeId);
        session.Undo();
        Assert.Equal(draft.Measure[0].NodeId, session.SelectedNodeId);
    }

    [Fact]
    public void RetainedTargetFromOlderRevisionCannotCommitOrClearRedo()
    {
        var draft = Draft();
        var session = new AuthoringDocumentSession(draft);
        var preparedRevision = session.Revision;
        session.ApplyEdit("rename", d => Rename(d, "changed"));
        session.Undo();
        var current = session.Snapshot;
        Assert.Throws<InvalidOperationException>(() => session.ApplyEdit("stale rename", d => Rename(d, "stale"),
            draft.Measure[0].NodeId, preparedRevision));
        Assert.True(current.ContentEquals(session.Snapshot));
        Assert.True(session.CanRedo);
        Assert.Equal(2, session.Revision);
    }

    [Fact]
    public void NewEditAfterUndoClearsOnlyThisProgramsRedoAndInvalidIdsFailAtomically()
    {
        var session = new AuthoringDocumentSession(Draft());
        session.ApplyEdit("first", d => Rename(d, "first"));
        session.Undo();
        Assert.Throws<InvalidOperationException>(() => session.ApplyEdit("invalid", d => d with { Measure = [d.Measure[0], d.Measure[0]] }));
        Assert.True(session.CanRedo);
        Assert.Throws<InvalidOperationException>(() => session.ApplyEdit("empty identity", d => d with { Measure = [d.Measure[0] with { NodeId = Guid.Empty }] }));
        Assert.True(session.CanRedo);
        session.ApplyEdit("second", d => Rename(d, "second"));
        Assert.False(session.CanRedo);
        Assert.Equal("second", Assert.IsType<MetricNode>(session.Draft.Measure[0]).Metric.Name);
    }

    [Theory]
    [InlineData("setup")]
    [InlineData("metric")]
    [InlineData("limit")]
    [InlineData("history")]
    [InlineData("expression")]
    [InlineData("transfer")]
    [InlineData("cleanup")]
    [InlineData("instrument")]
    public void UndoRestoresEachNestedValueExactly(string category)
    {
        var draft = Draft();
        var session = new AuthoringDocumentSession(draft);
        var before = session.Snapshot;
        session.ApplyEdit(category, d =>
        {
            var node = Assert.IsType<MetricNode>(d.Measure[0]);
            return category switch
            {
                "setup" => d with { Setup = [Assert.IsType<IdentitySetup>(d.Setup[0]) with { InstrumentSlot = "other" }] },
                "metric" => Rename(d, "changed"),
                "limit" => d with { Measure = [node with { Metric = node.Metric with { Limits = new LimitSpec(9, 10, 11) } }] },
                "history" => d with { Measure = [node with { Metric = node.Metric with { History = new HistorySpec(false, 9, 10) } }] },
                "expression" => d with { Measure = [node with { Metric = node.Metric with { Source = new ExpressionAlgorithm(["input", "other"], "input + other") } }] },
                "transfer" => d with { Measure = [node with { Metric = node.Metric with { Source = new TransferFunctionAlgorithm("input", [1, 2], [1, 3], 0.1, "zoh") } }] },
                "cleanup" => d with { Cleanup = d.Cleanup with { IncludeMeasureSlots = true, InstrumentSlots = ["other"] } },
                "instrument" => d with { Instruments = [d.Instruments[0] with { VisaAddress = "changed" }] },
                _ => throw new InvalidOperationException()
            };
        });
        var after = session.Snapshot;
        Assert.False(before.ContentEquals(after));
        Assert.True(session.Undo());
        Assert.True(before.ContentEquals(session.Snapshot));
        Assert.True(session.Redo());
        Assert.True(after.ContentEquals(session.Snapshot));
    }

    [Fact]
    public void NestedDependencyFindingsKeepTargetsAndOpaqueUncertainty()
    {
        var missing = Metric("result", new ExpressionAlgorithm(["missing"], "missing + 1"));
        var raw = new RawStepNode("unknown", "<Step />");
        var draft = Draft() with { Measure = [new RepeatNode(2, [missing, raw])] };
        var index = AuthoringDependencyIndex.Build(draft);
        Assert.True(index.HasOpaqueReferences);
        Assert.Contains(AuthoringIssueService.GetIssues(draft), issue => issue.Code == "MISSING_CHANNEL" && issue.NodeId == missing.NodeId);
        Assert.Contains(AuthoringIssueService.GetIssues(draft), issue => issue.Code == "OPAQUE_REFERENCES" && issue.NodeId == raw.NodeId);
    }

    private static ProgramDraft Draft() => new("test", new ProgramSidecar(),
        [new InstrumentRef("meter", "type", "address")], [new IdentitySetup("meter")],
        [Metric("input", new MeasureSource("meter", "read", new Dictionary<string, string>()))], new CleanupPolicy(true, "meter"));
    private static MetricNode Metric(string channel, MetricSource source)
        => new(new MetricDraft(channel, channel, "scalar", "V", new LimitSpec(0, 1, null), new HistorySpec(true, 1, 2), source));
    private static ProgramDraft Rename(ProgramDraft draft, string name)
    {
        var node = Assert.IsType<MetricNode>(draft.Measure[0]);
        return draft with { Measure = [node with { Metric = node.Metric with { Name = name } }] };
    }
    private sealed record UnknownNode : MeasureNode;
    private sealed record UnknownSource : MetricSource;
}
