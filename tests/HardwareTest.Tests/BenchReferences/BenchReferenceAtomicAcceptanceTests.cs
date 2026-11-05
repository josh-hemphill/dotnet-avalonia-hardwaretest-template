using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Time;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.BenchReferences;

public sealed class BenchReferenceAtomicAcceptanceTests
{
    [Fact]
    public async Task All_profiles_share_one_acceptance_instant_and_retirement_cannot_commit_partway_through_freeze()
    {
        using var temp = new TempDataDirectory();
        using var clock = new BlockingClock(BenchReferenceEvaluatorTests.Now);
        var firstStore = new FileBenchReferenceStore(temp.Path, clock);
        var secondStore = new FileBenchReferenceStore(temp.Path, clock);
        var first = Profile("First"); var second = Profile("Second");
        await firstStore.SaveAsync(first); await firstStore.SaveAsync(second);
        var rule = BenchReferenceEvaluatorTests.Rule(); var nextRule = rule with { RuleId = "second-criterion" };
        var selections = new[] { Selection(rule, first), Selection(nextRule, second) };
        var freezing = Task.Run(() => firstStore.FreezeAsync(selections, BenchReferenceEvaluatorTests.Context()));
        await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var retiring = secondStore.RetireAsync(first.Id, "Retired after accepted run boundary");
        Assert.False(retiring.IsCompleted);
        clock.Release.Set();
        var accepted = await freezing.WaitAsync(TimeSpan.FromSeconds(5));
        await retiring.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(BenchReferenceEvaluatorTests.Now, accepted.AcceptedAt);
        Assert.Equal(2, accepted.Uses.Count);
        Assert.All(accepted.Uses, use => Assert.Equal(accepted.AcceptedAt, use.AcceptedAt));
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstStore.LoadAsync(first.Id));
        // The run already accepted before retirement retains its exact numeric snapshots.
        Assert.All(BenchReferenceEvaluator.Evaluate(BenchReferenceEvaluatorTests.Source().Samples, accepted.Uses), evaluation => Assert.True(evaluation.Passed));
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstStore.FreezeAsync(selections, BenchReferenceEvaluatorTests.Context()));
    }

    [Fact]
    public async Task A_previously_retired_member_refuses_the_complete_selection_without_substitution()
    {
        using var temp = new TempDataDirectory();
        var clock = new FixedClock(BenchReferenceEvaluatorTests.Now);
        var store = new FileBenchReferenceStore(temp.Path, clock);
        var first = Profile("First"); var second = Profile("Second");
        await store.SaveAsync(first); await store.SaveAsync(second);
        await new FileBenchReferenceStore(temp.Path, clock).RetireAsync(first.Id, "No longer qualified");
        var firstBytes = File.ReadAllBytes(store.PathFor(first.Id)); var secondBytes = File.ReadAllBytes(store.PathFor(second.Id));
        var rules = new[] { BenchReferenceEvaluatorTests.Rule(), BenchReferenceEvaluatorTests.Rule() with { RuleId = "second" } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FreezeAsync(
            [Selection(rules[0], first), Selection(rules[1], second)], BenchReferenceEvaluatorTests.Context()));
        Assert.Equal(firstBytes, File.ReadAllBytes(store.PathFor(first.Id)));
        Assert.Equal(secondBytes, File.ReadAllBytes(store.PathFor(second.Id)));
    }

    [Fact]
    public async Task Freeze_preserves_the_inspected_id_revision_and_hash_and_refuses_changed_numbers()
    {
        using var temp = new TempDataDirectory();
        var store = new FileBenchReferenceStore(temp.Path, new FixedClock(BenchReferenceEvaluatorTests.Now));
        var profile = Profile("Shown to operator"); await store.SaveAsync(profile);
        var selected = Selection(BenchReferenceEvaluatorTests.Rule(), profile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FreezeAsync(
            [selected with { ExpectedSha256 = BenchReferenceIdentity.HashText("different shown content") }], BenchReferenceEvaluatorTests.Context()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FreezeAsync(
            [selected with { ExpectedRevision = 99 }], BenchReferenceEvaluatorTests.Context()));
        var accepted = await store.FreezeAsync([selected], BenchReferenceEvaluatorTests.Context());
        Assert.Equal(profile.Id, Assert.Single(accepted.Uses).ReferenceId);
        Assert.Equal(profile.ContentSha256, Assert.Single(accepted.Uses).ReferenceSha256);
    }

    [Fact]
    public async Task Expiry_is_checked_at_the_single_transaction_instant_for_every_member()
    {
        using var temp = new TempDataDirectory();
        var store = new FileBenchReferenceStore(temp.Path, new FixedClock(BenchReferenceEvaluatorTests.Now.AddMinutes(1)));
        var first = Profile("First");
        var second = BenchReferenceEvaluator.Capture("Expiring", BenchReferenceEvaluatorTests.Source(),
            [BenchReferenceEvaluatorTests.Rule()], BenchReferenceEvaluatorTests.Now, BenchReferenceEvaluatorTests.Now.AddMinutes(1));
        await store.SaveAsync(first); await store.SaveAsync(second);
        var selections = new[]
        {
            Selection(BenchReferenceEvaluatorTests.Rule(), first),
            Selection(BenchReferenceEvaluatorTests.Rule() with { RuleId = "second" }, second),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FreezeAsync(selections, BenchReferenceEvaluatorTests.Context()));
    }

    private static BenchReferenceProfile Profile(string name)
        => BenchReferenceEvaluator.Capture(name, BenchReferenceEvaluatorTests.Source(),
            [BenchReferenceEvaluatorTests.Rule()], BenchReferenceEvaluatorTests.Now);

    private static BenchReferenceSelection Selection(BenchReferenceRule rule, BenchReferenceProfile profile)
        => new() { Rule = rule, ReferenceId = profile.Id, ExpectedRevision = profile.Revision, ExpectedSha256 = profile.ContentSha256 };

    private sealed class FixedClock(DateTimeOffset time) : IClock
    {
        public DateTimeOffset UtcNow => time;
    }

    private sealed class BlockingClock(DateTimeOffset time) : IClock, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public DateTimeOffset UtcNow
        {
            get
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The atomic acceptance test did not release its catalog clock.");
                return time;
            }
        }
        public void Dispose() { Release.Set(); Release.Dispose(); }
    }
}
