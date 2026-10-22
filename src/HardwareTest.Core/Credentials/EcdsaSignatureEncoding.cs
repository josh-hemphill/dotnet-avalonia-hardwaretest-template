using System.Formats.Asn1;

namespace HardwareTest.Core.Credentials;

internal static class EcdsaSignatureEncoding
{
    public static byte[] ToDer(ReadOnlySpan<byte> raw)
    {
        if (raw.IsEmpty || raw.Length % 2 != 0)
            throw new ArgumentException("Expected two fixed-width ECDSA components.", nameof(raw));
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteIntegerUnsigned(MinimalUnsigned(raw[..(raw.Length / 2)]));
        writer.WriteIntegerUnsigned(MinimalUnsigned(raw[(raw.Length / 2)..]));
        writer.PopSequence();
        return writer.Encode();
    }

    private static ReadOnlySpan<byte> MinimalUnsigned(ReadOnlySpan<byte> value)
    {
        // Native signatures pad each component to the curve width. ASN.1 expects
        // minimal unsigned input and adds any required positive sign byte itself.
        while (value.Length > 1 && value[0] == 0) value = value[1..];
        return value;
    }
}
