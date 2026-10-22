using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class RecipeAvailabilityThreadingTests
{
    [AvaloniaFact]
    public async Task Reentrant_publication_waits_for_worker_started_by_property_subscriber()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.SelectedRecipeId = AuthoringRecipeIds.Prompt;
        await vm.WaitForRecipeAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(15));
        using var release = new ManualResetEventSlim(false);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var changed = false;
        System.ComponentModel.PropertyChangedEventHandler subscriber = (_, args) =>
        {
            if (changed || args.PropertyName != nameof(vm.CanInsertRecipe) || vm.SelectedRecipePrerequisites != "First result") return;
            changed = true;
            vm.SelectedRecipeId = AuthoringRecipeIds.Formula;
            AuthoringUiFixture.Drain(); // A nested dispatcher turn starts the replacement worker before publication returns.
            firstPublished.TrySetResult();
        };
        vm.PropertyChanged += subscriber;
        try
        {
            vm.ConfigureRecipeAvailability(action => Dispatcher.UIThread.Post(action), () => window.IsVisible, _ =>
            {
                Assert.False(Dispatcher.UIThread.CheckAccess());
                if (Interlocked.Increment(ref calls) == 1) return new("First result", null);
                secondEntered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Test did not release replacement recipe worker.");
                return new("Replacement result", null);
            });
            var idle = vm.WaitForRecipeAvailabilityAsync();
            await firstPublished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(idle.IsCompleted);
            release.Set();
            await idle.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Replacement result", vm.SelectedRecipePrerequisites);
            Assert.Equal(2, Volatile.Read(ref calls));
        }
        finally
        {
            release.Set();
            vm.PropertyChanged -= subscriber;
            vm.StopRecipeAvailability();
        }
    }

    [AvaloniaFact]
    public async Task Losing_owner_after_publication_blocks_cached_recipe_insertion()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show();
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.SelectedRecipeId = AuthoringRecipeIds.Prompt;
        vm.InsertionPosition = "End of section";
        var owner = true;
        vm.ConfigureRecipeAvailability(action => Dispatcher.UIThread.Post(action), () => owner);
        await vm.WaitForRecipeAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(vm.CanInsertRecipe, vm.SelectedRecipePrerequisites);
        var before = vm.SelectedDocument!.Snapshot;
        owner = false;
        Assert.False(vm.CanInsertRecipe);
        Assert.Contains("inactive workspace", vm.SelectedRecipePrerequisites, StringComparison.Ordinal);
        vm.InsertSelectedRecipe();
        Assert.Contains("inactive workspace", vm.Error!, StringComparison.Ordinal);
        Assert.Same(before, vm.SelectedDocument.Snapshot);
        vm.StopRecipeAvailability();
    }

    [AvaloniaFact]
    public async Task Large_plan_recipe_getters_are_cached_and_insertion_consumes_worker_prepared_output()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var original = new PlanCompiler().Load(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan"));
        var acquisition = AuthoringRecipeCatalog.EnumerateMetrics(original.Measure).First();
        var draft = original with
        {
            Measure = Enumerable.Range(0, 300).Select(index => (MeasureNode)new MetricNode(
                AuthoringSequenceOperations.WithOutputChannel(acquisition, $"input-{index}") with { Name = $"Acquire {index}" })).ToArray()
        };
        var document = AuthoringDocumentDto.FromDraft(draft);
        document.RequiresCompilation = true;
        new AuthoringDocumentStore(fixture.WorkspaceRoot).Save(document);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.SelectedRecipeId = AuthoringRecipeIds.Acquire;
        vm.InsertionPosition = "End of test steps";
        await vm.WaitForRecipeAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(20));
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Guid? preparedNode = null;
        vm.ConfigureRecipeAvailability(action => Dispatcher.UIThread.Post(action), () => window.IsVisible, input =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Test did not release recipe worker.");
            var result = AuthoringWorkspaceViewModel.EvaluateRecipeAvailability(input);
            preparedNode = result.Prepared?.Measure[^1].NodeId;
            return result;
        });
        AuthoringUiFixture.Drain();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            for (var index = 0; index < 250; index++)
            {
                Assert.False(vm.CanInsertRecipe);
                Assert.Contains("Checking", vm.SelectedRecipePrerequisites, StringComparison.Ordinal);
            }
            Assert.Equal(1, Volatile.Read(ref calls));
            var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() => heartbeat.TrySetResult());
            await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { release.Set(); }
        await vm.WaitForRecipeAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(vm.CanInsertRecipe, vm.SelectedRecipePrerequisites);
        Assert.NotNull(preparedNode);
        vm.InsertSelectedRecipe();
        Assert.Null(vm.Error);
        Assert.Equal(301, vm.SelectedProgram!.Measure.Count);
        Assert.Equal(preparedNode, vm.SelectedSequence!.NodeId);
        vm.StopRecipeAvailability();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_recipe_result_is_discarded_after_selection_or_workspace_changes(bool replaceWorkspace)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        using var replacement = new AuthoringUiFixture();
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.SelectedRecipeId = AuthoringRecipeIds.Prompt;
        await vm.WaitForRecipeAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(15));
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        vm.ConfigureRecipeAvailability(action => Dispatcher.UIThread.Post(action), () => window.IsVisible, input =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Test did not release recipe worker.");
                return new("Obsolete prerequisites", null);
            }
            return new($"Latest prerequisites: {input.RecipeId} / {input.InsertionPosition}", null);
        });
        AuthoringUiFixture.Drain();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (replaceWorkspace) vm.Open(replacement.WorkspaceRoot);
            else vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
            vm.SelectedRecipeId = AuthoringRecipeIds.Formula;
            vm.InsertionPosition = "Before selected";
            Assert.DoesNotContain("Obsolete", vm.SelectedRecipePrerequisites, StringComparison.Ordinal);
        }
        finally { release.Set(); }
        await vm.WaitForRecipeAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal($"Latest prerequisites: {AuthoringRecipeIds.Formula} / Before selected", vm.SelectedRecipePrerequisites);
        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.False(vm.CanInsertRecipe);
        vm.StopRecipeAvailability();
    }
}
