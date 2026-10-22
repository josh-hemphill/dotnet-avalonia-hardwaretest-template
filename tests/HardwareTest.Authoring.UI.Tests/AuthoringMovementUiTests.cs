using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringMovementUiTests
{
    [AvaloniaFact]
    public async Task Directional_controls_explain_boundaries_dependencies_and_safe_moves()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(0);
        var vm = fixture.ViewModel;
        vm.CreateDemoProgram("movement-help");
        var draft = AuthoringRecipeCatalog.Apply(vm.SelectedProgram!, AuthoringRecipeIds.Acquire);
        draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Formula);
        draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Acquire);
        var steps = AuthoringSequence.Flatten(draft).Where(row => row.Kind == SequenceRowKind.Metric)
            .Select(row => AuthoringSequence.ResolveMeasure(draft, row.IndexPath)!).ToArray();
        var group = new SectionNode("Power rails", steps);
        var destination = new SectionNode("Radio", []);
        vm.ReplaceSelected(draft with { Measure = [group, destination] });
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == steps[0].NodeId));
        await vm.WaitForMovementAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var actions = fixture.Control<Button>("Selected step actions");
        actions.Flyout!.ShowAt(actions); AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<Button>("Move selected step up").IsEnabled);
        Assert.Contains("Already first", fixture.Control<TextBlock>("Move up explanation").Text);
        Assert.False(fixture.Control<Button>("Move selected step down").IsEnabled);
        Assert.Contains("VDC", fixture.Control<TextBlock>("Move down explanation").Text);
        vm.SelectedSectionDestination = vm.SectionDestinations.Single(item => item.NodeId == destination.NodeId);
        await vm.WaitForMovementAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(vm.CanMoveToSection);
        Assert.Contains("VDC", fixture.Control<TextBlock>("Move to section explanation").Text);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == steps[2].NodeId));
        await vm.WaitForMovementAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(fixture.Control<Button>("Move selected step up").IsEnabled);
        Assert.False(fixture.Control<Button>("Move selected step down").IsEnabled);
        Assert.Contains("Already last", fixture.Control<TextBlock>("Move down explanation").Text);
        var before = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        AuthoringUiFixture.Click(fixture.Control<Button>("Move selected step up"));
        Assert.Null(vm.Error);
        Assert.Equal(steps[2].NodeId, Assert.IsType<SectionNode>(vm.SelectedProgram!.Measure[0]).Children[1].NodeId);
        vm.Undo();
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
    }

    [AvaloniaFact]
    public async Task Rejected_drop_explains_cross_group_scope_and_preserves_plan()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(0);
        var vm = fixture.ViewModel;
        var metric = AuthoringRecipeCatalog.EnumerateMetrics(vm.SelectedProgram!.Measure).First();
        var first = new MetricNode(metric);
        var second = new MetricNode(metric with { ChannelKey = "independent", Name = "Independent" });
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = [new SectionNode("Power", [first]), new SectionNode("Radio", [second])] });
        var before = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        vm.ReorderSequence(first.NodeId, second.NodeId);
        await vm.SequenceMoveTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("Drop not applied.", vm.Status);
        Assert.Contains("Move to section", vm.Error);
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        vm.ReportSequenceDropFailure("Drop on an item in the same group.");
        Assert.Contains("Cannot drop here", vm.Error);
        Assert.Contains("same group", vm.Error);
    }
    [AvaloniaFact]
    public async Task A_later_invalid_drop_cancels_an_earlier_valid_drop_without_overwriting_feedback()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(0);
        var vm = fixture.ViewModel;
        var metric = AuthoringRecipeCatalog.EnumerateMetrics(vm.SelectedProgram!.Measure).First();
        var first = new MetricNode(metric);
        var second = new MetricNode(metric with { ChannelKey = "independent", Name = "Independent" });
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = [new SectionNode("Power", [first, second])] });
        var before = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        var computed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Func<ProgramDraft>, Task<ProgramDraft>> evaluate = work => Task.Run(async () =>
        {
            var result = work();
            computed.TrySetResult();
            await release.Task;
            return result;
        });
        typeof(AuthoringWorkspaceViewModel).GetField("_evaluateSequenceDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, evaluate);
        try
        {
            vm.ReorderSequence(second.NodeId, first.NodeId);
            var pending = vm.SequenceMoveTask;
            await computed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            vm.ReportSequenceDropFailure("Drop on an item, not the phase heading.");
            var rejection = vm.Error;
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(rejection, vm.Error);
            Assert.Equal("Drop not applied.", vm.Status);
            Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        }
        finally { release.TrySetResult(); }
    }

    [AvaloniaFact]
    public async Task Drop_publication_uses_configured_dispatch_and_clears_only_its_stale_progress()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(0);
        var vm = fixture.ViewModel;
        var metric = AuthoringRecipeCatalog.EnumerateMetrics(vm.SelectedProgram!.Measure).First();
        var first = new MetricNode(metric);
        var second = new MetricNode(metric with { ChannelKey = "independent", Name = "Independent" });
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = [new SectionNode("Power", [first, second])] });
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == second.NodeId));
        await vm.WaitForMovementAvailabilityAsync();
        var before = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        var dispatched = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchField = typeof(AuthoringWorkspaceViewModel).GetField("_movementDispatch", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = dispatchField.GetValue(vm);
        dispatchField.SetValue(vm, (Action<Action>)(action => dispatched.TrySetResult(action)));
        try
        {
            vm.ReorderSequence(second.NodeId, first.NodeId);
            var publication = await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(vm.SequenceMoveTask.IsCompleted);
            Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
            dispatchField.SetValue(vm, original);
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == first.NodeId));
            publication();
            await vm.SequenceMoveTask.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("Drop cancelled because the plan or selection changed.", vm.Status);
            Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        }
        finally { dispatchField.SetValue(vm, original); }
    }

    [AvaloniaFact]
    public async Task Movement_checks_recover_when_reconfigured_after_dispatch_rejection()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(0);
        var vm = fixture.ViewModel;
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.IsSelectable));
        await vm.WaitForMovementAvailabilityAsync();
        var calls = 0;
        vm.ConfigureMovementAvailability(action =>
        {
            if (Interlocked.Increment(ref calls) == 1) action();
            else throw new InvalidOperationException("Dispatcher closed");
        }, () => true);
        await vm.WaitForMovementAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(15));
        vm.ConfigureMovementAvailability(action => Avalonia.Threading.Dispatcher.UIThread.Post(action), () => true);
        await vm.WaitForMovementAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.DoesNotContain("Checking", vm.MoveSequenceUpReason);
        Assert.DoesNotContain("Checking", vm.MoveSequenceDownReason);
    }

    [AvaloniaFact]
    public async Task Restarting_movement_owner_does_not_revive_an_old_drop()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(0);
        var vm = fixture.ViewModel;
        var metric = AuthoringRecipeCatalog.EnumerateMetrics(vm.SelectedProgram!.Measure).First();
        var first = new MetricNode(metric);
        var second = new MetricNode(metric with { ChannelKey = "independent", Name = "Independent" });
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = [new SectionNode("Power", [first, second])] });
        var before = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        var computed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Func<ProgramDraft>, Task<ProgramDraft>> evaluate = work => Task.Run(async () =>
        {
            var result = work();
            computed.TrySetResult();
            await release.Task;
            return result;
        });
        typeof(AuthoringWorkspaceViewModel).GetField("_evaluateSequenceDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, evaluate);
        try
        {
            vm.ReorderSequence(second.NodeId, first.NodeId);
            var pending = vm.SequenceMoveTask;
            await computed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            vm.StopMovementAvailability();
            vm.ConfigureMovementAvailability(action => Avalonia.Threading.Dispatcher.UIThread.Post(action), () => true);
            var status = vm.Status;
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(status, vm.Status);
            Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        }
        finally { release.TrySetResult(); }
    }

}
