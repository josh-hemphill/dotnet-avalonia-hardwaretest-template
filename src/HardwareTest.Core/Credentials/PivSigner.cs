using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HardwareTest.Core.Credentials;

/// On-card PIV sign using DIGITAL SIGNATURE (9C), PIV Auth (9A), or Card Auth (9E).
internal static class PivSigner
{
    private static readonly (byte Slot, byte[] ObjectId, bool RequiresPin)[] SlotOrder =
    [
        (PivApdu.SlotSignature, PivApdu.ObjectSignature, true),
        (PivApdu.SlotAuthentication, PivApdu.ObjectAuthentication, true),
        (PivApdu.SlotCardAuth, PivApdu.ObjectCardAuth, false),
    ];

    /// Signs SHA-256(payload) on the card. PIN is never stored; the caller must clear it.
    public static CredentialSignResult Sign(IApduChannel channel, byte[] payload, string? pin)
    {
        if (payload.Length == 0)
        {
            return CredentialSignResult.Failed("Nothing to sign.");
        }

        var select = channel.Transmit(PivApdu.SelectPiv);
        if (!PivApdu.IsSuccess(select))
        {
            return CredentialSignResult.Failed("PIV applet not found on this card.");
        }

        foreach (var (slot, objectId, requiresPin) in SlotOrder)
        {
            var certDer = TryReadCertificate(channel, objectId);
            if (certDer is null)
            {
                continue;
            }

            if (!TryDescribeKey(certDer, out var algorithm, out var algId, out var hashName))
            {
                continue;
            }

            if (requiresPin && string.IsNullOrEmpty(pin))
            {
                return CredentialSignResult.NeedPin("Enter badge PIN to sign.");
            }

            if (requiresPin)
            {
                var verify = channel.Transmit(PivApdu.VerifyPin(pin.AsSpan()));
                var retries = PivApdu.PinRetriesRemaining(verify);
                if (!PivApdu.IsSuccess(verify))
                {
                    if (retries is int left)
                    {
                        return CredentialSignResult.Failed($"Incorrect PIN ({left} retries left).", left);
                    }

                    if (PivApdu.IsPinRequired(verify))
                    {
                        return CredentialSignResult.Failed(
                            "PIN not accepted over this transport. Insert the chip (contact) to sign.");
                    }

                    return CredentialSignResult.Failed("Badge PIN verify failed.");
                }
            }

            var hash = SHA256.HashData(payload);
            var challenge = algId is PivApdu.AlgEccP256 or PivApdu.AlgEccP384
                ? hash
                : PivApdu.Sha256DigestInfo(hash);
            if (algId == PivApdu.AlgEccP384)
            {
                challenge = SHA384.HashData(payload);
            }

            var auth = channel.Transmit(PivApdu.GeneralAuthenticate(algId, slot, challenge));
            if (!PivApdu.IsSuccess(auth))
            {
                return CredentialSignResult.Failed("On-card sign failed.");
            }

            var signature = TryReadSignature(PivApdu.Body(auth!));
            if (signature is not { Length: > 0 })
            {
                return CredentialSignResult.Failed("Card returned an empty signature.");
            }

            if (!Verify(payload, signature, certDer, algorithm, hashName))
            {
                return CredentialSignResult.Failed("Card signature did not verify with the on-card certificate.");
            }

            return CredentialSignResult.Signed(
                signature,
                algorithm,
                certDer,
                Convert.ToHexString(SHA256.HashData(certDer)));
        }

        return CredentialSignResult.Failed("No PIV signing certificate on this card.");
    }

    public static bool Verify(
        byte[] payload,
        byte[] signature,
        byte[] certificateDer,
        string algorithm,
        HashAlgorithmName? hashName = null)
    {
        try
        {
            using var cert = X509CertificateLoader.LoadCertificate(certificateDer);
            if (algorithm == AttestationAlgorithm.PivEcdsaSha384
                || algorithm == AttestationAlgorithm.PivEcdsaSha256)
            {
                using var ecdsa = cert.GetECDsaPublicKey();
                if (ecdsa is null)
                {
                    return false;
                }

                var hash = hashName
                    ?? (algorithm == AttestationAlgorithm.PivEcdsaSha384
                        ? HashAlgorithmName.SHA384
                        : HashAlgorithmName.SHA256);
                return ecdsa.VerifyData(payload, signature, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                    || ecdsa.VerifyData(payload, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
            }

            using var rsa = cert.GetRSAPublicKey();
            return rsa is not null
                && rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
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

    private static byte[]? TryReadCertificate(IApduChannel channel, byte[] objectId)
    {
        var response = channel.Transmit(PivApdu.GetData(objectId));
        if (!PivApdu.IsSuccess(response))
        {
            return null;
        }

        return PivApdu.TryExtractCertificateDer(PivApdu.Body(response!));
    }

    private static bool TryDescribeKey(
        byte[] certificateDer,
        out string algorithm,
        out byte algId,
        out HashAlgorithmName hashName)
    {
        algorithm = string.Empty;
        algId = 0;
        hashName = HashAlgorithmName.SHA256;
        try
        {
            using var cert = X509CertificateLoader.LoadCertificate(certificateDer);
            using var rsa = cert.GetRSAPublicKey();
            if (rsa is not null)
            {
                algorithm = AttestationAlgorithm.PivRsaPkcs1Sha256;
                algId = rsa.KeySize switch
                {
                    1024 => PivApdu.AlgRsa1024,
                    2048 => PivApdu.AlgRsa2048,
                    3072 => PivApdu.AlgRsa3072,
                    4096 => PivApdu.AlgRsa4096,
                    _ => (byte)0,
                };
                return algId != 0;
            }

            using var ecdsa = cert.GetECDsaPublicKey();
            if (ecdsa is null)
            {
                return false;
            }

            var size = ecdsa.KeySize;
            if (size == 384)
            {
                algorithm = AttestationAlgorithm.PivEcdsaSha384;
                algId = PivApdu.AlgEccP384;
                hashName = HashAlgorithmName.SHA384;
                return true;
            }

            if (size == 256)
            {
                algorithm = AttestationAlgorithm.PivEcdsaSha256;
                algId = PivApdu.AlgEccP256;
                hashName = HashAlgorithmName.SHA256;
                return true;
            }

            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
