using System.Security.Cryptography;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using iText.Bouncycastleconnector;
using iText.Kernel.Pdf;
using iText.Signatures;
using Xunit;

namespace HardwareTest.Tests.Reporting;

public sealed class ITextPadesSignatureTests
{
    [Theory]
    [InlineData("RSA")]
    [InlineData("P256")]
    [InlineData("P384")]
    public void Current_certification_verifies_for_its_embedded_certificate(string algorithm)
    {
        using var certificate = CreateCertificate(algorithm);
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), certificate);
        Assert.True(ITextPadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint.ToLowerInvariant(), out var error), error);
        Assert.Contains("/ETSI.CAdES.detached"u8, signed);
        using var document = Open(signed);
        var signatures = new SignatureUtil(document);
        Assert.Equal("HardwareTestCertification", Assert.Single(signatures.GetSignatureNames()));
        var dictionary = signatures.GetSignatureDictionary("HardwareTestCertification");
        Assert.Equal(dictionary.GetIndirectReference(), document.GetCatalog().GetPdfObject()
            .GetAsDictionary(PdfName.Perms).GetAsDictionary(PdfName.DocMDP).GetIndirectReference());
        var transform = dictionary.GetAsArray(PdfName.Reference).GetAsDictionary(0);
        Assert.Equal(1, transform.GetAsDictionary(PdfName.TransformParams).GetAsNumber(PdfName.P).IntValue());
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
            Assert.False(ITextPadesSignature.TryVerify(signed, thumbprint, out var error));
            Assert.NotNull(error);
        }
    }

    [Fact]
    public void Signing_an_already_signed_report_is_rejected_with_empty_outputs()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), certificate);
        Assert.False(ITextPadesSignature.TrySign(signed, certificate.Certificate, certificate.PrivateKey,
            "Software Certifier", DateTimeOffset.UnixEpoch, out var twice, out var cms, out var error));
        Assert.Empty(twice);
        Assert.Empty(cms);
        Assert.Contains("already signed", error);
    }

    [Theory]
    [InlineData(AccessPermissions.UNSPECIFIED)]
    [InlineData(AccessPermissions.FORM_FIELDS_MODIFICATION)]
    [InlineData(AccessPermissions.ANNOTATION_MODIFICATION)]
    public void Cryptographically_valid_permissive_or_absent_certification_is_rejected(AccessPermissions permission)
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = SignWithOptions(certificate, permission);
        AssertCryptographicSignature(signed, "HardwareTestCertification");
        Assert.False(ITextPadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out _));
    }

    [Theory]
    [InlineData("AnotherSignature", true)]
    [InlineData("HardwareTestCertification", false)]
    public void Wrong_field_or_non_pades_profile_is_rejected(string field, bool cades)
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = SignWithOptions(certificate, AccessPermissions.NO_CHANGES_PERMITTED, field, cades);
        AssertCryptographicSignature(signed, field);
        Assert.False(ITextPadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out _));
    }

    [Fact]
    public void Trailing_bytes_outside_signed_range_are_rejected()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), certificate);
        var trailing = signed.Concat("tamper!!"u8.ToArray()).ToArray();
        AssertCryptographicSignature(trailing, "HardwareTestCertification");
        Assert.False(ITextPadesSignature.TryVerify(trailing, certificate.Certificate.Thumbprint, out var error));
        Assert.Contains("whole file", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_unsigned_incremental_revision_is_rejected()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var signed = Sign(PdfTestFixture.CreateMinimalPdf(), certificate);
        using var output = new MemoryStream();
        using (var document = new PdfDocument(new PdfReader(new MemoryStream(signed)), new PdfWriter(output),
            new StampingProperties().UseAppendMode()))
        {
            document.GetDocumentInfo().SetSubject("Unsigned modification after issuance");
        }

        var appended = output.ToArray();
        Assert.True(appended.Length > signed.Length);
        using (var document = Open(appended))
        {
            Assert.Equal("Unsigned modification after issuance", document.GetDocumentInfo().GetSubject());
            Assert.True(new SignatureUtil(document).GetTotalRevisions() > 1);
        }

        AssertCryptographicSignature(appended, "HardwareTestCertification");
        Assert.False(ITextPadesSignature.TryVerify(appended, certificate.Certificate.Thumbprint, out _));
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
            using var document = Open(signed);
            var range = new SignatureUtil(document).GetSignatureDictionary("HardwareTestCertification")
                .GetAsArray(PdfName.ByteRange);
            var contentsStart = checked((int)range.GetAsNumber(1).LongValue());
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

        Assert.False(ITextPadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out _));
    }

    [Fact]
    public void Unsigned_and_truncated_pdf_are_rejected()
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var pdf = PdfTestFixture.CreateMinimalPdf();
        Assert.False(ITextPadesSignature.TryVerify(pdf, certificate.Certificate.Thumbprint, out _));
        var signed = Sign(pdf, certificate);
        Assert.False(ITextPadesSignature.TryVerify(signed[..(signed.Length / 2)], certificate.Certificate.Thumbprint, out _));
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
        Assert.False(ITextPadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%PDF-nope")]
    public void Malformed_input_is_rejected(string content)
    {
        using var certificate = SoftwareSigningCertificate.CreateRsa();
        var bytes = System.Text.Encoding.ASCII.GetBytes(content);
        Assert.False(ITextPadesSignature.TryVerify(bytes, certificate.Certificate.Thumbprint, out _));
        Assert.False(ITextPadesSignature.TrySign(bytes, certificate.Certificate, certificate.PrivateKey,
            "Software Certifier", DateTimeOffset.UnixEpoch, out var signed, out var cms, out _));
        Assert.Empty(signed);
        Assert.Empty(cms);
    }

    [Fact]
    public async Task Typst_current_certification_report_accepts_itext_certification()
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
        Assert.True(ITextPadesSignature.TryVerify(signed, certificate.Certificate.Thumbprint, out var error), error);
        using var original = Open(pdf);
        using var issued = Open(signed);
        Assert.Equal(original.GetNumberOfPages(), issued.GetNumberOfPages());
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
        Assert.True(ITextPadesSignature.TrySign(pdf, certificate.Certificate, certificate.PrivateKey,
            "Software Certifier", DateTimeOffset.UnixEpoch, out var signed, out var cms, out var error), error);
        Assert.NotEmpty(cms);
        return signed;
    }

    private static PdfDocument Open(byte[] pdf) => new(new PdfReader(new MemoryStream(pdf, writable: false)));

    private static void AssertCryptographicSignature(byte[] pdf, string field)
    {
        using var document = Open(pdf);
        Assert.True(new SignatureUtil(document).ReadSignatureData(field).VerifySignatureIntegrityAndAuthenticity());
    }

    // Test-only options create otherwise authentic PDFs that violate current issuance policy.
    private static byte[] SignWithOptions(SoftwareSigningCertificate certificate, AccessPermissions permission,
        string field = "HardwareTestCertification", bool cades = true)
    {
        using var input = new MemoryStream(PdfTestFixture.CreateMinimalPdf());
        using var reader = new PdfReader(input);
        using var output = new MemoryStream();
        var properties = new SignerProperties().SetFieldName(field).SetCertificationLevel(permission);
        using var encoded = new MemoryStream(certificate.Certificate.RawData);
        var chain = new[] { BouncyCastleFactoryCreator.GetFactory().CreateX509Certificate(encoded) };
        var signature = new SoftwareRsaSignature((RSA)certificate.PrivateKey);
        if (cades)
        {
            new PdfPadesSigner(reader, output).SetEstimatedSize(32 * 1024)
                .SignWithBaselineBProfile(properties, chain, signature);
        }
        else
        {
            new PdfSigner(reader, output, new StampingProperties()).SetSignerProperties(properties)
                .SignDetached(signature, chain, null, null, null, 32 * 1024, PdfSigner.CryptoStandard.CMS);
        }

        return output.ToArray();
    }

    private sealed class SoftwareRsaSignature(RSA key) : IExternalSignature
    {
        public string GetDigestAlgorithmName() => "SHA-256";
        public string GetSignatureAlgorithmName() => "RSA";
        public ISignatureMechanismParams? GetSignatureMechanismParameters() => null;
        public byte[] Sign(byte[] message) => key.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
