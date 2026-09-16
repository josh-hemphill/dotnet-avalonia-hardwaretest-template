using HardwareTest.Shell;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class DeferredStartupTests
{
    [Fact]
    public async Task Cancel_during_in_flight_warmup_skips_later_apps_and_still_completes()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = new TokenWarmupStub(
            "vendor.block",
            async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            });
        var later = new TokenWarmupStub("vendor.later", _ => Task.CompletedTask);
        var complete = 0;
        var unexpected = 0;

        var run = DeferredStartup.RunProtectedAsync(
            token => ShellApplicationWarmup.RunAsync(
                [blocking, later],
                new ServiceCollection().BuildServiceProvider(),
                token),
            cts.Token,
            () =>
            {
                Interlocked.Increment(ref complete);
                return Task.CompletedTask;
            },
            _ =>
            {
                Interlocked.Increment(ref unexpected);
                return Task.CompletedTask;
            });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, blocking.Calls);
        Assert.Equal(0, later.Calls);
        Assert.Equal(1, complete);
        Assert.Equal(0, unexpected);
    }

    [Fact]
    public async Task Unexpected_fault_still_completes()
    {
        var complete = 0;
        Exception? unexpected = null;

        await DeferredStartup.RunProtectedAsync(
            _ => throw new InvalidOperationException("boom"),
            CancellationToken.None,
            () =>
            {
                Interlocked.Increment(ref complete);
                return Task.CompletedTask;
            },
            ex =>
            {
                unexpected = ex;
                return Task.CompletedTask;
            }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, complete);
        var boom = Assert.IsType<InvalidOperationException>(unexpected);
        Assert.Equal("boom", boom.Message);
    }

    [Fact]
    public async Task Cancel_after_warmup_finished_is_a_no_op()
    {
        using var cts = new CancellationTokenSource();
        var complete = 0;
        var unexpected = 0;
        var app = new TokenWarmupStub("vendor.ok", _ => Task.CompletedTask);

        await DeferredStartup.RunProtectedAsync(
            token => ShellApplicationWarmup.RunAsync(
                [app],
                new ServiceCollection().BuildServiceProvider(),
                token),
            cts.Token,
            () =>
            {
                Interlocked.Increment(ref complete);
                return Task.CompletedTask;
            },
            _ =>
            {
                Interlocked.Increment(ref unexpected);
                return Task.CompletedTask;
            }).WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();

        Assert.Equal(1, app.Calls);
        Assert.Equal(1, complete);
        Assert.Equal(0, unexpected);
    }

    [Fact]
    public async Task Cancel_between_work_success_and_onComplete_still_completes()
    {
        using var cts = new CancellationTokenSource();
        var workDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = 0;
        var unexpected = 0;

        var run = DeferredStartup.RunProtectedAsync(
            _ =>
            {
                workDone.SetResult();
                return Task.CompletedTask;
            },
            cts.Token,
            async () =>
            {
                await releaseComplete.Task;
                Interlocked.Increment(ref complete);
            },
            _ =>
            {
                Interlocked.Increment(ref unexpected);
                return Task.CompletedTask;
            });

        await workDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        releaseComplete.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, complete);
        Assert.Equal(0, unexpected);
    }

    private sealed class TokenWarmupStub : IShellApplication
    {
        private readonly Func<CancellationToken, Task> _warm;

        public TokenWarmupStub(string id, Func<CancellationToken, Task> warm)
        {
            Id = id;
            _warm = warm;
        }

        public string Id { get; }
        public string Title => Id;
        public string Version => "0.0.0";
        public int MinHostAbi => ShellHostAbi.Current;
        public int Calls { get; private set; }
        public void Configure(IServiceCollection services)
            => _ = services;

        public IReadOnlyList<ShellPageRegistration> Pages => [];

        public Task WarmAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            _ = services;
            Calls++;
            return _warm(cancellationToken);
        }
    }
}
