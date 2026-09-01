using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HardwareTest.Core.Credentials;

namespace HardwareTest.Tests.Credentials;

/// In-memory PIV card that answers SELECT / GET DATA / VERIFY / GENERAL AUTHENTICATE.
internal sealed class FakePivCard : IApduChannel, IDisposable
{
    public const string DefaultPin = "123456";

    private readonly RSA? _rsa;
    private readonly ECDsa? _ecdsa;
    private readonly byte[] _certDer;
    private readonly byte _slot;
    private readonly byte[] _objectId;
    private readonly byte _algId;
    private bool _disposed;

    private FakePivCard(RSA? rsa, ECDsa? ecdsa, byte[] certDer, byte slot, byte[] objectId, byte algId, string pin)
    {
        _rsa = rsa;
        _ecdsa = ecdsa;
        _certDer = certDer;
        _slot = slot;
        _objectId = objectId;
        _algId = algId;
        Pin = pin;
    }

    public string Pin { get; }
    public int PinRetries { get; set; } = 3;
    public bool FailVerifyAsSecurityStatus { get; set; }

    public static FakePivCard CreateRsa2048(string pin = DefaultPin, byte slot = PivApdu.SlotSignature)
    {
        var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=Fake PIV Signature",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return new FakePivCard(rsa, null, cert.RawData, slot, ObjectIdFor(slot), PivApdu.AlgRsa2048, pin);
    }

    public static FakePivCard CreateEccP256(string pin = DefaultPin, byte slot = PivApdu.SlotSignature)
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=Fake PIV ECC", ecdsa, HashAlgorithmName.SHA256);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return new FakePivCard(null, ecdsa, cert.RawData, slot, ObjectIdFor(slot), PivApdu.AlgEccP256, pin);
    }

    public static FakePivCard CreateCardAuthRsa()
        => CreateRsa2048(pin: DefaultPin, slot: PivApdu.SlotCardAuth);

    public byte[] CertDer => _certDer;

    public byte[]? Transmit(byte[] command)
    {
        if (command.Length < 4)
        {
            return [0x6D, 0x00];
        }

        if (command[1] == 0xA4)
        {
            return [0x90, 0x00];
        }

        if (command[1] == 0xCB)
        {
            return command.AsSpan().IndexOf(_objectId) >= 0 ? WrapCertificate() : [0x6A, 0x82];
        }

        if (command[1] == 0x20)
        {
            return VerifyPin(command);
        }

        if (command[1] == 0x87)
        {
            return Sign(command);
        }

        return [0x6D, 0x00];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _rsa?.Dispose();
        _ecdsa?.Dispose();
        _disposed = true;
    }

    private static byte[] ObjectIdFor(byte slot)
        => slot switch
        {
            PivApdu.SlotAuthentication => PivApdu.ObjectAuthentication,
            PivApdu.SlotCardAuth => PivApdu.ObjectCardAuth,
            _ => PivApdu.ObjectSignature,
        };

    private byte[] WrapCertificate()
    {
        var inner = PivApdu.Concat(
            PivApdu.EncodeTlv(0x70, _certDer),
            PivApdu.EncodeTlv(0x71, [0x00]));
        return PivApdu.Concat(PivApdu.EncodeTlv(0x53, inner), [0x90, 0x00]);
    }

    private byte[] VerifyPin(byte[] command)
    {
        if (FailVerifyAsSecurityStatus)
        {
            return [0x69, 0x82];
        }

        if (_slot == PivApdu.SlotCardAuth)
        {
            return [0x90, 0x00];
        }

        var presented = command.Length >= 13 ? command.AsSpan(5, 8) : ReadOnlySpan<byte>.Empty;
        var expected = PivApdu.PadPin(Pin.AsSpan());
        if (presented.SequenceEqual(expected))
        {
            return [0x90, 0x00];
        }

        PinRetries = Math.Max(0, PinRetries - 1);
        return [0x63, (byte)(0xC0 | (PinRetries & 0x0F))];
    }

    private byte[] Sign(byte[] command)
    {
        if (command[3] != _slot || command[2] != _algId)
        {
            return [0x6A, 0x88];
        }

        var body = command.AsSpan(5);
        var wrapped = PivApdu.FindTag(body, 0x7C) ?? body.ToArray();
        var challenge = PivApdu.FindTag(wrapped, 0x81);
        if (challenge is not { Length: > 0 })
        {
            return [0x6A, 0x80];
        }

        byte[] signature;
        if (_rsa is not null)
        {
            var hash = challenge.AsSpan(challenge.Length - 32).ToArray();
            signature = _rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        else
        {
            signature = _ecdsa!.SignHash(challenge, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        var inner = PivApdu.EncodeTlv(0x82, signature);
        return PivApdu.Concat(PivApdu.EncodeTlv(0x7C, inner), [0x90, 0x00]);
    }
}
