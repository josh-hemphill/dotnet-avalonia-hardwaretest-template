using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using HardwareTest.Core.Time;
using Xunit;

namespace HardwareTest.Tests.Time;

public sealed class UdpNtpTimeSourceTests
{
    [Fact]
    public void Parses_transmit_timestamp_from_ntp_packet()
    {
        var unixSeconds = 1_700_000_000L;
        var packet = new byte[48];
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(40), (uint)(unixSeconds + 2_208_988_800L));
        Assert.True(UdpNtpTimeSource.TryParseTransmitTimestamp(packet, out var utc));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), utc);
    }

    [Fact]
    public void Empty_host_fails_without_throwing()
    {
        var ntp = new UdpNtpTimeSource();
        Assert.False(ntp.TryGetUtcNow(" ", TimeSpan.FromMilliseconds(50), out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Dns_plus_receive_share_a_single_timeout_budget()
    {
        var budget = TimeSpan.FromMilliseconds(200);
        var ntp = new UdpNtpTimeSource((host, timeout, out address, out error) =>
        {
            Assert.True(timeout <= budget, "DNS Wait must receive remaining budget, not a fresh timeout.");
            Thread.Sleep(160);
            address = IPAddress.Parse("192.0.2.1");
            error = null;
            return true;
        });

        var clock = Stopwatch.StartNew();
        var ok = ntp.TryGetUtcNow("ntp.lab.local", budget, out _, out _);
        clock.Stop();

        Assert.False(ok);
        Assert.True(
            clock.Elapsed < TimeSpan.FromMilliseconds(280),
            $"NTP lookup took {clock.Elapsed.TotalMilliseconds:0}ms; stacked DNS+receive timeouts would be ~360ms.");
    }

    [Fact]
    public void Remaining_budget_drops_to_zero_after_elapsed_budget()
    {
        var clock = Stopwatch.StartNew();
        Thread.Sleep(30);
        Assert.False(UdpNtpTimeSource.TryRemaining(clock, TimeSpan.FromMilliseconds(10), out var remaining));
        Assert.Equal(TimeSpan.Zero, remaining);
    }
}
