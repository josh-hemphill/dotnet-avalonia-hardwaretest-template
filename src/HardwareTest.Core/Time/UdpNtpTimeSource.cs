using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace HardwareTest.Core.Time;

/// UDP/123 NTP query with a hard receive timeout. Never used from Safety Stop / worker kill.
public sealed class UdpNtpTimeSource : INtpTimeSource
{
    private const int NtpPort = 123;
    private const int PacketSize = 48;
    private const int TransmitTimestampOffset = 40;
    private const long NtpEpochOffsetSeconds = 2208988800L;

    public bool TryGetUtcNow(string host, TimeSpan timeout, out DateTimeOffset utc, out string? error)
    {
        utc = default;
        error = null;
        if (string.IsNullOrWhiteSpace(host))
        {
            error = "NTP host is empty.";
            return false;
        }

        timeout = ClockSkew.NtpTimeout(timeout);
        try
        {
            if (!TryResolve(host.Trim(), timeout, out var address, out error))
            {
                return false;
            }

            using var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
            {
                SendTimeout = (int)timeout.TotalMilliseconds,
                ReceiveTimeout = (int)timeout.TotalMilliseconds,
            };
            var endpoint = new IPEndPoint(address, NtpPort);
            var request = new byte[PacketSize];
            request[0] = 0x23; // LI=0, VN=4, Mode=3 (client)
            socket.SendTo(request, endpoint);

            var buffer = new byte[PacketSize];
            EndPoint remote = new IPEndPoint(address.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any
                : IPAddress.Any, 0);
            var received = socket.ReceiveFrom(buffer, ref remote);
            if (received < PacketSize)
            {
                error = "NTP reply was truncated.";
                return false;
            }

            if (!TryParseTransmitTimestamp(buffer, out utc))
            {
                error = "NTP transmit timestamp was invalid.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            utc = default;
            return false;
        }
    }

    internal static bool TryParseTransmitTimestamp(ReadOnlySpan<byte> packet, out DateTimeOffset utc)
    {
        utc = default;
        if (packet.Length < TransmitTimestampOffset + 8)
        {
            return false;
        }

        var seconds = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(TransmitTimestampOffset, 4));
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(TransmitTimestampOffset + 4, 4));
        var unixSeconds = (long)seconds - NtpEpochOffsetSeconds;
        if (unixSeconds < DateTimeOffset.MinValue.ToUnixTimeSeconds()
            || unixSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        var extraMs = fraction / (double)uint.MaxValue * 1000.0;
        utc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).AddMilliseconds(extraMs);
        return true;
    }

    private static bool TryResolve(string host, TimeSpan timeout, out IPAddress address, out string? error)
    {
        address = IPAddress.None;
        error = null;
        if (IPAddress.TryParse(host, out var parsed))
        {
            address = parsed;
            return true;
        }

        try
        {
            var task = Dns.GetHostAddressesAsync(host);
            if (!task.Wait(timeout))
            {
                error = "NTP DNS lookup timed out.";
                return false;
            }

            var found = task.Result.FirstOrDefault(a =>
                a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);
            if (found is null)
            {
                error = "NTP host has no IP address.";
                return false;
            }

            address = found;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
