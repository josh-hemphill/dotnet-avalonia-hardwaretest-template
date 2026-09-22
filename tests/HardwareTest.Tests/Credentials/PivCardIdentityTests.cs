using System.Security.Cryptography.X509Certificates;
using HardwareTest.Core.Credentials;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class PivCardIdentityTests
{
    [Fact]
    public void TryReadSignatureCertificateThumbprint_reads_only_9c()
    {
        using var card = FakePivCard.CreateRsa2048(slot: PivApdu.SlotSignature);
        using var certificate = X509CertificateLoader.LoadCertificate(card.CertDer);

        var thumbprint = PivCardIdentity.TryReadSignatureCertificateThumbprint(card);

        Assert.Equal(certificate.Thumbprint, thumbprint);
    }

    [Fact]
    public void TryReadSignatureCertificateThumbprint_rejects_non_9c_slot()
    {
        using var card = FakePivCard.CreateRsa2048(slot: PivApdu.SlotAuthentication);

        Assert.Null(PivCardIdentity.TryReadSignatureCertificateThumbprint(card));
    }

    [Fact]
    public void TryRead_uses_certificate_subject_not_uid()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=Jane Doe");
        card.Uid = [0xAA, 0xBB, 0xCC, 0xDD];

        var (serial, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("AABBCCDD", serial);
        Assert.Equal("Jane Doe", name);
    }

    [Fact]
    public void TryRead_uses_san_email_when_subject_is_generic()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=Card Authentication",
            emailSan: "jane.doe@agency.gov");
        card.Uid = [0x01, 0x02, 0x03, 0x04];

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("jane.doe@agency.gov", name);
    }

    [Fact]
    public void TryRead_prefers_subject_over_san_email()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=Jane Doe",
            emailSan: "jane.doe@agency.gov");

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("Jane Doe", name);
    }

    [Fact]
    public void TryRead_uses_san_email_when_subject_is_hex_id()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotSignature,
            subject: "CN=AABBCCDD",
            emailSan: "operator@bench.example");

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("operator@bench.example", name);
    }

    [Fact]
    public void TryRead_keeps_cac_style_subject()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=SMITH.JANE.Q.1234567890");

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("SMITH.JANE.Q.1234567890", name);
    }

    [Fact]
    public void TryRead_uses_printed_name_when_certificates_are_generic()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotCardAuth,
            subject: "CN=Card Authentication");
        card.PrintedInformation = PivApdu.Concat(
            PivApdu.EncodeTlv(PivApdu.PrintedNameTag, "DOE, JANE"u8.ToArray()),
            PivApdu.EncodeTlv(0x05, "AGENCY-9999"u8.ToArray()));

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("DOE, JANE", name);
    }

    [Fact]
    public void TryRead_does_not_treat_printed_agency_serial_as_name()
    {
        using var card = FakePivCard.CreateRsa2048(slot: PivApdu.SlotCardAuth);
        card.OmitCertificate = true;
        card.PrintedInformation = PivApdu.EncodeTlv(0x05, "AGENCY-9999"u8.ToArray());
        card.Uid = [0xDE, 0xAD, 0xBE, 0xEF];

        var (serial, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("DEADBEEF", serial);
        Assert.Null(name);
    }

    [Fact]
    public void TryRead_uses_dod_upn_without_dotted_host()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=Card Authentication",
            upnSan: "1234567890@mil");

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("1234567890@mil", name);
    }

    [Fact]
    public void TryRead_uses_upn_when_subject_is_generic_and_rfc822_is_absent()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=PIV Authentication",
            upnSan: "jane.doe@agency.gov");

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("jane.doe@agency.gov", name);
    }

    [Fact]
    public void TryRead_prefers_piv_auth_subject_over_card_auth()
    {
        using var auth = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=Jane Doe");
        using var cardAuth = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotCardAuth,
            subject: "CN=Other Person");
        using var card = new CompositePivCard(cardAuth, auth);

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("Jane Doe", name);
    }

    [Fact]
    public void TryRead_prefers_person_name_on_later_slot_over_digit_upn()
    {
        using var auth = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=Card Authentication",
            upnSan: "1234567890@mil");
        using var signature = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotSignature,
            subject: "CN=Jane Doe");
        using var card = new CompositePivCard(auth, signature);

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("Jane Doe", name);
    }

    [Fact]
    public void TryRead_uses_key_management_subject_when_auth_is_generic()
    {
        using var auth = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=PIV Authentication");
        using var key = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotKeyManagement,
            subject: "CN=Jane Doe");
        using var card = new CompositePivCard(auth, key);

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("Jane Doe", name);
    }

    [Fact]
    public void TryRead_prefers_printed_name_over_digit_upn()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=Card Authentication",
            upnSan: "1234567890@mil");
        card.PrintedInformation = PivApdu.EncodeTlv(PivApdu.PrintedNameTag, "DOE, JANE"u8.ToArray());

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("DOE, JANE", name);
    }

    [Fact]
    public void TryRead_uses_given_name_and_surname_when_cn_is_generic()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "G=Jane, SN=Doe, CN=Card Authentication");

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("Jane Doe", name);
    }

    [Fact]
    public void TryRead_unwraps_gzip_certificate_object()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=Jane Doe");
        card.GzipCertificate = true;

        var (_, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("Jane Doe", name);
    }

    [Fact]
    public void TryRead_retries_certificate_reads_after_transient_failures()
    {
        using var card = FakePivCard.CreateRsa2048(
            slot: PivApdu.SlotAuthentication,
            subject: "CN=Jane Doe");
        card.Uid = [0xAA, 0xBB, 0xCC, 0xDD];
        card.RemainingCertificateFailures = 2;

        var (serial, name) = PivCardIdentity.TryRead(card);

        Assert.Equal("AABBCCDD", serial);
        Assert.Equal("Jane Doe", name);
    }

    [Theory]
    [InlineData("Jane Doe", true)]
    [InlineData("jane.doe@agency.gov", true)]
    [InlineData("1234567890@mil", false)]
    [InlineData("Card AABBCCDD", false)]
    [InlineData("AABBCCDD", false)]
    public void Presence_is_settled_only_for_person_name_or_email(string display, bool settled)
        => Assert.Equal(settled, PivPresenceIdentity.IsSettled(display));

    [Fact]
    public void Presence_prefers_a_person_name_over_card_hex_fallback()
    {
        var hex = new OperatorCredential { DisplayName = "Card AABBCCDD", Serial = "AABBCCDD" };
        var named = new OperatorCredential { DisplayName = "Jane Doe", Serial = "AABBCCDD" };
        Assert.Equal("Jane Doe", PivPresenceIdentity.Better(hex, named).DisplayName);
        Assert.Equal("Card AABBCCDD", PivPresenceIdentity.Better(hex, hex).DisplayName);
    }

    [Fact]
    public void Presence_equal_quality_prefers_the_live_serial()
    {
        var atr = new OperatorCredential { DisplayName = "Card AABBCCDD", Serial = "ATR" };
        var uid = new OperatorCredential { DisplayName = "Card AABBCCDD", Serial = "UID" };
        Assert.Equal("UID", PivPresenceIdentity.Better(atr, uid).Serial);

        var upn = new OperatorCredential { DisplayName = "1234567890@mil", Serial = "UID1" };
        var hex = new OperatorCredential { DisplayName = "Card AABBCCDD", Serial = "UID2" };
        Assert.Equal("UID1", PivPresenceIdentity.Better(upn, hex).Serial);
    }

    [Fact]
    public void Presence_poll_returns_uid_fallback_after_settle_when_later_captures_fail()
    {
        var poll = new PivPresenceIdentity.Poll();
        var t0 = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
        var uid = Capture("Card AABBCCDD", "AABBCCDD");
        var gone = new CredentialCaptureResult { Error = "Present a badge: insert chip or tap the reader." };

        Assert.Null(poll.Observe(uid, t0, out var reading));
        Assert.Contains("Reading badge identity", reading, StringComparison.Ordinal);

        Assert.Null(poll.Observe(gone, t0.AddSeconds(1), out var stillReading));
        Assert.Contains("Reading badge identity", stillReading, StringComparison.Ordinal);

        var done = poll.Observe(gone, t0 + PivPresenceIdentity.Settle, out var present);
        Assert.Equal("Card AABBCCDD", done?.Credential?.DisplayName);
        Assert.Equal("AABBCCDD", done?.Credential?.Serial);
        Assert.Contains("Credential present", present, StringComparison.Ordinal);
    }

    [Fact]
    public void Presence_poll_returns_settled_name_before_settle_window()
    {
        var poll = new PivPresenceIdentity.Poll();
        var t0 = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(poll.Observe(Capture("Card AABBCCDD", "AABBCCDD"), t0, out _));

        var done = poll.Observe(Capture("Jane Doe", "AABBCCDD"), t0.AddMilliseconds(250), out _);
        Assert.Equal("Jane Doe", done?.Credential?.DisplayName);
    }

    [Fact]
    public void Presence_poll_does_not_return_before_settle_when_only_uid_is_seen()
    {
        var poll = new PivPresenceIdentity.Poll();
        var t0 = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(poll.Observe(Capture("Card AABBCCDD", "AABBCCDD"), t0, out _));
        Assert.Null(poll.Observe(Capture("Card AABBCCDD", "AABBCCDD"), t0.AddSeconds(1.9), out _));
    }

    private static CredentialCaptureResult Capture(string displayName, string serial)
        => new()
        {
            Credential = new OperatorCredential
            {
                DisplayName = displayName,
                Serial = serial,
                Transport = CredentialTransport.Contactless,
            },
        };

    [Theory]
    [InlineData("Jane Doe", true)]
    [InlineData("SMITH.JANE.Q.1234567890", true)]
    [InlineData("Card Authentication", false)]
    [InlineData("PIV Card Authentication", false)]
    [InlineData("PIV Authentication", false)]
    [InlineData("Digital Signature", false)]
    [InlineData("Key Management", false)]
    [InlineData("AABBCCDD", false)]
    [InlineData("Card AABBCCDD", false)]
    [InlineData("Card AA:BB:CC:DD", false)]
    [InlineData("AA:BB:CC:DD", false)]
    public void Useful_person_name_rejects_slot_labels_and_card_ids(string value, bool expected)
        => Assert.Equal(expected, PivCertificateName.IsUsefulPersonName(value));

    [Theory]
    [InlineData("jane.doe@agency.gov", true)]
    [InlineData("edipi@mil", true)]
    [InlineData("1234567890@mil", true)]
    [InlineData("not-an-email", false)]
    [InlineData("user@", false)]
    public void Email_accepts_rfc822_and_dod_upn(string value, bool expected)
        => Assert.Equal(expected, PivCertificateName.IsEmail(value));
}
