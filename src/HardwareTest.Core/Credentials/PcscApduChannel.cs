using PCSC;
using PCSC.Exceptions;

namespace HardwareTest.Core.Credentials;

/// <summary>APDU channel over a connected pcsc-sharp reader.</summary>
internal sealed class PcscApduChannel(ICardReader reader) : IApduChannel
{
    private const int TransmitBufferSize = 4096;
    private const int MaxGetResponseRounds = 16;

    public byte[]? Transmit(byte[] command)
    {
        try
        {
            var first = TransmitOnce(command);
            if (first is not { Length: >= 2 })
            {
                return null;
            }

            if (first[^2] == 0x6C)
            {
                first = TransmitOnce(ApplyLe(command, first[^1]));
                if (first is not { Length: >= 2 })
                {
                    return null;
                }
            }
            else if (first[^2] == 0x67)
            {
                first = TransmitOnce(ApplyLe(command, 0x00));
                if (first is not { Length: >= 2 })
                {
                    return null;
                }
            }

            var payload = new List<byte>(first.Length);
            payload.AddRange(first.AsSpan(0, first.Length - 2).ToArray());
            var sw1 = first[^2];
            var sw2 = first[^1];
            for (var round = 0; round < MaxGetResponseRounds && sw1 == 0x61; round++)
            {
                var more = TransmitOnce([0x00, 0xC0, 0x00, 0x00, sw2]);
                if (more is not { Length: >= 2 })
                {
                    return null;
                }

                payload.AddRange(more.AsSpan(0, more.Length - 2).ToArray());
                sw1 = more[^2];
                sw2 = more[^1];
            }

            payload.Add(sw1);
            payload.Add(sw2);
            return payload.ToArray();
        }
        catch (PCSCException)
        {
            return null;
        }
    }

    /// <summary>Sets or replaces Le for ISO 7816 Case 2/3/4 retry responses.</summary>
    internal static byte[] ApplyLe(ReadOnlySpan<byte> send, byte le)
    {
        if (send.Length <= 4)
        {
            var shortCommand = new byte[5];
            send.CopyTo(shortCommand);
            shortCommand[4] = le;
            return shortCommand;
        }

        if (send.Length == 5)
        {
            var case2 = send.ToArray();
            case2[4] = le;
            return case2;
        }

        var lc = send[4];
        if (lc != 0 && send.Length == 5 + lc)
        {
            var case4 = new byte[send.Length + 1];
            send.CopyTo(case4);
            case4[^1] = le;
            return case4;
        }

        if (lc != 0 && send.Length == 5 + lc + 1)
        {
            var replaced = send.ToArray();
            replaced[^1] = le;
            return replaced;
        }

        var appended = new byte[send.Length + 1];
        send.CopyTo(appended);
        appended[^1] = le;
        return appended;
    }

    private byte[]? TransmitOnce(byte[] command)
    {
        var response = new byte[TransmitBufferSize];
        var received = reader.Transmit(command, response);
        return received >= 2 ? response[..received] : null;
    }
}
