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
        return TrySign(pdf, certificate, new DotNetExternalSignature(privateKey), displayName, signingTime,
            out signedPdf, out cms, out error);
    }

    internal static bool TrySign(
        byte[] pdf, X509Certificate2 certificate, IExternalSignature externalSignature,
        string displayName, DateTimeOffset signingTime, out byte[] signedPdf, out byte[] cms, out string? error)
    {
        signedPdf = [];
        cms = [];
        try
        {
            using (var existingInput = new MemoryStream(pdf, writable: false))
            using (var existingReader = new PdfReader(existingInput))
            using (var existingDocument = new PdfDocument(existingReader))
            {
                if (new SignatureUtil(existingDocument).GetSignatureNames().Count != 0)
                {
                    error = "PDF is already signed; certification requires an unsigned working report.";
                    return false;
                }
            }

            using var input = new MemoryStream(pdf, writable: false);
            using var reader = new PdfReader(input);
            using var output = new MemoryStream();
            var signerProperties = new SignerProperties()
                .SetFieldName(FieldName)
                .SetCertificationLevel(AccessPermissions.NO_CHANGES_PERMITTED)
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
                externalSignature);

            signedPdf = output.ToArray();
            if (!TryExtractLastCms(signedPdf, out cms, out error))
            {
                signedPdf = [];
                return false;
            }

            if (!TryVerify(signedPdf, certificate.Thumbprint, out error))
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
            signedPdf = [];
            cms = [];
            error = "iText PAdES signing failed. " + ex.Message;
            return false;
        }
    }

    public static bool TryVerify(byte[] pdf, string? expectedSignerThumbprint, out string? error)
    {
        if (string.IsNullOrWhiteSpace(expectedSignerThumbprint))
        {
            error = "Expected signer certificate thumbprint is required.";
            return false;
        }

        try
        {
            using var input = new MemoryStream(pdf, writable: false);
            using var reader = new PdfReader(input);
            using var document = new PdfDocument(reader);
            var signatures = new SignatureUtil(document);
            var names = signatures.GetSignatureNames();
            Require(names.Count == 1 && names[0] == FieldName,
                "PDF must contain exactly the current certification signature field.");
            var dictionary = signatures.GetSignatureDictionary(FieldName);
            Require(dictionary is not null, "PDF certification signature dictionary is missing.");
            Require(PdfName.ETSI_CAdES_DETACHED.Equals(dictionary.GetAsName(PdfName.SubFilter)),
                "PDF certification must use the current PAdES profile.");
            Require(signatures.SignatureCoversWholeDocument(FieldName),
                "PDF certification signature must cover the whole file.");

            // The reader may ignore trailing bytes. Bind its parsed range to the supplied bytes.
            var range = dictionary.GetAsArray(PdfName.ByteRange);
            Require(range is not null && range.Size() == 4, "PDF signature ByteRange must have four values.");
            var values = new long[4];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = ReadNonnegativeInteger(range.GetAsNumber(i), pdf.LongLength);
            }

            Require(values[0] == 0 && values[1] > 0 && values[2] > values[1]
                && values[3] > 0 && values[3] == pdf.LongLength - values[2],
                "PDF certification signature must cover the whole file, including its final bytes.");

            var certification = document.GetCatalog().GetPdfObject()
                .GetAsDictionary(PdfName.Perms)?.GetAsDictionary(PdfName.DocMDP);
            var signatureReference = dictionary.GetIndirectReference();
            Require(signatureReference is not null && certification?.GetIndirectReference() is { } reference
                && signatureReference.Equals(reference),
                "PDF catalog must bind DocMDP to this certification signature.");
            var references = dictionary.GetAsArray(PdfName.Reference);
            var docMdpCount = 0;
            if (references is not null)
            {
                for (var i = 0; i < references.Size(); i++)
                {
                    var transform = references.GetAsDictionary(i);
                    if (!PdfName.DocMDP.Equals(transform?.GetAsName(PdfName.TransformMethod)))
                    {
                        continue;
                    }

                    docMdpCount++;
                    Require(ReadNonnegativeInteger(transform!.GetAsDictionary(PdfName.TransformParams)
                        ?.GetAsNumber(PdfName.P), 3) == 1,
                        "PDF certification must forbid changes (DocMDP P=1).");
                }
            }

            Require(docMdpCount == 1, "PDF certification must contain exactly one DocMDP transform.");
            var signature = signatures.ReadSignatureData(FieldName);
            Require(signature is not null && signature.VerifySignatureIntegrityAndAuthenticity(),
                "PDF certification signature integrity did not verify.");
            var signerDer = signature.GetSigningCertificate()?.GetEncoded();
            Require(signerDer is { Length: > 0 }, "PDF signature has no embedded signer certificate.");
            // SHA1 computes the existing X509 thumbprint identifier, not the signature digest.
            var actualThumbprint = Convert.ToHexString(SHA1.HashData(signerDer));
            Require(string.Equals(actualThumbprint, expectedSignerThumbprint, StringComparison.OrdinalIgnoreCase),
                "PDF signer certificate thumbprint does not match the expected credential.");
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = "PDF signature verification failed. " + ex.Message;
            return false;
        }
    }

    private static long ReadNonnegativeInteger(PdfNumber? number, long maximum)
    {
        var value = number?.DoubleValue() ?? double.NaN;
        Require(double.IsFinite(value) && value >= 0 && value <= maximum && value == Math.Truncate(value),
            "PDF signature numeric value must be a bounded nonnegative integer.");
        return number!.LongValue();
    }

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
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
