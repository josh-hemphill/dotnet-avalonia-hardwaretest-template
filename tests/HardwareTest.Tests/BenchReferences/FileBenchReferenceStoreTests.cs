using System.Text.Json;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.BenchReferences;

public sealed class FileBenchReferenceStoreTests
{
    private static BenchReferenceProfile Profile(string name = "Golden board")
        => BenchReferenceEvaluator.Capture(name, BenchReferenceEvaluatorTests.Source(),
            [BenchReferenceEvaluatorTests.Rule()], BenchReferenceEvaluatorTests.Now);

    [Fact]
    public async Task Saved_profile_survives_restart_and_source_run_removal_without_changing_values()
    {
        using var temp = new TempDataDirectory();
        var store = new FileBenchReferenceStore(temp.Path);
        var profile = Profile();
        await store.SaveAsync(profile);
        var before = await File.ReadAllBytesAsync(store.PathFor(profile.Id));
        var restarted = new FileBenchReferenceStore(temp.Path);
        var loaded = await restarted.LoadAsync(profile.Id);
        Assert.Equal(profile.ContentSha256, loaded.ContentSha256);
        Assert.Equal(6, Assert.Single(loaded.Values).Value);
        Assert.Equal("GOLDEN-1", loaded.SourceDutSerial);
        Assert.Equal("golden-source", loaded.SourceRunId);
        Assert.Equal(profile.Id, Assert.Single(await restarted.ListAsync()).Profile!.Id);
        Assert.Equal(before, await File.ReadAllBytesAsync(store.PathFor(profile.Id)));
    }

    [Fact]
    public async Task Replacement_has_new_identity_and_existing_identity_can_never_be_overwritten()
    {
        using var temp = new TempDataDirectory();
        var store = new FileBenchReferenceStore(temp.Path);
        var first = Profile(); var replacement = Profile();
        Assert.Equal(first.Name, replacement.Name);
        Assert.NotEqual(first.Id, replacement.Id);
        await store.SaveAsync(first); await store.SaveAsync(replacement);
        var before = await File.ReadAllBytesAsync(store.PathFor(first.Id));
        var changed = first with { Name = "Overwrite attempt" };
        changed = changed with { ContentSha256 = BenchReferenceIdentity.ComputeProfileHash(changed) };
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(changed));
        Assert.Equal(before, await File.ReadAllBytesAsync(store.PathFor(first.Id)));
        Assert.Equal(2, (await store.ListAsync()).Count);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(store.PathFor(first.Id))!, "*.pending-*"));
    }

    [Fact]
    public async Task Retirement_blocks_future_selection_but_keeps_profile_and_run_owned_evidence_unchanged()
    {
        using var temp = new TempDataDirectory();
        var store = new FileBenchReferenceStore(temp.Path);
        var profile = Profile(); await store.SaveAsync(profile);
        var use = BenchReferenceEvaluator.Freeze(BenchReferenceEvaluatorTests.Rule(), profile,
            BenchReferenceEvaluatorTests.Context(), BenchReferenceEvaluatorTests.Now);
        var before = await File.ReadAllBytesAsync(store.PathFor(profile.Id));
        var description = BenchReferenceEvidence.DescribeUse(use);
        await store.RetireAsync(profile.Id, "Replaced after fixture service");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FileBenchReferenceStore(temp.Path).LoadAsync(profile.Id));
        var retired = Assert.Single(await store.ListAsync());
        Assert.True(retired.IsRetired);
        Assert.Equal("Replaced after fixture service", retired.Retirement?.Reason);
        Assert.Equal(profile.Id, retired.Retirement?.ReferenceId);
        Assert.Equal(before, await File.ReadAllBytesAsync(store.PathFor(profile.Id)));
        Assert.True(Assert.Single(BenchReferenceEvaluator.Evaluate(BenchReferenceEvaluatorTests.Source(), [use])).Passed);
        Assert.Equal(description, BenchReferenceEvidence.DescribeUse(use));
    }

    [Fact]
    public async Task Corrupt_and_future_profiles_stay_visible_as_errors_without_hiding_healthy_entries_or_rewriting_bytes()
    {
        using var temp = new TempDataDirectory();
        var store = new FileBenchReferenceStore(temp.Path);
        var healthy = Profile(); await store.SaveAsync(healthy);
        var future = Guid.NewGuid().ToString("N"); var corrupt = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(store.PathFor(future), "{\"schemaVersion\":999,\"unknownFutureValue\":true}");
        await File.WriteAllTextAsync(store.PathFor(corrupt), "{broken");
        var futureBefore = await File.ReadAllBytesAsync(store.PathFor(future));
        var corruptBefore = await File.ReadAllBytesAsync(store.PathFor(corrupt));
        var entries = await store.ListAsync();
        Assert.Equal(3, entries.Count);
        Assert.Equal(2, entries.Count(entry => entry.Error is not null));
        Assert.Single(entries, entry => entry.Profile?.Id == healthy.Id && entry.Error is null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync(future));
        await Assert.ThrowsAnyAsync<JsonException>(() => store.LoadAsync(corrupt));
        Assert.Equal(futureBefore, await File.ReadAllBytesAsync(store.PathFor(future)));
        Assert.Equal(corruptBefore, await File.ReadAllBytesAsync(store.PathFor(corrupt)));
    }

    [Fact]
    public async Task Malformed_retirement_cannot_make_a_profile_available_by_ignoring_the_marker()
    {
        using var temp = new TempDataDirectory();
        var store = new FileBenchReferenceStore(temp.Path);
        var profile = Profile(); await store.SaveAsync(profile);
        var directory = Path.Combine(temp.Path, FileBenchReferenceStore.DirectoryName, "retired");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, profile.Id + ".json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":999}");
        var before = await File.ReadAllBytesAsync(path);
        Assert.NotNull(Assert.Single(await store.ListAsync()).Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync(profile.Id));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Missing_selected_identity_never_falls_back_to_another_same_named_profile_or_backup()
    {
        using var temp = new TempDataDirectory();
        var store = new FileBenchReferenceStore(temp.Path);
        var first = Profile(); var second = Profile();
        await store.SaveAsync(first); await store.SaveAsync(second);
        File.Move(store.PathFor(first.Id), store.PathFor(first.Id) + ".bak");
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadAsync(first.Id));
        Assert.Equal(second.Id, Assert.Single(await store.ListAsync()).Profile!.Id);
        Assert.False(File.Exists(store.PathFor(first.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync("../" + second.Id));
    }

    [Fact]
    public async Task Cancelled_publication_preserves_old_profiles_and_reports_no_new_success()
    {
        using var temp = new TempDataDirectory();
        var store = new FileBenchReferenceStore(temp.Path);
        var old = Profile(); await store.SaveAsync(old);
        var before = await File.ReadAllBytesAsync(store.PathFor(old.Id));
        var next = Profile("Cancelled new reference");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(next, cancellation.Token));
        Assert.False(File.Exists(store.PathFor(next.Id)));
        Assert.Equal(before, await File.ReadAllBytesAsync(store.PathFor(old.Id)));
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task New_run_round_trip_owns_reference_snapshots_and_effective_numeric_evaluations()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var profile = Profile();
        var use = BenchReferenceEvaluator.Freeze(BenchReferenceEvaluatorTests.Rule(), profile,
            BenchReferenceEvaluatorTests.Context(), BenchReferenceEvaluatorTests.Now);
        var run = BenchReferenceEvaluatorTests.Source(6, 8);
        run.RunId = "dependent-run"; run.ExecutionScope = RunExecutionScopes.FullSuite;
        run.ReferenceUses = [use];
        run.ReferenceEvaluations = BenchReferenceEvaluator.Evaluate(run, [use]).ToList();
        await store.SaveAsync(run);
        var loaded = Assert.IsType<TestRunRecord>(await store.LoadAsync(run.RunId));
        Assert.Equal(SchemaVersions.TestRunRecord, loaded.StoredSchemaVersion);
        Assert.Equal(profile.ContentSha256, Assert.Single(loaded.ReferenceUses).ReferenceSha256);
        Assert.Equal(6, Assert.Single(loaded.ReferenceUses).ReferenceValue.Value);
        Assert.Equal(7, Assert.Single(loaded.ReferenceEvaluations).MeasuredValue);
        Assert.True(Assert.Single(loaded.ReferenceEvaluations).Passed);
        Assert.Equal(BenchReferenceEvidence.Describe(run), BenchReferenceEvidence.Describe(loaded));
    }

    [Fact]
    public async Task Previous_v4_run_is_readable_and_cannot_be_rewritten_or_promoted_as_new_reference_evidence()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var path = Path.Combine(store.GetRunDirectory("historical-v4"), "run.json");
        const string original = """{"schemaVersion":4,"runId":"historical-v4","planId":"reference-test","result":"Passed","samples":[]}""";
        await File.WriteAllTextAsync(path, original);
        var loaded = Assert.IsType<TestRunRecord>(await store.LoadAsync("historical-v4"));
        Assert.True(loaded.IsSchemaReadOnly);
        Assert.Equal(4, loaded.StoredSchemaVersion);
        Assert.Throws<InvalidOperationException>(() => BenchReferenceEvaluator.Capture("No invented provenance", loaded,
            [BenchReferenceEvaluatorTests.Rule()], BenchReferenceEvaluatorTests.Now));
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => store.SaveAsync(loaded));
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => store.SaveAsync(new TestRunRecord { RunId = loaded.RunId }));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.True(Assert.Single(await store.ListAsync()).IsSchemaReadOnly);
    }

    [Fact]
    public async Task Suite_with_known_previous_run_stays_read_only_and_preserves_the_embedded_original()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var suites = new FileSuiteRunStore(runStore, temp.RunsDirectory);
        var path = Path.Combine(suites.GetSuiteRunDirectory("historical-suite"), "suite-run.json");
        const string original = """{"schemaVersion":1,"suiteRunId":"historical-suite","planRuns":[{"schemaVersion":4,"runId":"historical-member","result":"Passed"}]}""";
        await File.WriteAllTextAsync(path, original);
        var loaded = Assert.IsType<SuiteRunRecord>(await suites.LoadAsync("historical-suite"));
        Assert.True(loaded.IsSchemaReadOnly);
        Assert.True(Assert.Single(loaded.PlanRuns).IsSchemaReadOnly);
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => suites.SaveAsync(loaded));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }
}
