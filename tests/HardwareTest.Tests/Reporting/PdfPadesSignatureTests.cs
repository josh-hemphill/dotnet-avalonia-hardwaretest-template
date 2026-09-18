using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Credentials;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Reporting;

public sealed class PdfPadesSignatureTests
{
    [Fact]
    public void Software_rsa_embeds_verifiable_cms()
    {
        using var rsa = RSA.Create(2048);
        using var cert = CreateCert(rsa);
        var pdf = PdfPadesSignature.CreateMinimalPdf();
        Assert.True(
            PdfPadesSignature.TryPrepare(pdf, "Jane Certifier", DateTimeOffset.UnixEpoch, out var prepared, out var prepError),
            prepError);
        var cms = SignCms(prepared.SignedBytes, cert, rsa);
        Assert.True(prepared.TryEmbed(cms, out var signed, out var embedError), embedError);
        Assert.True(PdfPadesSignature.TryVerify(signed, out var verifyError), verifyError);
        Assert.Contains("/ByteRange"u8, signed);
        Assert.Contains("/adbe.pkcs7.detached"u8, signed);
    }

    [Fact]
    public void Tampered_page_content_fails_verify()
    {
        using var rsa = RSA.Create(2048);
        using var cert = CreateCert(rsa);
        var pdf = PdfPadesSignature.CreateMinimalPdf();
        Assert.True(PdfPadesSignature.TryPrepare(pdf, "X", DateTimeOffset.UnixEpoch, out var prepared, out _));
        Assert.True(prepared.TryEmbed(SignCms(prepared.SignedBytes, cert, rsa), out var signed, out _));
        var hello = "Certification"u8.ToArray();
        var at = IndexOf(signed, hello);
        Assert.True(at >= 0);
        signed[at] = (byte)'X';
        Assert.False(PdfPadesSignature.TryVerify(signed, out _));
    }

    [Fact]
    public async Task Typst_certification_pdf_accepts_piv_pades()
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
        byte[] pdf;
        try
        {
            var artifacts = await reports.GenerateReportsAsync(run, [ReportKinds.Certification]);
            Assert.Equal(ReportKinds.Certification, artifacts.Single().Kind);
            pdf = await File.ReadAllBytesAsync(artifacts.Single().PdfPath);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "Typst native library was not restored. Run tests with `-r linux-x64`.",
                ex);
        }

        using var card = FakePivCard.CreateRsa2048();
        Assert.True(
            PdfPadesSignature.TryPrepare(pdf, "Fake PIV Signature", DateTimeOffset.UnixEpoch, out var prepared, out var prepError),
            prepError);
        var cms = PivSigner.SignCms(card, prepared.SignedBytes, FakePivCard.DefaultPin, DateTimeOffset.UnixEpoch);
        Assert.True(cms.Succeeded, cms.Error);
        Assert.True(prepared.TryEmbed(cms.Signature!, out var signed, out var embedError), embedError);
        Assert.True(PdfPadesSignature.TryVerify(signed, out var verifyError), verifyError);
        Assert.True(
            PdfPadesSignature.TryPrepare(signed, "Second", DateTimeOffset.UnixEpoch, out var second, out var secondError),
            secondError);
        Assert.NotEmpty(second.SignedBytes);
    }

    [Fact]
    public void Indirect_kids_array_attaches_signature_field()
    {
        using var rsa = RSA.Create(2048);
        using var cert = CreateCert(rsa);
        var pdf = PdfPadesSignature.CreateMinimalPdf(indirectKids: true);
        Assert.True(
            PdfPadesSignature.TryPrepare(pdf, "Indirect Kids", DateTimeOffset.UnixEpoch, out var prepared, out var prepError),
            prepError);
        Assert.True(prepared.TryEmbed(SignCms(prepared.SignedBytes, cert, rsa), out var signed, out var embedError), embedError);
        Assert.True(PdfPadesSignature.TryVerify(signed, out var verifyError), verifyError);
    }

    [Fact]
    public void Second_incremental_signature_walks_prev_xref()
    {
        using var rsa = RSA.Create(2048);
        using var cert = CreateCert(rsa);
        var pdf = PdfPadesSignature.CreateMinimalPdf();
        Assert.True(PdfPadesSignature.TryPrepare(pdf, "First", DateTimeOffset.UnixEpoch, out var first, out var firstError), firstError);
        Assert.True(first.TryEmbed(SignCms(first.SignedBytes, cert, rsa), out var once, out var firstEmbed), firstEmbed);
        Assert.True(PdfPadesSignature.TryPrepare(once, "Second", DateTimeOffset.UnixEpoch, out var second, out var secondError), secondError);
        Assert.True(second.TryEmbed(SignCms(second.SignedBytes, cert, rsa), out var twice, out var secondEmbed), secondEmbed);
        Assert.True(PdfPadesSignature.TryVerify(twice, out var verifyError), verifyError);
        Assert.Contains("(HardwareTestCertification-2)"u8, twice);
    }

    [Fact]
    public void Trailing_bytes_outside_byterange_fail_verify()
    {
        using var rsa = RSA.Create(2048);
        using var cert = CreateCert(rsa);
        var pdf = PdfPadesSignature.CreateMinimalPdf();
        Assert.True(PdfPadesSignature.TryPrepare(pdf, "X", DateTimeOffset.UnixEpoch, out var prepared, out _));
        Assert.True(prepared.TryEmbed(SignCms(prepared.SignedBytes, cert, rsa), out var signed, out _));
        var padded = new byte[signed.Length + 8];
        signed.CopyTo(padded, 0);
        "tamper!!"u8.CopyTo(padded.AsSpan(signed.Length));
        Assert.False(PdfPadesSignature.TryVerify(padded, out var error));
        Assert.Contains("whole file", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reserved_cms_slot_fits_large_piv_payload()
    {
        Assert.True(PdfPadesSignature.ReservedCmsBytes >= 16 * 1024);
        var pdf = PdfPadesSignature.CreateMinimalPdf();
        Assert.True(PdfPadesSignature.TryPrepare(pdf, "Large", DateTimeOffset.UnixEpoch, out var prepared, out var prepError), prepError);
        var large = new byte[9 * 1024];
        large[0] = 0x30;
        large[1] = 0x80;
        Assert.True(prepared.TryEmbed(large, out var signed, out var embedError), embedError);
        Assert.NotEmpty(signed);
    }

    [Fact]
    public void Rejects_non_pdf()
    {
        Assert.False(PdfPadesSignature.TryPrepare("%PDF-nope"u8.ToArray(), "X", DateTimeOffset.UnixEpoch, out _, out var error));
        Assert.Contains("PDF", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Piv_rsa_card_embeds_verifiable_pades()
    {
        using var card = FakePivCard.CreateRsa2048();
        var pdf = PdfPadesSignature.CreateMinimalPdf();
        Assert.True(PdfPadesSignature.TryPrepare(pdf, "Fake PIV Signature", DateTimeOffset.UnixEpoch, out var prepared, out var prepError), prepError);
        var cms = PivSigner.SignCms(card, prepared.SignedBytes, FakePivCard.DefaultPin, DateTimeOffset.UnixEpoch);
        Assert.True(cms.Succeeded, cms.Error);
        Assert.True(prepared.TryEmbed(cms.Signature!, out var signed, out var embedError), embedError);
        Assert.True(PdfPadesSignature.TryVerify(signed, out var verifyError), verifyError);
    }

    [Fact]
    public void Piv_ecc_card_embeds_verifiable_pades()
    {
        using var card = FakePivCard.CreateEccP256();
        var pdf = PdfPadesSignature.CreateMinimalPdf();
        Assert.True(PdfPadesSignature.TryPrepare(pdf, "Fake PIV ECC", DateTimeOffset.UnixEpoch, out var prepared, out var prepError), prepError);
        var cms = PivSigner.SignCms(card, prepared.SignedBytes, FakePivCard.DefaultPin, DateTimeOffset.UnixEpoch);
        Assert.True(cms.Succeeded, cms.Error);
        Assert.Equal(AttestationAlgorithm.PivEcdsaSha256, cms.Algorithm);
        Assert.True(prepared.TryEmbed(cms.Signature!, out var signed, out var embedError), embedError);
        Assert.True(PdfPadesSignature.TryVerify(signed, out var verifyError), verifyError);
    }

    private static X509Certificate2 CreateCert(RSA rsa)
    {
        var req = new CertificateRequest("CN=Software PAdES", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static byte[] SignCms(byte[] document, X509Certificate2 cert, RSA rsa)
    {
        var content = new System.Security.Cryptography.Pkcs.ContentInfo(document);
        var signedCms = new System.Security.Cryptography.Pkcs.SignedCms(content, detached: true);
        var signer = new System.Security.Cryptography.Pkcs.CmsSigner(
            System.Security.Cryptography.Pkcs.SubjectIdentifierType.IssuerAndSerialNumber,
            cert,
            rsa)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        signedCms.ComputeSignature(signer, silent: true);
        return signedCms.Encode();
    }

    private static int IndexOf(byte[] data, byte[] needle)
    {
        for (var i = 0; i <= data.Length - needle.Length; i++)
        {
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
