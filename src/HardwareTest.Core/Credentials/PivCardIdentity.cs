using System.Text;

namespace HardwareTest.Core.Credentials;

/// PIV SELECT / GET DATA helpers plus PC/SC Get UID.
internal static class PivCardIdentity
{
    private static readonly byte[] GetUid = [0xFF, 0xCA, 0x00, 0x00, 0x00];

    private static readonly byte[][] IdentityCertificateObjects =
    [
        PivApdu.ObjectAuthentication,
        PivApdu.ObjectSignature,
        PivApdu.ObjectKeyManagement,
        PivApdu.ObjectCardAuth,
    ];

    public static bool IsContactlessReader(string readerName)
    {
        return readerName.Contains("contactless", StringComparison.OrdinalIgnoreCase)
            || readerName.Contains("nfc", StringComparison.OrdinalIgnoreCase)
            || readerName.Contains("picc", StringComparison.OrdinalIgnoreCase)
            || readerName.Contains("cl ", StringComparison.OrdinalIgnoreCase);
    }

    public static (string? Serial, string? DisplayName) TryRead(nint card, int protocol)
        => TryRead(new PcscApduChannel(card, protocol));

    public static (string? Serial, string? DisplayName) TryRead(IApduChannel channel)
    {
        string? serial = null;
        var uid = channel.Transmit(GetUid);
        if (PivApdu.IsSuccess(uid) && uid!.Length > 2)
        {
            serial = Convert.ToHexString(uid.AsSpan(0, uid.Length - 2));
        }

        if (!PivApdu.TrySelect(channel))
        {
            return (serial, null);
        }

        var best = TryReadBestCertificateName(channel);
        best = PivCertificateName.Prefer(best, PivCertificateName.Classify(TryReadPrintedName(channel)));
        return (serial, best.Value);
    }

    private static PivIdentityCandidate TryReadBestCertificateName(IApduChannel channel)
    {
        var best = PivIdentityCandidate.None;
        foreach (var objectId in IdentityCertificateObjects)
        {
            var der = PivApdu.TryReadCertificateDer(channel, objectId);
            if (der is null)
            {
                continue;
            }

            best = PivCertificateName.Prefer(best, PivCertificateName.TryCandidate(der));
        }

        return best;
    }

    private static string? TryReadPrintedName(IApduChannel channel)
    {
        var printed = channel.Transmit(PivApdu.GetData(PivApdu.ObjectPrintedInformation));
        if (!PivApdu.IsSuccess(printed))
        {
            return null;
        }

        return TryParsePrintedName(printed!);
    }

    private static string? TryParsePrintedName(byte[] response)
    {
        var body = PivApdu.Body(response);
        var container = body;
        if (PivApdu.TryReadTlv(body, out var tag, out var value, out _) && tag == 0x53)
        {
            container = value;
        }

        var nameBytes = PivApdu.FindTag(container, PivApdu.PrintedNameTag);
        if (nameBytes is not { Length: > 0 })
        {
            return null;
        }

        var cleaned = new string(Encoding.UTF8.GetString(nameBytes)
            .Where(c => !char.IsControl(c) && c != '\0')
            .ToArray()).Trim();
        if (!PivCertificateName.IsUsefulPersonName(cleaned))
        {
            return null;
        }

        return cleaned.Length <= 64 ? cleaned : cleaned[..64];
    }
}
