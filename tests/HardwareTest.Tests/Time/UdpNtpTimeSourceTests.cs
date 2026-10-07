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
        var elapsed = TimeSpan.Zero;
        var dnsCalls = 0;
        var ntp = new UdpNtpTimeSource((host, timeout, out address, out error) =>
        {
            dnsCalls++;
            Assert.Equal(budget, timeout);
            elapsed = budget;
            // A null sentinel would fail if any socket/address work were attempted.
            address = null!;
            error = null;
            return true;
        }, _ => elapsed);

        Assert.False(ntp.TryGetUtcNow("ntp.lab.local", budget, out var utc, out var error));
        Assert.Equal(1, dnsCalls);
        Assert.Equal(default, utc);
        Assert.Equal("NTP lookup timed out.", error);
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
