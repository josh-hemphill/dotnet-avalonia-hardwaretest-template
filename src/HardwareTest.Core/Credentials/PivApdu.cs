namespace HardwareTest.Core.Credentials;

/// PIV APDU builders and BER-TLV helpers (NIST SP 800-73).
internal static class PivApdu
{
    public const byte SlotAuthentication = 0x9A;
    public const byte SlotSignature = 0x9C;
    public const byte SlotCardAuth = 0x9E;

    public const byte AlgRsa1024 = 0x06;
    public const byte AlgRsa2048 = 0x07;
    public const byte AlgEccP256 = 0x11;
    public const byte AlgEccP384 = 0x14;
    public const byte AlgRsa3072 = 0x27;
    public const byte AlgRsa4096 = 0x28;

    public static readonly byte[] SelectPiv =
        [0x00, 0xA4, 0x04, 0x00, 0x0B, 0xA0, 0x00, 0x00, 0x03, 0x08, 0x00, 0x00, 0x10, 0x00, 0x01, 0x00];

    public static readonly byte[] ObjectAuthentication = [0x5F, 0xC1, 0x05];
    public static readonly byte[] ObjectSignature = [0x5F, 0xC1, 0x0A];
    public static readonly byte[] ObjectCardAuth = [0x5F, 0xC1, 0x01];

    /// SHA-256 DigestInfo prefix (RFC 8017).
    public static ReadOnlySpan<byte> Sha256DigestInfoPrefix
        => [0x30, 0x31, 0x30, 0x0D, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01, 0x05, 0x00, 0x04, 0x20];

    public static byte[] GetData(ReadOnlySpan<byte> objectId)
    {
        var data = new byte[2 + 1 + objectId.Length];
        data[0] = 0x5C;
        data[1] = (byte)objectId.Length;
        objectId.CopyTo(data.AsSpan(2));
        return [0x00, 0xCB, 0x3F, 0xFF, (byte)data.Length, .. data];
    }

    public static byte[] VerifyPin(ReadOnlySpan<char> pin)
        => [0x00, 0x20, 0x00, 0x80, 0x08, .. PadPin(pin)];

    public static byte[] PadPin(ReadOnlySpan<char> pin)
    {
        var padded = new byte[8];
        Array.Fill(padded, (byte)0xFF);
        var n = Math.Min(pin.Length, 8);
        for (var i = 0; i < n; i++)
        {
            padded[i] = (byte)pin[i];
        }

        return padded;
    }

    public static byte[] GeneralAuthenticate(byte algorithm, byte slot, ReadOnlySpan<byte> challenge)
    {
        var inner = Concat(EncodeTlv(0x82, []), EncodeTlv(0x81, challenge));
        var body = EncodeTlv(0x7C, inner);
        return [0x00, 0x87, algorithm, slot, (byte)body.Length, .. body];
    }

    public static byte[] Sha256DigestInfo(ReadOnlySpan<byte> sha256)
    {
        var info = new byte[Sha256DigestInfoPrefix.Length + 32];
        Sha256DigestInfoPrefix.CopyTo(info);
        sha256[..32].CopyTo(info.AsSpan(Sha256DigestInfoPrefix.Length));
        return info;
    }

    public static bool IsSuccess(byte[]? response)
        => response is { Length: >= 2 } && response[^2] == 0x90 && response[^1] == 0x00;

    public static bool IsPinRequired(byte[]? response)
        => response is { Length: >= 2 } && response[^2] == 0x69 && response[^1] is 0x82 or 0x83;

    public static int? PinRetriesRemaining(byte[]? response)
    {
        if (response is not { Length: >= 2 } || response[^2] != 0x63)
        {
            return null;
        }

        return response[^1] & 0x0F;
    }

    public static ReadOnlySpan<byte> Body(byte[] response)
        => response.AsSpan(0, response.Length - 2);

    public static byte[] EncodeTlv(byte tag, ReadOnlySpan<byte> value)
    {
        var length = EncodeLength(value.Length);
        var encoded = new byte[1 + length.Length + value.Length];
        encoded[0] = tag;
        length.CopyTo(encoded.AsSpan(1));
        value.CopyTo(encoded.AsSpan(1 + length.Length));
        return encoded;
    }

    public static bool TryReadTlv(ReadOnlySpan<byte> data, out byte tag, out ReadOnlySpan<byte> value, out int consumed)
    {
        tag = 0;
        value = default;
        consumed = 0;
        if (data.Length < 2)
        {
            return false;
        }

        tag = data[0];
        var offset = 1;
        if (!TryReadLength(data[offset..], out var length, out var lengthSize))
        {
            return false;
        }

        offset += lengthSize;
        if (offset + length > data.Length)
        {
            return false;
        }

        value = data.Slice(offset, length);
        consumed = offset + length;
        return true;
    }

    public static byte[]? FindTag(ReadOnlySpan<byte> data, byte tag)
    {
        var remaining = data;
        while (TryReadTlv(remaining, out var found, out var value, out var consumed))
        {
            if (found == tag)
            {
                return value.ToArray();
            }

            remaining = remaining[consumed..];
        }

        return null;
    }

    /// Unwraps a PIV GET DATA certificate object (53 / 70 / raw DER).
    public static byte[]? TryExtractCertificateDer(ReadOnlySpan<byte> responseBody)
    {
        if (responseBody.Length >= 2 && responseBody[0] == 0x30)
        {
            return responseBody.ToArray();
        }

        var container = responseBody;
        if (TryReadTlv(responseBody, out var tag, out var value, out _) && tag == 0x53)
        {
            container = value;
        }

        var cert = FindTag(container, 0x70);
        if (cert is { Length: > 0 })
        {
            return cert;
        }

        return responseBody.Length >= 2 && responseBody[0] == 0x70
            ? FindTag(responseBody, 0x70)
            : null;
    }

    public static byte[] Concat(params byte[][] parts)
    {
        var length = parts.Sum(p => p.Length);
        var buffer = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(buffer.AsSpan(offset));
            offset += part.Length;
        }

        return buffer;
    }

    private static byte[] EncodeLength(int length)
    {
        if (length < 0x80)
        {
            return [(byte)length];
        }

        if (length <= 0xFF)
        {
            return [0x81, (byte)length];
        }

        return [0x82, (byte)(length >> 8), (byte)length];
    }

    private static bool TryReadLength(ReadOnlySpan<byte> data, out int length, out int size)
    {
        length = 0;
        size = 0;
        if (data.Length < 1)
        {
            return false;
        }

        var first = data[0];
        if (first < 0x80)
        {
            length = first;
            size = 1;
            return true;
        }

        var count = first & 0x7F;
        if (count is 0 or > 2 || data.Length < 1 + count)
        {
            return false;
        }

        length = 0;
        for (var i = 0; i < count; i++)
        {
            length = (length << 8) | data[1 + i];
        }

        size = 1 + count;
        return true;
    }
}
