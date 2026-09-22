using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

    private const int NameReadAttempts = 4;

    public static bool IsContactlessReader(string readerName)
    {
        return readerName.Contains("contactless", StringComparison.OrdinalIgnoreCase)
            || readerName.Contains("nfc", StringComparison.OrdinalIgnoreCase)
            || readerName.Contains("picc", StringComparison.OrdinalIgnoreCase)
            || readerName.Contains("cl ", StringComparison.OrdinalIgnoreCase);
    }

    public static (string? Serial, string? DisplayName) TryRead(IApduChannel channel)
    {
        if (!PivApdu.TrySelect(channel))
        {
            return (TryReadUid(channel), null);
        }

        var best = PivIdentityCandidate.None;
        for (var attempt = 0; attempt < NameReadAttempts; attempt++)
        {
            if (attempt > 0 && !PivApdu.TrySelect(channel))
            {
                continue;
            }

            best = PivCertificateName.Prefer(best, TryReadBestCertificateName(channel));
            best = PivCertificateName.Prefer(best, PivCertificateName.Classify(TryReadPrintedName(channel)));
            if (best.Quality >= PivIdentityQuality.DirectoryEmail)
            {
                break;
            }
        }

        return (TryReadUid(channel), best.Value);
    }

    public static string? TryReadSignatureCertificateThumbprint(IApduChannel channel)
    {
        if (!PivApdu.TrySelect(channel))
        {
            return null;
        }

        var der = PivApdu.TryReadCertificateDer(channel, PivApdu.ObjectSignature);
        if (der is null)
        {
            return null;
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadCertificate(der);
            return certificate.Thumbprint;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string? TryReadUid(IApduChannel channel)
    {
        var uid = channel.Transmit(GetUid);
        if (PivApdu.IsSuccess(uid) && uid!.Length > 2)
        {
            return Convert.ToHexString(uid.AsSpan(0, uid.Length - 2));
        }

        return null;
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
