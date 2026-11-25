using System.Buffers;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using MiPdf;
using MiPdf.Forms;
using MiPdf.Objects;
using MiPdf.Signatures;

namespace HardwareTest.Core.Reporting;

internal static class PadesSignature
{
    private const int SignatureSize = 32 * 1024;
    private const string FieldName = "HardwareTestCertification";
    private const string DataContentType = "1.2.840.113549.1.7.1";
    private const string ContentTypeAttribute = "1.2.840.113549.1.9.3";
    private const string SigningCertificate = "1.2.840.113549.1.9.16.2.12";
    private const string SigningCertificateV2 = "1.2.840.113549.1.9.16.2.47";
    private static readonly PdfName DocMdp = PdfName.Get("DocMDP");
    private static readonly SearchValues<byte> HexDigits = SearchValues.Create("0123456789ABCDEFabcdef"u8);

    // The provider signs a digest using its retained key. ECDSA providers return native P1363 bytes.
    internal static bool TrySign(
        byte[] pdf, X509Certificate2 certificate, HashAlgorithmName digestAlgorithm, Func<byte[], byte[]> signHash,
        string displayName, DateTimeOffset signingTime, out byte[] signedPdf, out byte[] cms, out string? error)
    {
        signedPdf = [];
        cms = [];
        try
        {
            var options = new PdfSignatureOptions
            {
                FieldName = FieldName,
                Format = PdfSignatureFormat.PAdES,
                Certification = CertificationLevel.NoChanges,
                DigestAlgorithm = digestAlgorithm,
                SigningTime = signingTime.ToUniversalTime(),
                Reason = "Hardware test report certification",
                ContactInfo = displayName,
                SignatureCreator = "HardwareTest",
            };
            // Preparation rejects damaged or already-signed PDFs and reserves space before touching the key.
            var prepared = PdfSigner.PrepareExternalSignature(pdf, options, certificate, SignatureSize);
            var attributes = CmsUtil.CreateSignedAttributes(prepared.GetDocumentDigest(), certificate);
            var hash = CryptographicOperations.HashData(digestAlgorithm, attributes);
            var encoded = AssembleDetachedCms(attributes, signHash(hash), certificate, digestAlgorithm);
            var signed = PdfSigner.CompleteExternalSignature(prepared, encoded);

            if (!TryVerify(signed, certificate.Thumbprint, out error))
                return false;

            signedPdf = signed;
            cms = encoded;
            return true;
        }
        catch (Exception ex)
        {
            error = "PAdES signing failed. " + ex.Message;
            return false;
        }
    }

    internal static byte[] AssembleDetachedCms(byte[] attributes, byte[] nativeSignature,
        X509Certificate2 certificate, HashAlgorithmName digestAlgorithm)
    {
        using var ecdsa = certificate.GetECDsaPublicKey();
        // The provider supplies known P1363 bytes. Convert explicitly before miPDFlib's dual-format API.
        var signature = ecdsa is null ? nativeSignature : CmsUtil.P1363ToDer(nativeSignature);
        return CmsUtil.AssembleSignedData(attributes, signature, certificate, digestAlgorithm: digestAlgorithm);
    }

    internal static bool TryVerify(byte[] pdf, string? expectedSignerThumbprint, out string? error)
    {
        if (string.IsNullOrWhiteSpace(expectedSignerThumbprint))
        {
            error = "Expected signer certificate thumbprint is required.";
            return false;
        }

        try
        {
            using var document = PdfDocument.Open(pdf);
            Require(!document.WasRepaired, "The PDF has a damaged cross-reference table.");
            var fields = SignedFields(document).ToArray();
            Require(fields.Length == 1 && fields[0].FullName == FieldName,
                "PDF must contain exactly the current certification signature field.");

            var field = fields[0];
            var dictionary = field.SignatureDictionary!;
            Require(dictionary[PdfName.SubFilter] is PdfName { Value: "ETSI.CAdES.detached" },
                "PDF certification must use the current PAdES profile.");
            RequireNoChangesCertification(document, field, dictionary);
            RequireWholeFileByteRange(pdf, dictionary);

            var result = PdfSignatureValidator.Validate(document).Single();
            using var signer = result.SignerCertificate;
            try
            {
                Require(result.IsIntegrityValid && signer is not null && result.Cms is { Length: > 0 },
                    "PDF certification signature integrity did not verify.");
                RequireCadesProfile(result.Cms, signer);
                Require(string.Equals(signer.Thumbprint, expectedSignerThumbprint, StringComparison.OrdinalIgnoreCase),
                    "PDF signer certificate thumbprint does not match the expected credential.");
            }
            finally
            {
                foreach (var certificate in result.Certificates)
                    certificate.Dispose();
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

    private static IEnumerable<PdfSignatureField> SignedFields(PdfDocument document)
        => document.GetForm()?.Fields.OfType<PdfSignatureField>().Where(field => field.IsSigned) ?? [];

    private static void RequireNoChangesCertification(PdfDocument document, PdfSignatureField field, PdfDictionary dictionary)
    {
        var catalogReference = document.Catalog.GetDictionary(PdfName.Perms)?[DocMdp];
        Require(field.Dictionary[PdfName.V] is PdfReference signatureReference && signatureReference.Equals(catalogReference),
            "PDF catalog must bind DocMDP to this certification signature.");

        var transforms = dictionary.GetArray(PdfName.Get("Reference"))?
            .Select(item => item.Resolve()).OfType<PdfDictionary>()
            .Where(transform => transform.GetName(PdfName.Get("TransformMethod")) == DocMdp)
            .ToArray();
        Require(transforms is { Length: 1 }, "PDF certification must contain exactly one DocMDP transform.");
        Require(transforms[0].GetDictionary(PdfName.Get("TransformParams"))?.GetNumber(PdfName.P) is { Value: 1 },
            "PDF certification must forbid changes (DocMDP P=1).");
    }

    private static void RequireWholeFileByteRange(byte[] pdf, PdfDictionary dictionary)
    {
        var range = dictionary[PdfName.ByteRange] as PdfArray;
        Require(range is { Count: 4 },
            "PDF signature ByteRange must have four values.");
        var values = range.Select(value => value is PdfNumber { IsInteger: true } number ? number.LongValue : -1).ToArray();
        Require(values[0] == 0 && values[1] > 0 && values[2] > values[1] && values[2] < pdf.Length
            && values[3] == pdf.Length - values[2],
            "PDF certification signature must cover the whole file, including its final bytes.");

        var start = (int)values[1];
        var end = (int)values[2];
        var contents = dictionary[PdfName.Contents] as PdfString;
        Require(contents is { IsHex: true, Bytes.Length: > 0 }
            && end - start == 2L * contents.Bytes.Length + 2
            && pdf[start] == (byte)'<' && pdf[end - 1] == (byte)'>'
            && pdf.AsSpan(start + 1, end - start - 2).IndexOfAnyExcept(HexDigits) < 0,
            "PDF signature ByteRange must exclude exactly the Contents token.");

        // miPDFlib does not expose token offsets. Reparse a private copy with the first nibble
        // changed to bind this dense hex token to the selected field's Contents value.
        var probe = (byte[])pdf.Clone();
        var highNibble = contents.Bytes[0] < 16 ? 1 : 0;
        probe[start + 1] = (byte)('0' + highNibble);
        using var probed = PdfDocument.Open(probe);
        var parsed = probed.GetForm()?.GetField<PdfSignatureField>(FieldName)?
            .SignatureDictionary?[PdfName.Contents] as PdfString;
        Require(!probed.WasRepaired && parsed?.Bytes.Length == contents.Bytes.Length
            && parsed.Bytes[0] == ((highNibble << 4) | (contents.Bytes[0] & 0x0f))
            && parsed.Bytes.AsSpan(1).SequenceEqual(contents.Bytes.AsSpan(1)),
            "PDF signature ByteRange must exclude the selected signature's Contents token.");
    }

    private static void RequireCadesProfile(byte[] encoded, X509Certificate2 signer)
    {
        var cms = new SignedCms();
        cms.Decode(encoded);
        Require(cms.SignerInfos.Count == 1, "PAdES certification must contain a single CMS signer.");
        RequireDetachedData(encoded);

        var attributes = cms.SignerInfos[0].SignedAttributes.Cast<CryptographicAttributeObject>().ToArray();
        var contentType = attributes.SingleOrDefault(attribute => attribute.Oid.Value == ContentTypeAttribute);
        Require(contentType?.Values.Count == 1, "PAdES signature must have one signed content-type value.");
        var contentTypeValue = new AsnReader(contentType.Values[0].RawData, AsnEncodingRules.DER);
        Require(contentTypeValue.ReadObjectIdentifier() == DataContentType && !contentTypeValue.HasData,
            "PAdES signed content type must be id-data.");

        // CAdES binds the exact certificate even if another certificate shares its public key.
        var certificateAttributes = attributes
            .Where(attribute => attribute.Oid.Value is SigningCertificate or SigningCertificateV2)
            .ToArray();
        Require(certificateAttributes.Length > 0, "PAdES signature is missing its signing-certificate attribute.");
        foreach (var attribute in certificateAttributes)
        {
            Require(attribute.Values.Count == 1, "PAdES signing-certificate attribute must have one value.");
            var essCertId = new AsnReader(attribute.Values[0].RawData, AsnEncodingRules.BER)
                .ReadSequence().ReadSequence().ReadSequence();
            var algorithm = HashAlgorithmName.SHA1;
            if (attribute.Oid.Value == SigningCertificateV2)
            {
                // ESSCertIDv2 defaults to SHA-256 when the algorithm is omitted.
                algorithm = essCertId.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence)
                    ? HashAlgorithmName.FromOid(essCertId.ReadSequence().ReadObjectIdentifier())
                    : HashAlgorithmName.SHA256;
            }
            var hash = CryptographicOperations.HashData(algorithm, signer.RawData);
            Require(CryptographicOperations.FixedTimeEquals(hash, essCertId.ReadOctetString()),
                "PAdES signing-certificate attribute does not match the signer.");
        }
    }

    private static void RequireDetachedData(byte[] encoded)
    {
        // SignedCms.Decode has validated SignedData's structure. Its public content bytes do not
        // distinguish an omitted eContent field from a present empty OCTET STRING (RFC 5652 §5.2).
        var contentInfo = new AsnReader(encoded, AsnEncodingRules.BER).ReadSequence();
        contentInfo.ReadObjectIdentifier();
        var signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
        signedData.ReadInteger();
        signedData.ReadSetOf(skipSortOrderValidation: true);
        var encapsulatedContent = signedData.ReadSequence();
        Require(encapsulatedContent.ReadObjectIdentifier() == DataContentType && !encapsulatedContent.HasData,
            "PAdES certification must contain detached id-data CMS.");
    }

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }
}
