using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Host.Worker;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.Tests.Fixtures;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class WorkerProgressOrderingTests
{
    [Fact]
    public async Task Blocked_callback_delays_terminal_response_without_blocking_worker_controls()
    {
        using var temp = new TempDataDirectory();
        using var process = StartWorker(temp.Path);
        await LoadFlatLeavesAsync(process);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var items = new List<OpenTapProgress>();
        var active = 0;
        var concurrent = 0;
        var count = 0;
        var run = RunAsync(process, envelope =>
        {
            if (Interlocked.Increment(ref active) != 1)
            {
                Interlocked.Exchange(ref concurrent, 1);
            }
            try
            {
                if (Interlocked.Increment(ref count) == 1)
                {
                    entered.SetResult();
                    release.Wait();
                }
                var progress = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.OpenTapProgress);
                Assert.NotNull(progress);
                lock (items)
                {
                    items.Add(progress);
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
                callbackExited.TrySetResult();
            }
        });

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var ping = await process.Request(WorkerProtocol.Ping, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(ping.Ok, ping.Error);
            // Resume is a real control RPC and returns the session snapshot. The plan must
            // finish even while the first remote progress handler remains blocked.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            WorkerSnapshot snapshot;
            do
            {
                var control = await process.Request(WorkerProtocol.Resume, deadline.Token);
                Assert.True(control.Ok, control.Error);
                snapshot = WorkerProtocol.ReadPayload(control, WorkerJsonContext.Default.WorkerSnapshot)!;
                Assert.NotNull(snapshot);
            }
            while (snapshot.IsExecuting);

            Assert.False(run.IsCompleted);
            Assert.Equal(1, Volatile.Read(ref count));
            release.Set();
            var response = await run.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(response.Ok, response.Error);
            Assert.Equal(0, Volatile.Read(ref concurrent));
            lock (items)
            {
                Assert.NotEmpty(items);
                Assert.True(items[^1].IsCompleted);
                Assert.Single(items, p => p.IsCompleted);
            }
        }
        finally
        {
            release.Set();
            process.Stop(writeDossier: false);
            if (entered.Task.IsCompleted)
            {
                await callbackExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task Throwing_callback_fails_request_and_next_run_has_fresh_progress_lifetime()
    {
        using var temp = new TempDataDirectory();
        using var process = StartWorker(temp.Path);
        await LoadFlatLeavesAsync(process);

        var failed = await RunAsync(process, _ => throw new InvalidOperationException("progress callback rejected"))
            .WaitAsync(TimeSpan.FromSeconds(60));
        Assert.False(failed.Ok);
        Assert.Contains("progress callback rejected", failed.Error, StringComparison.Ordinal);
        Assert.True(process.IsAlive);
        var items = new List<OpenTapProgress>();
        var next = await RunAsync(process, envelope =>
        {
            var progress = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.OpenTapProgress);
            Assert.NotNull(progress);
            items.Add(progress);
        }).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(next.Ok, next.Error);
        Assert.NotEmpty(items);
        Assert.True(items[^1].IsCompleted);
    }

    [Fact]
    public async Task Rejected_operator_prompt_aborts_execution_before_error_response_and_worker_reuse()
    {
        using var temp = new TempDataDirectory();
        using var process = StartWorker(temp.Path);
        OpenTapPluginSearch.SearchSerialized();
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new OperatorPromptStep { Message = "Await rejection", Name = "Operator prompt" });
        var planPath = Path.Combine(temp.Path, "operator-rejection.TapPlan");
        plan.Save(planPath);
        var loaded = await process.Request(WorkerProtocol.LoadPlan,
            new WorkerPathRequest { Path = planPath }, WorkerJsonContext.Default.WorkerPathRequest, CancellationToken.None);
        Assert.True(loaded.Ok, loaded.Error);
        var rejectedPrompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var response = await RunAsync(process, envelope =>
        {
            var progress = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.OpenTapProgress);
            Assert.NotNull(progress);
            if (progress.AwaitingOperator)
            {
                rejectedPrompt.TrySetResult();
                throw new InvalidOperationException("operator prompt delivery rejected");
            }
        }).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(rejectedPrompt.Task.IsCompleted);
        Assert.False(response.Ok);
        Assert.Contains("operator prompt delivery rejected", response.Error, StringComparison.Ordinal);
        var control = await process.Request(WorkerProtocol.TryGetStepConditionSummary,
            new WorkerPathRequest { Path = "missing" }, WorkerJsonContext.Default.WorkerPathRequest,
            CancellationToken.None);
        Assert.True(control.Ok, control.Error);
        var snapshot = WorkerProtocol.ReadPayload(control, WorkerJsonContext.Default.WorkerBoolResult)?.Snapshot;
        Assert.NotNull(snapshot);
        Assert.False(snapshot.IsExecuting);
        Assert.False(snapshot.IsAwaitingOperator);
        Assert.True(process.IsAlive);
        await LoadFlatLeavesAsync(process);
        var items = new List<OpenTapProgress>();
        var next = await RunAsync(process, envelope =>
        {
            var progress = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.OpenTapProgress);
            Assert.NotNull(progress);
            items.Add(progress);
        }).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(next.Ok, next.Error);
        Assert.NotEmpty(items);
        Assert.True(items[^1].IsCompleted);
    }

    [Fact]
    public async Task Client_clears_operator_state_after_rejected_prompt_and_can_run_again()
    {
        using var temp = new TempDataDirectory();
        using var client = new OpenTapWorkerClient(
            new AppSettings { UseMockVisa = true, CrashEnabled = false, DataDirectory = temp.Path });
        OpenTapPluginSearch.SearchSerialized();
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new OperatorPromptStep { Message = "Client prompt", Name = "Operator prompt" });
        var planPath = Path.Combine(temp.Path, "client-operator-rejection.TapPlan");
        plan.Save(planPath);
        await client.LoadPlanAsync(planPath);
        var rejected = false;
        var progress = new CallbackProgress(value =>
        {
            if (value.AwaitingOperator)
            {
                Assert.True(client.IsAwaitingOperator);
                Assert.NotNull(client.OperatorPromptMessage);
                Assert.NotNull(client.PendingInteraction);
                rejected = true;
                throw new InvalidOperationException("client prompt delivery rejected");
            }
        });

        var error = await Record.ExceptionAsync(() => client.RunAsync(progress).WaitAsync(TimeSpan.FromSeconds(60)));

        Assert.True(rejected);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("client prompt delivery rejected", error.Message, StringComparison.Ordinal);
        Assert.False(client.IsExecuting);
        Assert.False(client.IsAwaitingOperator);
        Assert.Null(client.OperatorPromptMessage);
        Assert.Null(client.PendingInteraction);
        Assert.Equal(planPath, client.LoadedPlanPath);
        await client.LoadPlanShapeAsync(PlanShapeFixtures.FlatLeavesName);
        var items = new List<OpenTapProgress>();
        await client.RunAsync(new CallbackProgress(items.Add)).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.NotEmpty(items);
        Assert.True(items[^1].IsCompleted);
        Assert.False(client.IsExecuting);
        Assert.False(client.IsAwaitingOperator);
        Assert.Null(client.OperatorPromptMessage);
        Assert.Null(client.PendingInteraction);
    }

    [Fact]
    public async Task Overlapping_client_run_does_not_clear_active_operator_interaction()
    {
        using var temp = new TempDataDirectory();
        using var client = new OpenTapWorkerClient(
            new AppSettings { UseMockVisa = true, CrashEnabled = false, DataDirectory = temp.Path });
        OpenTapPluginSearch.SearchSerialized();
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new OperatorPromptStep { Message = "Keep this prompt", Name = "Operator prompt" });
        var planPath = Path.Combine(temp.Path, "overlapping-run.TapPlan");
        plan.Save(planPath);
        await client.LoadPlanAsync(planPath);
        var awaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = client.RunAsync(new CallbackProgress(value =>
        {
            if (value.AwaitingOperator)
            {
                awaiting.TrySetResult();
            }
        }));
        try
        {
            await awaiting.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var pending = client.PendingInteraction;
            Assert.NotNull(pending);

            var secondError = await Record.ExceptionAsync(() => client.RunAsync().WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.IsType<InvalidOperationException>(secondError);
            Assert.Contains("already in progress", secondError.Message, StringComparison.Ordinal);
            Assert.True(client.IsExecuting);
            Assert.True(client.IsAwaitingOperator);
            Assert.Equal("Keep this prompt", client.OperatorPromptMessage);
            Assert.Same(pending, client.PendingInteraction);
            client.Resume();
            await first.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.False(client.IsExecuting);
            Assert.False(client.IsAwaitingOperator);
            Assert.Null(client.OperatorPromptMessage);
            Assert.Null(client.PendingInteraction);
        }
        finally
        {
            if (!first.IsCompleted)
            {
                client.Abort();
                await first.WaitAsync(TimeSpan.FromSeconds(30));
            }
        }
    }

    [Fact]
    public async Task Stopping_connection_releases_pending_run_even_with_blocked_callback()
    {
        using var temp = new TempDataDirectory();
        using var process = StartWorker(temp.Path);
        await LoadFlatLeavesAsync(process);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = RunAsync(process, _ =>
        {
            entered.TrySetResult();
            try
            {
                release.Wait();
            }
            finally
            {
                exited.TrySetResult();
            }
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(60));
            process.Stop(writeDossier: false);
            var error = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsType<OpenTapWorkerProcessException>(error);
            Assert.False(process.IsAlive);
        }
        finally
        {
            release.Set();
            process.Stop(writeDossier: false);
            if (entered.Task.IsCompleted)
            {
                await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    private static OpenTapWorkerProcess StartWorker(string directory)
    {
        var process = new OpenTapWorkerProcess();
        process.Start(new AppSettings { UseMockVisa = true, CrashEnabled = false, DataDirectory = directory });
        return process;
    }

    private static async Task LoadFlatLeavesAsync(OpenTapWorkerProcess process)
    {
        var response = await process.Request(WorkerProtocol.LoadPlanShape,
            new WorkerFixtureRequest { FixtureFileName = PlanShapeFixtures.FlatLeavesName },
            WorkerJsonContext.Default.WorkerFixtureRequest, CancellationToken.None);
        Assert.True(response.Ok, response.Error);
    }

    private static Task<WorkerEnvelope> RunAsync(OpenTapWorkerProcess process, Action<WorkerEnvelope> callback)
        => process.Request(WorkerProtocol.Run, new WorkerRunRequest(),
            WorkerJsonContext.Default.WorkerRunRequest, CancellationToken.None, callback);

    private sealed class CallbackProgress(Action<OpenTapProgress> callback) : IProgress<OpenTapProgress>
    {
        public void Report(OpenTapProgress value) => callback(value);
    }
}
