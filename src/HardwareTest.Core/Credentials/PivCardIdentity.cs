using System.Text;

namespace HardwareTest.Core.Credentials;

/// PIV SELECT / GET DATA helpers plus PC/SC Get UID.
internal static class PivCardIdentity
{
    private static readonly byte[] SelectPiv =
        [0x00, 0xA4, 0x04, 0x00, 0x0B, 0xA0, 0x00, 0x00, 0x03, 0x08, 0x00, 0x00, 0x10, 0x00, 0x01, 0x00];

    private static readonly byte[] GetUid = [0xFF, 0xCA, 0x00, 0x00, 0x00];

    private static readonly byte[] GetPrinted =
        [0x00, 0xCB, 0x3F, 0xFF, 0x05, 0x5C, 0x03, 0x5F, 0xC1, 0x09];

    public static bool IsContactlessReader(string readerName)
    {
        return readerName.Contains("contactless", StringComparison.OrdinalIgnoreCase)
            || readerName.Contains("nfc", StringComparison.OrdinalIgnoreCase)
            || readerName.Contains("picc", StringComparison.OrdinalIgnoreCase)
            || readerName.Contains("cl ", StringComparison.OrdinalIgnoreCase);
    }

    public static (string? Serial, string? DisplayName) TryRead(nint card, int protocol)
    {
        string? serial = null;
        string? name = null;

        var uid = PcscNative.Transmit(card, protocol, GetUid);
        if (IsSuccess(uid) && uid!.Length > 2)
        {
            serial = Convert.ToHexString(uid.AsSpan(0, uid.Length - 2));
        }

        var select = PcscNative.Transmit(card, protocol, SelectPiv);
        if (IsSuccess(select))
        {
            var printed = PcscNative.Transmit(card, protocol, GetPrinted);
            if (IsSuccess(printed))
            {
                name = TryParsePrintedName(printed!);
            }
        }

        return (serial, name);
    }

    private static bool IsSuccess(byte[]? response)
        => response is { Length: >= 2 }
           && response[^2] == 0x90
           && response[^1] == 0x00;

    private static string? TryParsePrintedName(byte[] response)
    {
        var body = response.AsSpan(0, response.Length - 2);
        var text = Encoding.ASCII.GetString(body);
        var cleaned = new string(text.Where(c => !char.IsControl(c) && c != '\0').ToArray()).Trim();
        if (cleaned.Length < 2)
        {
            return null;
        }

        return cleaned.Length <= 64 ? cleaned : cleaned[..64];
    }
}
