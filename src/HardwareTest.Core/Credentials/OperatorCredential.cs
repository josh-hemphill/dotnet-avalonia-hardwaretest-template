namespace HardwareTest.Core.Credentials;

/// Transport used to present a credential (chip vs tap).
public static class CredentialTransport
{
    public const string Contact = "contact";
    public const string Contactless = "contactless";
}

/// How the responsible party was bound to a report.
public static class AttestationKind
{
    public const string Signed = "signed";
    public const string Presence = "presence";
}

/// Sidecar algorithm ids (presence is not a cryptographic signature).
public static class AttestationAlgorithm
{
    public const string Presence = "presence";
    public const string MockHmac = "HMAC-SHA256-mock";
}

/// Identity read from a smart card chip or contactless tap.
public sealed class OperatorCredential
{
    public string DisplayName { get; init; } = string.Empty;
    public string Serial { get; init; } = string.Empty;
    public string Transport { get; init; } = CredentialTransport.Contactless;
    public string? ReaderName { get; init; }
    public string? Thumbprint { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
}

/// Persisted attestation for one report kind on a run.
public sealed class ReportAttestation
{
    public string Kind { get; set; } = AttestationKind.Presence;
    public string ReportKind { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public string Transport { get; set; } = CredentialTransport.Contactless;
    public string? Thumbprint { get; set; }
    public string PdfSha256 { get; set; } = string.Empty;
    public string RunJsonSha256 { get; set; } = string.Empty;
    public string? SidecarPath { get; set; }
    public string? Algorithm { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

/// Outcome of WaitForPresence / TrySign.
public sealed class CredentialCaptureResult
{
    public OperatorCredential? Credential { get; init; }
    public string? Error { get; init; }
    public bool Succeeded => Credential is not null && string.IsNullOrWhiteSpace(Error);
}

/// Outcome of attesting a report before export or print.
public sealed class ReportAttestationResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public ReportAttestation? Attestation { get; init; }
}
