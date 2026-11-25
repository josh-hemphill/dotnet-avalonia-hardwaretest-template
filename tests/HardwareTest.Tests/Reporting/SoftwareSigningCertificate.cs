using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HardwareTest.Core.Reporting;

namespace HardwareTest.Tests.Reporting;

internal sealed class SoftwareSigningCertificate : IDisposable
{
    private SoftwareSigningCertificate(AsymmetricAlgorithm key)
    {
        PrivateKey = key;
        Certificate = CreateCertificate(key);
    }

    public AsymmetricAlgorithm PrivateKey { get; }
    public X509Certificate2 Certificate { get; }

    public static SoftwareSigningCertificate CreateRsa() => new(RSA.Create(2048));
    public static SoftwareSigningCertificate CreateP256() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));
    public static SoftwareSigningCertificate CreateP384() => new(ECDsa.Create(ECCurve.NamedCurves.nistP384));

    // Identical subjects deliberately ensure display names cannot authorize another certificate.
    public static X509Certificate2 CreateCertificate(AsymmetricAlgorithm key)
    {
        var request = key switch
        {
            RSA rsa => new CertificateRequest("CN=Software Certifier", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            ECDsa ecdsa => new CertificateRequest("CN=Software Certifier", ecdsa,
                ecdsa.KeySize >= 384 ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA256),
            _ => throw new ArgumentException("Unsupported software signing key.", nameof(key)),
        };
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    internal static bool TrySign(
        byte[] pdf, X509Certificate2 certificate, AsymmetricAlgorithm key, string displayName, DateTimeOffset signingTime,
        out byte[] signedPdf, out byte[] cms, out string? error)
    {
        var digest = key is ECDsa { KeySize: >= 384 } ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA256;
        return PadesSignature.TrySign(pdf, certificate, digest, hash => key switch
        {
            RSA rsa => rsa.SignHash(hash, digest, RSASignaturePadding.Pkcs1),
            ECDsa ec => ec.SignHash(hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _ => throw new NotSupportedException("Expected an RSA or ECDSA key."),
        }, displayName, signingTime, out signedPdf, out cms, out error);
    }

    public void Dispose()
    {
        Certificate.Dispose();
        PrivateKey.Dispose();
    }
}
