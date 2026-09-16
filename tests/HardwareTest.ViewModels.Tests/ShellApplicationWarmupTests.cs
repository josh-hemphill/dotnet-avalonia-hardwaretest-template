using HardwareTest.Shell;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ShellApplicationWarmupTests
{
    [Fact]
    public async Task RunAsync_continues_after_a_faulted_app()
    {
        var faulted = new WarmupStub("vendor.fault", () => throw new InvalidOperationException("boom"));
        var later = new WarmupStub("vendor.ok", () => Task.CompletedTask);
        var faults = new List<string>();

        await ShellApplicationWarmup.RunAsync(
            [faulted, later],
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None,
            (app, ex) => faults.Add($"{app.Id}:{ex.Message}"));

        Assert.Equal(["vendor.fault:boom"], faults);
        Assert.Equal(1, faulted.Calls);
        Assert.Equal(1, later.Calls);
    }

    [Fact]
    public async Task RunAsync_continues_when_onFault_throws()
    {
        var faulted = new WarmupStub("vendor.fault", () => throw new InvalidOperationException("boom"));
        var later = new WarmupStub("vendor.ok", () => Task.CompletedTask);

        await ShellApplicationWarmup.RunAsync(
            [faulted, later],
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None,
            (_, _) => throw new InvalidOperationException("callback"));

        Assert.Equal(1, faulted.Calls);
        Assert.Equal(1, later.Calls);
    }

    [Fact]
    public async Task RunAsync_rethrows_cancellation_from_WarmAsync()
    {
        using var cts = new CancellationTokenSource();
        var later = new WarmupStub("vendor.later", () => Task.CompletedTask);
        var cancelling = new WarmupStub(
            "vendor.cancel",
            async () =>
            {
                await cts.CancelAsync();
                cts.Token.ThrowIfCancellationRequested();
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ShellApplicationWarmup.RunAsync(
                [cancelling, later],
                new ServiceCollection().BuildServiceProvider(),
                cts.Token));
        Assert.Equal(1, cancelling.Calls);
        Assert.Equal(0, later.Calls);
    }

    [Fact]
    public async Task RunAsync_stops_when_token_is_already_cancelled()
    {
        var later = new WarmupStub("vendor.later", () => Task.CompletedTask);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ShellApplicationWarmup.RunAsync(
                [later],
                new ServiceCollection().BuildServiceProvider(),
                new CancellationToken(canceled: true)));
        Assert.Equal(0, later.Calls);
    }

    private sealed class WarmupStub : IShellApplication
    {
        private readonly Func<Task> _warm;

        public WarmupStub(string id, Func<Task> warm)
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
            _ = cancellationToken;
            Calls++;
            return _warm();
        }
    }
}
