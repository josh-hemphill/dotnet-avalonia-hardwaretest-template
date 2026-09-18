using System.Globalization;

namespace HardwareTest.Core.Credentials;

/// Formats badge identity for Typst inputs and Results chrome without embedding signature bytes.
public static class ReportAttestationStamp
{
    /// Maps an attestation (or a pre-sign overlay) to Typst `attestationKind` / `attestationDetail` / `attestationAt`.
    public static void FormatInputs(
        ReportAttestation? attestation,
        out string kind,
        out string detail,
        out string capturedAt)
    {
        if (attestation is null || string.IsNullOrWhiteSpace(attestation.DisplayName))
        {
            kind = string.Empty;
            detail = string.Empty;
            capturedAt = string.Empty;
            return;
        }

        kind = string.IsNullOrWhiteSpace(attestation.Kind) ? attestation.Transport : attestation.Kind;
        detail = $"{attestation.DisplayName} ({attestation.Transport}, {attestation.Serial})";
        capturedAt = attestation.CapturedAt == default
            ? string.Empty
            : attestation.CapturedAt.ToString("u", CultureInfo.InvariantCulture);
    }

    /// One-line Results summary of who certified a report.
    public static string FormatSummary(ReportAttestation attestation)
    {
        ArgumentNullException.ThrowIfNull(attestation);
        var at = attestation.CapturedAt == default
            ? string.Empty
            : $" at {attestation.CapturedAt.ToString("u", CultureInfo.InvariantCulture)}";
        var kind = string.IsNullOrWhiteSpace(attestation.Kind) ? attestation.Transport : attestation.Kind;
        return $"{kind}: {attestation.DisplayName} ({attestation.Transport}, {attestation.Serial}){at}";
    }
}
