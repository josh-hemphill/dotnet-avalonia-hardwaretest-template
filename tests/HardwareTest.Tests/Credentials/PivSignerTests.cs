using System.Security.Cryptography;
using HardwareTest.Core.Credentials;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class PivSignerTests
{
    [Fact]
    public void PadPin_ff_pads_to_eight_bytes()
    {
        Assert.Equal([0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0xFF, 0xFF], PivApdu.PadPin("123456"));
    }

    [Fact]
    public void Sha256DigestInfo_is_rfc8017_prefix_plus_hash()
    {
        var hash = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var info = PivApdu.Sha256DigestInfo(hash);
        Assert.Equal(51, info.Length);
        Assert.Equal(hash, info[^32..]);
        Assert.True(info.AsSpan(0, PivApdu.Sha256DigestInfoPrefix.Length).SequenceEqual(PivApdu.Sha256DigestInfoPrefix));
    }

    [Fact]
    public void TryExtractCertificateDer_unwraps_piv_53_70_object()
    {
        var der = new byte[] { 0x30, 0x03, 0x02, 0x01, 0x00 };
        var inner = PivApdu.Concat(PivApdu.EncodeTlv(0x70, der), PivApdu.EncodeTlv(0x71, [0x00]));
        var body = PivApdu.EncodeTlv(0x53, inner);
        Assert.Equal(der, PivApdu.TryExtractCertificateDer(body));
    }

    [Fact]
    public void Sign_rsa_2048_round_trips_with_pin()
    {
        using var card = FakePivCard.CreateRsa2048();
        var payload = "pdf-hash:run-hash"u8.ToArray();
        var result = PivSigner.Sign(card, payload, FakePivCard.DefaultPin);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(AttestationAlgorithm.PivRsaPkcs1Sha256, result.Algorithm);
        Assert.NotNull(result.CertificateDer);
        Assert.True(PivSigner.Verify(payload, result.Signature!, result.CertificateDer!, result.Algorithm!));
    }

    [Fact]
    public void Sign_ecc_p256_round_trips_with_pin()
    {
        using var card = FakePivCard.CreateEccP256();
        var payload = "ecc-payload"u8.ToArray();
        var result = PivSigner.Sign(card, payload, FakePivCard.DefaultPin);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(AttestationAlgorithm.PivEcdsaSha256, result.Algorithm);
        Assert.True(PivSigner.Verify(payload, result.Signature!, result.CertificateDer!, result.Algorithm!));
    }

    [Fact]
    public void Sign_without_pin_on_9c_returns_pin_required()
    {
        using var card = FakePivCard.CreateRsa2048();
        var result = PivSigner.Sign(card, "x"u8.ToArray(), pin: null);
        Assert.True(result.PinRequired);
        Assert.False(result.Succeeded);
        Assert.Contains("PIN", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sign_wrong_pin_reports_retries()
    {
        using var card = FakePivCard.CreateRsa2048();
        card.PinRetries = 3;
        var result = PivSigner.Sign(card, "x"u8.ToArray(), "000000");
        Assert.False(result.Succeeded);
        Assert.Equal(2, result.PinRetriesRemaining);
        Assert.Contains("Incorrect PIN", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sign_card_auth_slot_does_not_need_pin()
    {
        using var card = FakePivCard.CreateCardAuthRsa();
        var payload = "card-auth"u8.ToArray();
        var result = PivSigner.Sign(card, payload, pin: null);
        Assert.True(result.Succeeded, result.Error);
        Assert.True(PivSigner.Verify(payload, result.Signature!, result.CertificateDer!, result.Algorithm!));
    }

    [Fact]
    public void Sign_tampered_payload_does_not_verify()
    {
        using var card = FakePivCard.CreateRsa2048();
        var payload = "original"u8.ToArray();
        var result = PivSigner.Sign(card, payload, FakePivCard.DefaultPin);
        Assert.True(result.Succeeded, result.Error);
        Assert.False(PivSigner.Verify("other"u8.ToArray(), result.Signature!, result.CertificateDer!, result.Algorithm!));
    }

    [Fact]
    public void Contactless_pin_blocked_returns_insert_chip_message()
    {
        using var card = FakePivCard.CreateRsa2048();
        card.FailVerifyAsSecurityStatus = true;
        var result = PivSigner.Sign(card, "x"u8.ToArray(), FakePivCard.DefaultPin);
        Assert.False(result.Succeeded);
        Assert.Contains("Insert the chip", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
