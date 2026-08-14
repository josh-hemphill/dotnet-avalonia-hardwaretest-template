using System.Buffers.Binary;
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
}
