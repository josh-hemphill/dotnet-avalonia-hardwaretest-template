using HardwareTest.Core.Hardware;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class MeasurementAcquisitionTests
{
    [Fact]
    public void Capacity_below_16_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeasurementAcquisition(8));
    }

    [Fact]
    public void Snapshot_and_copy_reflect_ring_buffer_wrap()
    {
        var acq = new MeasurementAcquisition(16);
        acq.Reset("CH1");
        for (var i = 0; i < 20; i++)
        {
            acq.Add(DateTimeOffset.UtcNow, i);
        }

        Assert.Equal(16, acq.Count);
        var snap = acq.Snapshot();
        Assert.Equal(16, snap.Length);
        Assert.Equal(4, snap[0].Value);
        Assert.Equal(19, snap[^1].Value);

        var ys = new double[16];
        acq.CopyYs(ys);
        Assert.Equal(4, ys[0]);
        Assert.Equal(19, ys[^1]);
    }

    [Fact]
    public async Task Acquire_parses_nan_for_non_numeric_response()
    {
        var gate = new VisaSessionGate();
        var session = new MockVisaSession(
            "MOCK::1",
            new Dictionary<string, string> { ["READ?"] = "not-a-number" });
        var traced = new TracingVisaSession(session, gate);
        var acq = new MeasurementAcquisition();

        var samples = new List<MeasurementSample>();
        await foreach (var sample in acq.AcquireAsync(traced, "VDC", 2, 0, "READ?"))
        {
            samples.Add(sample);
        }

        Assert.Equal(2, samples.Count);
        Assert.All(samples, s => Assert.True(double.IsNaN(s.Value)));
    }

    [Fact]
    public async Task Acquire_can_be_cancelled()
    {
        var gate = new VisaSessionGate();
        var session = new TracingVisaSession(new MockVisaSession("MOCK::1"), gate);
        var acq = new MeasurementAcquisition();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(30);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in acq.AcquireAsync(session, "VDC", 1000, 20, "READ?", cts.Token))
            {
            }
        });
    }
}
