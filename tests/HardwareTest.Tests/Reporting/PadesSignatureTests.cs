using System.Formats.Asn1;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using MiPdf;
using MiPdf.Forms;
using MiPdf.IO;
using MiPdf.Objects;
using MiPdf.Signatures;
using Xunit;

namespace HardwareTest.Tests.Reporting;

public sealed class PadesSignatureTests
{
    private const string FieldName = "HardwareTestCertification";
    private const string ContentTypeAttribute = "1.2.840.113549.1.9.3";
    private const string SigningCertificateV2 = "1.2.840.113549.1.9.16.2.47";

    [Theory]
    [InlineData("RSA", "2.16.840.1.101.3.4.2.1")]
    [InlineData("P256", "2.16.840.1.101.3.4.2.1")]
    [InlineData("P384", "2.16.840.1.101.3.4.2.2")]
    public void Current_certification_verifies_for_its_embedded_certificate(string algorithm, string digestOid)
    {
        using var certificate = CreateCertificate(algorithm);
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), certificate);
        Assert.True(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint.ToLowerInvariant(), out var error), error);
        Assert.Contains("/ETSI.CAdES.detached"u8, signed);
        using var document = PdfDocument.Open(signed);
        var field = Assert.Single(document.GetForm()!.Fields.OfType<PdfSignatureField>());
        Assert.Equal(FieldName, field.FullName);
        Assert.Equal(field.Dictionary[PdfName.V], document.Catalog.GetDictionary(PdfName.Perms)![PdfName.Get("DocMDP")]);
        var transform = Assert.IsType<PdfDictionary>(Assert.Single(field.SignatureDictionary!.GetArray(PdfName.Get("Reference"))!));
        Assert.Equal(1, transform.GetDictionary(PdfName.Get("TransformParams"))!.GetInt(PdfName.P));
        var cms = AssertCryptographicSignature(signed, FieldName);
        Assert.Equal(digestOid, Assert.Single(cms.SignerInfos.Cast<SignerInfo>()).DigestAlgorithm.Value);
    }

    [Fact]
    public void Thumbprint_binds_certificate_rather_than_subject_or_public_key()
    {
        using var signer = SoftwareSigningCertificate.CreateRsa();
        using var sameName = SoftwareSigningCertificate.CreateRsa();
        using var sameKey = SoftwareSigningCertificate.CreateCertificate(signer.PrivateKey);
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), signer);
        Assert.Equal(signer.Certificate.Subject, sameName.Certificate.Subject);
        Assert.Equal(signer.Certificate.Subject, sameKey.Subject);
        Assert.NotEqual(signer.Certificate.Thumbprint, sameKey.Thumbprint);
        foreach (var thumbprint in new string?[] { sameName.Certificate.Thumbprint, sameKey.Thumbprint, null, "", " " })
        {
            Assert.False(PadesSignature.TryVerify(signed, thumbprint, out var error));
            Assert.NotNull(error);
        }
    }

    [Theory]
    [InlineData(FieldName, "already signed")]
    [InlineData("EarlierApproval", "first signature")]
    public void Signing_an_already_signed_report_is_rejected_before_calling_the_key(string field, string expectedError)
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = SignWithOptions(certificate, null, field);
        var keyCalls = 0;
        Assert.False(PadesSignature.TrySign(signed, certificate.Certificate, HashAlgorithmName.SHA256,
            _ => { keyCalls++; return []; }, "Software Certifier", DateTimeOffset.UnixEpoch,
            out var twice, out var cms, out var error));
        Assert.Equal(0, keyCalls);
        Assert.Empty(twice);
        Assert.Empty(cms);
        Assert.Contains(expectedError, error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(CertificationLevel.FormFilling)]
    [InlineData(CertificationLevel.FormFillingAndAnnotations)]
    public void Cryptographically_valid_permissive_or_absent_certification_is_rejected(CertificationLevel? permission)
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = SignWithOptions(certificate, permission);
        AssertCryptographicSignature(signed, "HardwareTestCertification");
        Assert.False(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out _));
    }

    [Theory]
    [InlineData("AnotherSignature", true)]
    [InlineData("HardwareTestCertification", false)]
    public void Wrong_field_or_non_pades_profile_is_rejected(string field, bool cades)
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = SignWithOptions(certificate, CertificationLevel.NoChanges, field, cades);
        AssertCryptographicSignature(signed, field);
        Assert.False(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out _));
    }

    [Theory]
    [InlineData("tamper!!")]
    [InlineData("\n")]
    public void Trailing_bytes_outside_signed_range_are_rejected(string suffix)
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), certificate);
        var trailing = signed.Concat(Encoding.ASCII.GetBytes(suffix)).ToArray();
        AssertCryptographicSignature(trailing, "HardwareTestCertification");
        Assert.False(PadesSignature.TryVerify(trailing, certificate.Certificate.Thumbprint, out var error));
        Assert.Contains("whole file", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_unsigned_incremental_revision_is_rejected()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), certificate);
        using var document = PdfDocument.Open(signed);
        document.Info.Subject = "Unsigned modification after issuance";
        var appended = document.ToArray(new PdfSaveOptions { Incremental = true });

        Assert.True(appended.Length > signed.Length);
        using var changed = PdfDocument.Open(appended);
        Assert.Equal("Unsigned modification after issuance", changed.Info.Subject);
        Assert.Equal(signed.Length, PdfSignatureValidator.Validate(changed).Single().SignedRevisionLength);

        AssertCryptographicSignature(appended, "HardwareTestCertification");
        Assert.False(PadesSignature.TryVerify(appended, certificate.Certificate.Thumbprint, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Signed_content_or_cms_tamper_is_rejected(bool tamperCms)
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), certificate);
        if (tamperCms)
        {
            using var document = PdfDocument.Open(signed);
            var range = GetSignatureDictionary(document, FieldName).GetArray(PdfName.ByteRange)!;
            var contentsStart = Assert.IsType<PdfNumber>(range[1]).IntValue;
            // Contents is excluded from ByteRange, so corrupt CMS itself, not signed PDF syntax.
            Assert.Equal((byte)'<', signed[contentsStart]);
            signed[contentsStart + 1] = signed[contentsStart + 1] == (byte)'0' ? (byte)'1' : (byte)'0';
        }
        else
        {
            var titleAt = signed.AsSpan().IndexOf("(Certification)"u8);
            Assert.True(titleAt >= 0);
            signed[titleAt + 1] = (byte)'X';
        }

        Assert.False(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out _));
    }

    [Fact]
    public void Unsigned_and_truncated_pdf_are_rejected()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var pdf = PdfTestFixture.CreateMinimalPdf();
        Assert.False(PadesSignature.TryVerify(pdf, certificate.Certificate.Thumbprint, out _));
        var signed = Sign(pdf, certificate);
        Assert.False(PadesSignature.TryVerify(signed[..(signed.Length / 2)], certificate.Certificate.Thumbprint, out _));
    }

    [Fact]
    public void Malformed_byte_range_is_rejected()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), certificate);
        var rangeAt = signed.AsSpan().IndexOf("/ByteRange"u8);
        Assert.True(rangeAt >= 0);
        var firstValue = rangeAt + signed.AsSpan(rangeAt).IndexOf("[0"u8) + 1;
        Assert.True(firstValue > rangeAt);
        signed[firstValue] = (byte)'1';
        Assert.False(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%PDF-nope")]
    public void Malformed_input_is_rejected(string content)
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var bytes = System.Text.Encoding.ASCII.GetBytes(content);
        Assert.False(PadesSignature.TryVerify(bytes, certificate.Certificate.Thumbprint, out _));
        Assert.False(SoftwareSigningCertificate.TrySign(bytes, certificate.Certificate, certificate.PrivateKey,
            "Software Certifier", DateTimeOffset.UnixEpoch, out var signed, out var cms, out _));
        Assert.Empty(signed);
        Assert.Empty(cms);
    }

    [Fact]
    public async Task Typst_current_certification_report_accepts_pades_certification()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = new TestRunRecord
        {
            RunId = Guid.NewGuid().ToString("N"),
            PlanName = "PAdES",
            DutSerial = "DUT-1",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        };
        await runStore.SaveAsync(run);
        using var reports = new TypstReportService(runStore, new AppSettings { EmbedPlotsInReport = false });
        var artifacts = await reports.GenerateReportsAsync(run, [ReportKinds.Certification]);
        var artifact = Assert.Single(artifacts);
        Assert.Equal(ReportKinds.Certification, artifact.Kind);
        var pdf = await File.ReadAllBytesAsync(artifact.PdfPath);
        using var certificate = SoftwareSigningCertificate.CreateP256();
        var signed = Sign(pdf, certificate);
        Assert.True(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out var error), error);
        using var original = PdfDocument.Open(pdf);
        using var issued = PdfDocument.Open(signed);
        Assert.Equal(original.PageCount, issued.PageCount);
        AssertCryptographicSignature(signed, FieldName);
    }

    private static SoftwareSigningCertificate CreateCertificate(string algorithm) => algorithm switch
    {
        "RSA" => SoftwareSigningCertificate.CreateRsa(),
        "P256" => SoftwareSigningCertificate.CreateP256(),
        "P384" => SoftwareSigningCertificate.CreateP384(),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
    };

    private static byte[] Sign(byte[] pdf, SoftwareSigningCertificate certificate)
    {
        Assert.True(SoftwareSigningCertificate.TrySign(pdf, certificate.Certificate, certificate.PrivateKey,
            "Software Certifier", DateTimeOffset.UnixEpoch, out var signed, out var cms, out var error), error);
        using var document = PdfDocument.Open(signed);
        var contents = GetSignatureDictionary(document, FieldName).GetString(PdfName.Contents)!.Bytes;
        AsnDecoder.ReadEncodedValue(contents, AsnEncodingRules.BER, out _, out _, out var length);
        Assert.Equal(cms, contents[..length]);
        return signed;
    }

    [Theory]
    [InlineData("RSA", "5701A0F679A083F3F284F8390E43339558BAC716")]
    [InlineData("P256", "683E478464E847EC64E0970C8BC1624905FA1C97")]
    [InlineData("P384", "4B9D8C0AA804AE7B384315D8A67F606913406DD3")]
    public void Previously_issued_itext_report_verifies(string key, string thumbprint)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "reporting", $"itext-{key}.pdf.gz");
        using var fixture = File.OpenRead(path);
        using var gzip = new GZipStream(fixture, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        var pdf = output.ToArray();

        Assert.True(PadesSignature.TryVerify(pdf, thumbprint, out var error), error);
        AssertCryptographicSignature(pdf, FieldName);
    }

    [Fact]
    public void Byte_range_of_a_different_hex_token_is_rejected_by_the_contents_check()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        const int size = 4096;
        var decoy = Enumerable.Repeat((byte)0xA5, size).ToArray();
        var options = new PdfSignatureOptions
        {
            FieldName = FieldName,
            Certification = CertificationLevel.NoChanges,
            SignatureSize = size,
            SignatureDictionaryEntries = { ["Other"] = new PdfString(decoy, hex: true) },
        };
        var signed = PdfSigner.Sign(PdfTestFixture.CreateMinimalPdf(), certificate.Certificate, options);
        Assert.True(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out var originalError), originalError);

        var token = Encoding.ASCII.GetBytes($"<{Convert.ToHexString(decoy)}>");
        var decoyStart = signed.AsSpan().IndexOf(token);
        Assert.True(decoyStart >= 0);
        var decoyEnd = decoyStart + token.Length;
        var rangeStart = signed.AsSpan().IndexOf("/ByteRange"u8);
        var range = signed.AsSpan(rangeStart);
        var open = range.IndexOf((byte)'[');
        var close = range.IndexOf((byte)']');
        Encoding.ASCII.GetBytes($"[0 {decoyStart} {decoyEnd} {signed.Length - decoyEnd}".PadRight(close - open))
            .CopyTo(range[open..close]);

        Assert.False(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out var error));
        Assert.Contains("selected signature's Contents token", error);
    }

    [Theory]
    [InlineData('G', "exclude exactly the Contents token")]
    [InlineData('>', "PDF signature verification failed")]
    public void Contents_rejects_a_non_hex_final_nibble(char invalid, string expectedError)
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), certificate);
        using var document = PdfDocument.Open(signed);
        var range = GetSignatureDictionary(document, FieldName).GetArray(PdfName.ByteRange)!;
        var contentsEnd = Assert.IsType<PdfNumber>(range[2]).IntValue;
        Assert.Equal((byte)'0', signed[contentsEnd - 2]);
        signed[contentsEnd - 2] = (byte)invalid;

        Assert.False(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out var error));
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void Ecdsa_signer_output_is_read_as_p1363_even_when_it_looks_like_der()
    {
        using var certificate = SoftwareSigningCertificate.CreateP256();
        var native = Enumerable.Repeat((byte)0x11, 64).ToArray();
        Convert.FromHexString("303E021D").CopyTo(native, 0);
        Convert.FromHexString("021D").CopyTo(native, 33);
        var reader = new AsnReader(native, AsnEncodingRules.DER);
        var sequence = reader.ReadSequence();
        sequence.ReadInteger();
        sequence.ReadInteger();
        sequence.ThrowIfNotEmpty();
        reader.ThrowIfNotEmpty();

        var attributes = CmsUtil.CreateSignedAttributes(new byte[32], certificate.Certificate);
        var cms = PadesSignature.AssembleDetachedCms(attributes, native, certificate.Certificate, HashAlgorithmName.SHA256);

        var signature = CmsUtil.GetSignatureValue(cms);
        Assert.NotEqual(native, signature);
        Assert.Equal(CmsUtil.P1363ToDer(native), signature);
    }

    [Fact]
    public void Attached_cms_is_rejected_even_when_it_authenticates_the_pdf_bytes()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var input = PdfTestFixture.CreateMinimalPdf();
        using var valid = PdfDocument.Open(Sign(input, certificate));
        var contents = GetSignatureDictionary(valid, FieldName).GetString(PdfName.Contents)!.Bytes;
        var binding = CmsUtil.GetSignedAttribute(contents, SigningCertificateV2)!;
        var prepared = PrepareCertification(input);
        var cms = new SignedCms(new ContentInfo(prepared.GetSignedData()));
        var signer = new CmsSigner(certificate.Certificate);
        signer.SignedAttributes.Add(new AsnEncodedData(SigningCertificateV2, binding));
        cms.ComputeSignature(signer);
        cms.CheckSignature(verifySignatureOnly: true);
        var signed = PdfSigner.CompleteExternalSignature(prepared, cms.Encode());

        Assert.False(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out var error));
        Assert.Contains("detached id-data CMS", error);
    }

    [Fact]
    public void Detached_cms_without_signed_content_type_is_rejected()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var prepared = PrepareCertification(PdfTestFixture.CreateMinimalPdf());
        var attributes = CmsUtil.CreateSignedAttributes(prepared.GetDocumentDigest(), certificate.Certificate);
        var reader = new AsnReader(attributes, AsnEncodingRules.DER).ReadSetOf();
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSetOf())
        {
            while (reader.HasData)
            {
                var entry = reader.ReadEncodedValue();
                var attribute = new AsnReader(entry, AsnEncodingRules.DER).ReadSequence();
                if (attribute.ReadObjectIdentifier() != ContentTypeAttribute)
                    writer.WriteEncodedValue(entry.Span);
            }
        }
        var incompleteAttributes = writer.Encode();
        var rsa = Assert.IsAssignableFrom<RSA>(certificate.PrivateKey);
        var signature = rsa.SignData(incompleteAttributes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cms = CmsUtil.AssembleSignedData(incompleteAttributes, signature, certificate.Certificate);
        var signed = PdfSigner.CompleteExternalSignature(prepared, cms);

        Assert.False(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out var error));
        Assert.Contains("one signed content-type value", error);
    }

    [Fact]
    public void Valid_detached_cms_without_cades_certificate_binding_is_rejected()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var prepared = PrepareCertification(PdfTestFixture.CreateMinimalPdf());
        var cms = new SignedCms(new ContentInfo(prepared.GetSignedData()), detached: true);
        var signer = new CmsSigner(certificate.Certificate);
        signer.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));
        cms.ComputeSignature(signer);
        var signed = PdfSigner.CompleteExternalSignature(prepared, cms.Encode());

        AssertCryptographicSignature(signed, FieldName);
        Assert.False(PadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out var error));
        Assert.Contains("missing its signing-certificate attribute", error);
    }

    private static PreparedSignature PrepareCertification(byte[] pdf)
        => PdfSigner.PrepareExternalSignature(pdf, new PdfSignatureOptions
        {
            FieldName = FieldName,
            Certification = CertificationLevel.NoChanges,
        });

    private static PdfDictionary GetSignatureDictionary(PdfDocument document, string field)
        => document.GetForm()!.GetField<PdfSignatureField>(field)!.SignatureDictionary!;

    private static SignedCms AssertCryptographicSignature(byte[] pdf, string field)
    {
        using var document = PdfDocument.Open(pdf);
        var dictionary = GetSignatureDictionary(document, field);
        var range = dictionary.GetArray(PdfName.ByteRange)!.Select(value => Assert.IsType<PdfNumber>(value).IntValue).ToArray();
        var contents = dictionary.GetString(PdfName.Contents)!.Bytes;
        AsnDecoder.ReadEncodedValue(contents, AsnEncodingRules.BER, out _, out _, out var length);
        var data = pdf.AsSpan(range[0], range[1]).ToArray().Concat(pdf.AsSpan(range[2], range[3]).ToArray()).ToArray();
        var oracle = new SignedCms(new ContentInfo(data), detached: true);
        oracle.Decode(contents.AsSpan(0, length));
        oracle.CheckSignature(verifySignatureOnly: true);
        return oracle;
    }

    // Test-only options create otherwise authentic PDFs that violate current issuance policy.
    private static byte[] SignWithOptions(SoftwareSigningCertificate certificate, CertificationLevel? permission,
        string field = FieldName, bool cades = true)
        => PdfSigner.Sign(PdfTestFixture.CreateMinimalPdf(), certificate.Certificate, new PdfSignatureOptions
        {
            FieldName = field,
            Certification = permission,
            Format = cades ? PdfSignatureFormat.PAdES : PdfSignatureFormat.Pkcs7Detached,
        });
}
