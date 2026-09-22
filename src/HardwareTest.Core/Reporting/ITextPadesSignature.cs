using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Bouncycastleconnector;
using iText.Commons.Bouncycastle.Cert;
using iText.Kernel.Pdf;
using iText.Signatures;

namespace HardwareTest.Core.Reporting;

/// PAdES Baseline-B creation and verification delegated to iText.
internal static class ITextPadesSignature
{
    private const int EstimatedSignatureSize = 32 * 1024;
    private const string FieldName = "HardwareTestCertification";

    public static bool TrySign(
        byte[] pdf,
        X509Certificate2 certificate,
        AsymmetricAlgorithm privateKey,
        string displayName,
        DateTimeOffset signingTime,
        out byte[] signedPdf,
        out byte[] cms,
        out string? error)
    {
        signedPdf = [];
        cms = [];
        try
        {
            using var input = new MemoryStream(pdf, writable: false);
            using var reader = new PdfReader(input);
            using var output = new MemoryStream();
            var signerProperties = new SignerProperties()
                .SetFieldName(FieldName)
                .SetClaimedSignDate(signingTime.UtcDateTime)
                .SetReason("Hardware test report certification")
                .SetContact(displayName)
                .SetSignatureCreator("HardwareTest");
            var chain = ConvertCertificate(certificate);
            var signer = new PdfPadesSigner(reader, output)
                .SetEstimatedSize(EstimatedSignatureSize);
            signer.SignWithBaselineBProfile(
                signerProperties,
                chain,
                new DotNetExternalSignature(privateKey));

            signedPdf = output.ToArray();
            if (!TryExtractLastCms(signedPdf, out cms, out error))
            {
                signedPdf = [];
                return false;
            }

            if (!TryVerify(signedPdf, out error))
            {
                signedPdf = [];
                cms = [];
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = "iText PAdES signing failed. " + ex.Message;
            return false;
        }
    }

    public static bool TryVerify(byte[] pdf, out string? error)
    {
        try
        {
            using var input = new MemoryStream(pdf, writable: false);
            using var reader = new PdfReader(input);
            using var document = new PdfDocument(reader);
            var signatures = new SignatureUtil(document);
            var names = signatures.GetSignatureNames();
            if (names.Count == 0)
            {
                error = "PDF contains no embedded signature.";
                return false;
            }

            foreach (var name in names)
            {
                var signature = signatures.ReadSignatureData(name);
                if (!signature.VerifySignatureIntegrityAndAuthenticity())
                {
                    error = $"PDF signature '{name}' did not verify.";
                    return false;
                }
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = "PDF signature verification failed. " + ex.Message;
            return false;
        }
    }

    private static IX509Certificate[] ConvertCertificate(X509Certificate2 certificate)
    {
        using var encoded = new MemoryStream(certificate.RawData, writable: false);
        return [BouncyCastleFactoryCreator.GetFactory().CreateX509Certificate(encoded)];
    }

    private static bool TryExtractLastCms(byte[] pdf, out byte[] cms, out string? error)
    {
        cms = [];
        using var input = new MemoryStream(pdf, writable: false);
        using var reader = new PdfReader(input);
        using var document = new PdfDocument(reader);
        var signatures = new SignatureUtil(document);
        var names = signatures.GetSignatureNames();
        if (names.Count == 0)
        {
            error = "iText did not create a PDF signature field.";
            return false;
        }

        var dictionary = signatures.GetSignatureDictionary(names[^1]);
        var contents = dictionary?.GetAsString(PdfName.Contents)?.GetValueBytes();
        if (contents is not { Length: > 0 })
        {
            error = "PDF signature Contents is empty.";
            return false;
        }

        try
        {
            AsnDecoder.ReadEncodedValue(contents, AsnEncodingRules.BER, out _, out _, out var consumed);
            cms = contents[..consumed];
            error = null;
            return true;
        }
        catch (AsnContentException)
        {
            error = "PDF signature Contents is not a CMS object.";
            return false;
        }
    }

    private sealed class DotNetExternalSignature(AsymmetricAlgorithm key) : IExternalSignature
    {
        public string GetDigestAlgorithmName()
            => key is ECDsa { KeySize: >= 384 } ? "SHA-384" : "SHA-256";

        public string GetSignatureAlgorithmName()
            => key is RSA ? "RSA" : "ECDSA";

        public ISignatureMechanismParams? GetSignatureMechanismParameters() => null;

        public byte[] Sign(byte[] message)
            => key switch
            {
                RSA rsa => rsa.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                ECDsa ecdsa when ecdsa.KeySize >= 384 =>
                    ecdsa.SignData(message, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence),
                ECDsa ecdsa =>
                    ecdsa.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
                _ => throw new CryptographicException("The PIV signing key is not RSA or ECDSA."),
            };
    }
}
