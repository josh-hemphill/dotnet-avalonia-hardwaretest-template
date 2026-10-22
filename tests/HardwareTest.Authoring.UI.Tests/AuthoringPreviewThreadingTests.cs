using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringPreviewThreadingTests
{
    [AvaloniaFact]
    public async Task Preview_uses_owned_ingest_source_when_public_recording_DTO_is_mutated()
    {
        using var fixture = new AuthoringUiFixture();
        var directory = Path.Combine(fixture.WorkspaceRoot, "recordings", "owned");
        Directory.CreateDirectory(directory);
        var run = new TestRunRecord
        {
            PlanId = "sample",
            RunId = "owned",
            Samples = [new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 3 }]
        };
        File.WriteAllText(Path.Combine(directory, "run.json"), JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        var vm = fixture.ViewModel;
        vm.Open(fixture.WorkspaceRoot);
        vm.SelectDataset(0);
        vm.ConfigurePreviewProcessing(action => Dispatcher.UIThread.Post(action), () => true);
        // The compatibility DTO remains mutable for domain callers. A background
        // request must not enumerate this live object or copy these later values.
        vm.SelectedDataset!.Run.Samples[0].Value = 999;
        vm.SelectedDataset.Run.Samples.Add(new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 1000 });
        await vm.WaitForPreviewAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var acquisition = Assert.Single(vm.BoardTiles, tile => tile.Preview.ChannelKey == "VDC");
        Assert.Equal([3.0], acquisition.Preview.CannedSamples);
        Assert.Contains("1 samples", vm.DataSourceDetails, StringComparison.Ordinal);
        vm.StopPreviewProcessing();
    }

    [AvaloniaFact]
    public async Task Program_picker_keeps_progress_and_dispatcher_responsive_until_worker_publishes_current_plan()
    {
        using var fixture = new AuthoringUiFixture();
        var vm = fixture.ViewModel;
        vm.Open(fixture.WorkspaceRoot);
        vm.DisplayName = "Edited sample retained";
        vm.InitializePlan(new("another-program") { Instruments = [] });
        var window = fixture.Show();
        await vm.WaitForPreviewAsync().WaitAsync(TimeSpan.FromSeconds(15));
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ConfigurePreviewProcessing(action => Dispatcher.UIThread.Post(action), () => window.IsVisible, input =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            if (input.PlanId == "sample")
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Preview test did not release worker.");
            }
            return AuthoringPreviewPresentation.Empty with { Preview = MetricPreviewBuilder.Empty with { ChannelKey = input.PlanId ?? "" } };
        });
        fixture.Control<ComboBox>("Selected test plan").SelectedItem = vm.ProgramRows.Single(row => row.PlanId == "sample");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(vm.LoadingBusy);
            Assert.True(vm.PreviewBusy);
            Assert.True(window.FindControl<Border>("LoadingOverlay")!.IsEffectivelyVisible);
            var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() => heartbeat.TrySetResult());
            await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotEqual("another-program", vm.Preview.ChannelKey);
        }
        finally { release.Set(); }
        await vm.WaitForPreviewAsync().WaitAsync(TimeSpan.FromSeconds(15));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (vm.LoadingBusy) await Task.Delay(1, timeout.Token);
        Assert.Equal("sample", vm.SelectedProgram!.PlanId);
        Assert.Equal("sample", vm.Preview.ChannelKey);
        Assert.Equal("Edited sample retained", vm.DisplayName);
    }

    [AvaloniaFact]
    public async Task Desktop_preview_evaluates_off_dispatcher_and_getters_only_read_published_values()
    {
        using var fixture = new AuthoringUiFixture();
        fixture.ViewModel.Open(fixture.WorkspaceRoot);
        var vm = fixture.ViewModel;
        var calls = 0;
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ConfigurePreviewProcessing(action => Dispatcher.UIThread.Post(action), () => true, input =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Preview test did not release worker.");
            return AuthoringWorkspaceViewModel.EvaluatePreview(input);
        });
        AuthoringUiFixture.Drain();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(vm.PreviewBusy);
            // These bindings used to evaluate the whole graph, construct OpenTAP
            // steps and read raw fragments on the UI thread on every access.
            for (var i = 0; i < 5; i++)
            {
                Assert.Empty(vm.BoardTiles);
                _ = vm.Preview;
                _ = vm.PreviewChrome;
                _ = vm.PreviewNote;
                _ = vm.FormulaRecordingEvidence;
                _ = vm.FormulaDeploymentStatus;
                _ = vm.FormulaError;
                _ = vm.FormulaSaveNote;
            }
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally { release.Set(); }
        await vm.WaitForPreviewAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotEmpty(vm.BoardTiles);
        var published = vm.BoardTiles;
        Assert.Same(published, vm.BoardTiles);
        Assert.False(vm.PreviewBusy);
        vm.StopPreviewProcessing();
    }

    [AvaloniaFact]
    public async Task Obsolete_document_worker_cannot_publish_into_newer_selection()
    {
        using var fixture = new AuthoringUiFixture();
        fixture.ViewModel.Open(fixture.WorkspaceRoot);
        var vm = fixture.ViewModel;
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var evaluatedNames = new List<string>();
        vm.ConfigurePreviewProcessing(action => Dispatcher.UIThread.Post(action), () => true, input =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            var name = input.Document!.Restore().Sidecar.DisplayName ?? string.Empty;
            evaluatedNames.Add(name);
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Preview test did not release worker.");
            }
            return AuthoringPreviewPresentation.Empty with { Preview = MetricPreviewBuilder.Empty with { ChannelKey = name } };
        });
        AuthoringUiFixture.Drain();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            vm.DisplayName = "Intermediate edit";
            vm.DisplayName = "Latest edit";
            Assert.Empty(vm.Preview.ChannelKey);
        }
        finally { release.Set(); }
        await vm.WaitForPreviewAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("Latest edit", vm.Preview.ChannelKey);
        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.Equal("Latest edit", evaluatedNames[^1]);
        Assert.DoesNotContain("Intermediate edit", evaluatedNames);
        vm.StopPreviewProcessing();
    }

    [AvaloniaFact]
    public async Task Closing_discards_late_preview_and_resolves_waiters()
    {
        using var fixture = new AuthoringUiFixture();
        fixture.ViewModel.Open(fixture.WorkspaceRoot);
        var vm = fixture.ViewModel;
        var window = fixture.Show();
        await vm.WaitForPreviewAsync().WaitAsync(TimeSpan.FromSeconds(15));
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ConfigurePreviewProcessing(action =>
        {
            Dispatcher.UIThread.Post(action);
            if (finished.Task.IsCompleted) posted.TrySetResult();
        }, () => window.IsVisible, _ =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Preview test did not release worker.");
            finished.TrySetResult();
            return AuthoringPreviewPresentation.Empty with { Preview = MetricPreviewBuilder.Empty with { ChannelKey = "late" } };
        });
        AuthoringUiFixture.Drain();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        window.Close();
        Assert.False(window.IsVisible);
        await vm.WaitForPreviewAsync();
        release.Set();
        await posted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        AuthoringUiFixture.Drain();
        Assert.NotEqual("late", vm.Preview.ChannelKey);
        Assert.False(vm.PreviewBusy);
    }

    [AvaloniaFact]
    public void Hidden_preview_does_not_construct_board_widgets_until_revealed()
    {
        using var fixture = new AuthoringUiFixture();
        fixture.ViewModel.Open(fixture.WorkspaceRoot);
        var pane = new OperatorPreviewPane { DataContext = fixture.ViewModel, IsVisible = false };
        var window = new Window { Content = pane, Width = 900, Height = 600 };
        window.Show();
        try
        {
            AuthoringUiFixture.Drain();
            Assert.Empty(pane.GetVisualDescendants().OfType<BoardPreviewTileView>());
            fixture.ViewModel.DisplayName = "Changed while hidden";
            AuthoringUiFixture.Drain();
            Assert.Empty(pane.GetVisualDescendants().OfType<BoardPreviewTileView>());
            pane.IsVisible = true;
            pane.UpdateLayout();
            AuthoringUiFixture.Drain();
            Assert.NotEmpty(pane.GetVisualDescendants().OfType<BoardPreviewTileView>());
        }
        finally { window.Close(); }
    }
}
