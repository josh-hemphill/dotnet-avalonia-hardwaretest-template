using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace HardwareTest.Core.Credentials;

/// Builds RFC 5652 CMS/PKCS#7 detached signatures using the on-card PIV key.
internal static class PivCmsSigner
{
    private static readonly Oid Sha256Oid = new("2.16.840.1.101.3.4.2.1");
    private static readonly Oid Sha384Oid = new("2.16.840.1.101.3.4.2.2");

    /// CMS-signs <paramref name="document"/> with the unlocked PIV slot (PIN already verified).
    public static CredentialSignResult Sign(
        IApduChannel channel,
        byte slot,
        byte algId,
        string algorithm,
        byte[] certificateDer,
        byte[] document,
        DateTimeOffset signingTime)
    {
        if (document.Length == 0)
        {
            return CredentialSignResult.Failed("Nothing to sign.");
        }

        try
        {
            using var cert = X509CertificateLoader.LoadCertificate(certificateDer);
            using var privateKey = CreatePrivateKey(channel, slot, algId, cert);
            if (privateKey is null)
            {
                return CredentialSignResult.Failed("On-card certificate key type is not supported for CMS.");
            }

            var content = new ContentInfo(document);
            var signedCms = new SignedCms(content, detached: true);
            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert, privateKey)
            {
                DigestAlgorithm = algId == PivApdu.AlgEccP384 ? Sha384Oid : Sha256Oid,
                IncludeOption = X509IncludeOption.EndCertOnly,
            };
            signer.SignedAttributes.Add(new Pkcs9SigningTime(signingTime.UtcDateTime));
            signedCms.ComputeSignature(signer, silent: true);
            var cms = signedCms.Encode();
            return CredentialSignResult.Signed(
                cms,
                algorithm,
                certificateDer,
                Convert.ToHexString(SHA256.HashData(certificateDer)));
        }
        catch (CryptographicException ex)
        {
            return CredentialSignResult.Failed("On-card CMS sign failed. " + ex.Message);
        }
    }

    /// Verifies a detached CMS over <paramref name="document"/> using the embedded signer cert.
    public static bool Verify(byte[] document, byte[] cms)
    {
        try
        {
            var signedCms = new SignedCms(new ContentInfo(document), detached: true);
            signedCms.Decode(cms);
            signedCms.CheckSignature(verifySignatureOnly: true);
            return signedCms.SignerInfos.Count > 0;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static AsymmetricAlgorithm? CreatePrivateKey(
        IApduChannel channel,
        byte slot,
        byte algId,
        X509Certificate2 cert)
    {
        using var rsa = cert.GetRSAPublicKey();
        if (rsa is not null)
        {
            return new PivCardRsa(channel, slot, algId, rsa.ExportParameters(false), rsa.KeySize);
        }

        using var ecdsa = cert.GetECDsaPublicKey();
        return ecdsa is null
            ? null
            : new PivCardEcdsa(channel, slot, algId, ecdsa.ExportParameters(false));
    }
}

/// RSA whose SignHash is GENERAL AUTHENTICATE on the PIV card (PKCS#1 v1.5).
internal sealed class PivCardRsa : RSA
{
    private readonly IApduChannel _channel;
    private readonly byte _slot;
    private readonly byte _algId;
    private readonly RSAParameters _publicParameters;

    public PivCardRsa(IApduChannel channel, byte slot, byte algId, RSAParameters publicParameters, int keySize)
    {
        _channel = channel;
        _slot = slot;
        _algId = algId;
        _publicParameters = publicParameters;
        KeySizeValue = keySize;
        LegalKeySizesValue = [new KeySizes(keySize, keySize, 0)];
    }

    public override RSAParameters ExportParameters(bool includePrivateParameters)
    {
        if (includePrivateParameters)
        {
            throw new CryptographicException("PIV private keys cannot be exported.");
        }

        return _publicParameters;
    }

    public override void ImportParameters(RSAParameters parameters)
        => throw new CryptographicException("PIV RSA keys are card-backed.");

    public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        if (padding != RSASignaturePadding.Pkcs1)
        {
            throw new CryptographicException("PIV RSA signatures use PKCS#1 v1.5.");
        }

        if (hashAlgorithm != HashAlgorithmName.SHA256)
        {
            throw new CryptographicException("PIV RSA document signatures use SHA-256.");
        }

        return PivCardSign.Authenticate(_channel, _algId, _slot, PivApdu.Sha256DigestInfo(hash));
    }

    public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        using var rsa = RSA.Create(_publicParameters);
        return rsa.VerifyHash(hash, signature, hashAlgorithm, padding);
    }
}

/// ECDSA whose SignHash is GENERAL AUTHENTICATE on the PIV card.
internal sealed class PivCardEcdsa : ECDsa
{
    private readonly IApduChannel _channel;
    private readonly byte _slot;
    private readonly byte _algId;
    private readonly ECParameters _publicParameters;

    public PivCardEcdsa(IApduChannel channel, byte slot, byte algId, ECParameters publicParameters)
    {
        _channel = channel;
        _slot = slot;
        _algId = algId;
        _publicParameters = publicParameters;
        KeySizeValue = publicParameters.Q.X?.Length == 48 ? 384 : 256;
        LegalKeySizesValue = [new KeySizes(KeySizeValue, KeySizeValue, 0)];
    }

    public override ECParameters ExportParameters(bool includePrivateParameters)
    {
        if (includePrivateParameters)
        {
            throw new CryptographicException("PIV private keys cannot be exported.");
        }

        return _publicParameters;
    }

    public override void ImportParameters(ECParameters parameters)
        => throw new CryptographicException("PIV ECDSA keys are card-backed.");

    public override byte[] SignHash(byte[] hash)
        => PivCardSign.Authenticate(_channel, _algId, _slot, hash);

    public override bool VerifyHash(byte[] hash, byte[] signature)
    {
        using var ecdsa = ECDsa.Create(_publicParameters);
        return ecdsa.VerifyHash(hash, signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
               || ecdsa.VerifyHash(hash, signature, DSASignatureFormat.Rfc3279DerSequence);
    }
}

/// Sends GENERAL AUTHENTICATE and unwraps the 0x82 signature.
internal static class PivCardSign
{
    public static byte[] Authenticate(IApduChannel channel, byte algId, byte slot, ReadOnlySpan<byte> challenge)
    {
        var auth = channel.Transmit(PivApdu.GeneralAuthenticate(algId, slot, challenge));
        if (!PivApdu.IsSuccess(auth))
        {
            throw new CryptographicException("On-card sign failed.");
        }

        var signature = TryReadSignature(PivApdu.Body(auth!));
        if (signature is not { Length: > 0 })
        {
            throw new CryptographicException("Card returned an empty signature.");
        }

        return signature;
    }

    private static byte[]? TryReadSignature(ReadOnlySpan<byte> body)
    {
        var top = PivApdu.FindTag(body, 0x82);
        if (top is { Length: > 0 })
        {
            return top;
        }

        var wrapped = PivApdu.FindTag(body, 0x7C);
        return wrapped is null ? null : PivApdu.FindTag(wrapped, 0x82);
    }
}
