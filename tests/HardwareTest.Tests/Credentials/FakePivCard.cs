using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HardwareTest.Core.Credentials;

namespace HardwareTest.Tests.Credentials;

/// In-memory PIV public identity card that answers SELECT and GET DATA.
internal sealed class FakePivCard : IApduChannel, IDisposable
{
    public const byte SlotAuthentication = 0x9A;
    public const byte SlotSignature = 0x9C;
    public const byte SlotKeyManagement = 0x9D;
    public const byte SlotCardAuth = 0x9E;

    private readonly X509Certificate2 _certificate;
    private readonly byte[] _objectId;

    private FakePivCard(byte[] certDer, byte[] objectId)
    {
        _certificate = X509CertificateLoader.LoadCertificate(certDer);
        _objectId = objectId;
    }

    public byte[]? Uid { get; set; }
    public byte[]? PrintedInformation { get; set; }
    public bool OmitCertificate { get; set; }
    public bool GzipCertificate { get; set; }
    public int RemainingCertificateFailures { get; set; }

    public static FakePivCard CreateRsa2048(
        byte slot = SlotSignature,
        string subject = "CN=Fake PIV Signature",
        string? emailSan = null,
        string? upnSan = null)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            subject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        AddSan(req, emailSan, upnSan);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return new FakePivCard(cert.RawData, ObjectIdFor(slot));
    }

    public byte[] CertDer => _certificate.RawData;

    public byte[]? Transmit(byte[] command)
    {
        if (command.Length < 4)
        {
            return [0x6D, 0x00];
        }

        if (command[0] == 0xFF && command[1] == 0xCA)
        {
            return Uid is { Length: > 0 } ? PivApdu.Concat(Uid, [0x90, 0x00]) : [0x6A, 0x82];
        }

        if (command[1] == 0xA4)
        {
            return [0x90, 0x00];
        }

        if (command[1] == 0xCB)
        {
            if (PrintedInformation is { Length: > 0 }
                && command.AsSpan().IndexOf(PivApdu.ObjectPrintedInformation) >= 0)
            {
                return PivApdu.Concat(PivApdu.EncodeTlv(0x53, PrintedInformation), [0x90, 0x00]);
            }

            if (RemainingCertificateFailures > 0)
            {
                RemainingCertificateFailures--;
                return [0x6A, 0x82];
            }

            return !OmitCertificate && command.AsSpan().IndexOf(_objectId) >= 0
                ? WrapCertificate()
                : [0x6A, 0x82];
        }

        return [0x6D, 0x00];
    }

    public void Dispose() => _certificate.Dispose();

    private static byte[] ObjectIdFor(byte slot)
        => slot switch
        {
            SlotAuthentication => PivApdu.ObjectAuthentication,
            SlotKeyManagement => PivApdu.ObjectKeyManagement,
            SlotCardAuth => PivApdu.ObjectCardAuth,
            _ => PivApdu.ObjectSignature,
        };

    private static void AddSan(CertificateRequest request, string? emailSan, string? upnSan)
    {
        if (string.IsNullOrWhiteSpace(emailSan) && string.IsNullOrWhiteSpace(upnSan))
        {
            return;
        }

        var san = new SubjectAlternativeNameBuilder();
        if (!string.IsNullOrWhiteSpace(emailSan))
        {
            san.AddEmailAddress(emailSan.Trim());
        }

        if (!string.IsNullOrWhiteSpace(upnSan))
        {
            san.AddUserPrincipalName(upnSan.Trim());
        }

        request.CertificateExtensions.Add(san.Build());
    }

    private byte[] WrapCertificate()
    {
        var payload = CertDer;
        byte info = 0x00;
        if (GzipCertificate)
        {
            payload = Gzip(payload);
            info = 0x01;
        }

        var inner = PivApdu.Concat(
            PivApdu.EncodeTlv(0x70, payload),
            PivApdu.EncodeTlv(0x71, [info]));
        return PivApdu.Concat(PivApdu.EncodeTlv(0x53, inner), [0x90, 0x00]);
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }
}

/// Routes public GET DATA to the slot that owns the certificate object.
internal sealed class CompositePivCard : IApduChannel, IDisposable
{
    private readonly FakePivCard[] _slots;

    public CompositePivCard(params FakePivCard[] slots) => _slots = slots;

    public byte[]? Transmit(byte[] command)
    {
        if (command.Length >= 2 && command[0] == 0xFF && command[1] == 0xCA)
        {
            foreach (var slot in _slots)
            {
                var uid = slot.Transmit(command);
                if (PivApdu.IsSuccess(uid))
                {
                    return uid;
                }
            }

            return [0x6A, 0x82];
        }

        if (command.Length >= 2 && command[1] == 0xA4)
        {
            return [0x90, 0x00];
        }

        byte[]? last = null;
        foreach (var slot in _slots)
        {
            var response = slot.Transmit(command);
            last = response;
            if (PivApdu.IsSuccess(response))
            {
                return response;
            }
        }

        return last ?? [0x6A, 0x88];
    }

    public void Dispose()
    {
        foreach (var slot in _slots)
        {
            slot.Dispose();
        }
    }
}
