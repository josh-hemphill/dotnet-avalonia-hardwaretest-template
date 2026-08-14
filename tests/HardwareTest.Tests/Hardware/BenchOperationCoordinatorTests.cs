using HardwareTest.Core.Hardware;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class BenchOperationCoordinatorTests
{
    [Fact]
    public void TryEnter_succeeds_when_idle()
    {
        var bench = new BenchOperationCoordinator();
        Assert.True(bench.TryEnter(BenchOperation.IdQuery, out var lease, out var status));
        Assert.NotNull(lease);
        Assert.Equal(string.Empty, status);
        Assert.Equal(BenchOperation.IdQuery, bench.Current);
        lease!.Dispose();
        Assert.Null(bench.Current);
    }

    [Theory]
    [InlineData(BenchOperation.IdQuery, BenchOperation.ModeSwap, "Cannot switch VISA mode while an Instruments query is in progress.")]
    [InlineData(BenchOperation.Run, BenchOperation.IdQuery, "Cannot query *IDN? while a run is in progress.")]
    [InlineData(BenchOperation.ModeSwap, BenchOperation.Run, "Cannot start a run while a VISA mode switch is in progress.")]
    public void TryEnter_fails_closed_while_another_operation_is_held(
        BenchOperation held,
        BenchOperation requested,
        string expected)
    {
        var bench = new BenchOperationCoordinator();
        Assert.True(bench.TryEnter(held, out var lease, out _));
        using (lease)
        {
            Assert.False(bench.TryEnter(requested, out var second, out var status));
            Assert.Null(second);
            Assert.Equal(expected, status);
            Assert.Equal(held, bench.Current);
        }
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var bench = new BenchOperationCoordinator();
        Assert.True(bench.TryEnter(BenchOperation.Run, out var lease, out _));
        lease!.Dispose();
        lease.Dispose();
        Assert.Null(bench.Current);
        Assert.True(bench.TryEnter(BenchOperation.ModeSwap, out var next, out _));
        next!.Dispose();
    }
}
