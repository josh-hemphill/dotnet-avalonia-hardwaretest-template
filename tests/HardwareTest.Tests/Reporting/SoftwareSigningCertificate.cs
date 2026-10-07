using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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

    public void Dispose()
    {
        Certificate.Dispose();
        PrivateKey.Dispose();
    }
}
