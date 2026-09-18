using HardwareTest.Core.Credentials;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class ReportAttestationStampTests
{
    [Fact]
    public void FormatInputs_empty_when_missing_display_name()
    {
        ReportAttestationStamp.FormatInputs(null, out var kind, out var detail, out var at);
        Assert.Equal(string.Empty, kind);
        Assert.Equal(string.Empty, detail);
        Assert.Equal(string.Empty, at);

        ReportAttestationStamp.FormatInputs(
            new ReportAttestation { DisplayName = " ", Serial = "X" },
            out kind,
            out detail,
            out at);
        Assert.Equal(string.Empty, kind);
        Assert.Equal(string.Empty, detail);
        Assert.Equal(string.Empty, at);
    }

    [Fact]
    public void FormatInputs_uses_transport_when_kind_blank()
    {
        var captured = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        ReportAttestationStamp.FormatInputs(
            new ReportAttestation
            {
                Kind = string.Empty,
                DisplayName = "Jane Certifier",
                Serial = "CARD-1",
                Transport = CredentialTransport.Contact,
                CapturedAt = captured,
            },
            out var kind,
            out var detail,
            out var at);
        Assert.Equal(CredentialTransport.Contact, kind);
        Assert.Equal("Jane Certifier (contact, CARD-1)", detail);
        Assert.Equal("2026-09-18 12:00:00Z", at);
    }

    [Fact]
    public void FormatSummary_includes_party_and_time()
    {
        var summary = ReportAttestationStamp.FormatSummary(new ReportAttestation
        {
            Kind = AttestationKind.Signed,
            DisplayName = "Jane Certifier",
            Serial = "CARD-1",
            Transport = CredentialTransport.Contact,
            CapturedAt = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero),
        });
        Assert.Equal("signed: Jane Certifier (contact, CARD-1) at 2026-09-18 12:00:00Z", summary);
    }

    [Fact]
    public void FormatSummary_uses_transport_when_kind_blank()
    {
        var summary = ReportAttestationStamp.FormatSummary(new ReportAttestation
        {
            Kind = string.Empty,
            DisplayName = "Jane Certifier",
            Serial = "CARD-1",
            Transport = CredentialTransport.Contact,
        });
        Assert.Equal("contact: Jane Certifier (contact, CARD-1)", summary);
    }
}
