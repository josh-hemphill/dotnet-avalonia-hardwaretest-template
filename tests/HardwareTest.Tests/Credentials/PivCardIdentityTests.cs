using HardwareTest.Core.Credentials;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class PivCardIdentityTests
{
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
