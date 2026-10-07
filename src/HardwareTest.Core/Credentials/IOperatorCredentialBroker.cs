namespace HardwareTest.Core.Credentials;

/// Public chip/tap identity capture, with no private-key operations.
public interface IOperatorCredentialPresenceBroker
{
    /// Operator-facing reader status (no reader, waiting, mock, …).
    string StatusText { get; }

    /// Waits for a chip insert or contactless tap and reads identity.
    Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// Current mock payload signing or physical PKCS11 probe and embedded-PDF signing.
public interface IOperatorCredentialBroker : IOperatorCredentialPresenceBroker
{
    /// True when this broker is the in-process mock (CI / no reader).
    bool IsMock { get; }

    /// True when this broker implements signing (PIN may still be required on the card).
    bool CanSign { get; }

    /// Algorithm id written on signed sidecars when the broker uses a fixed algorithm.
    string? SigningAlgorithm { get; }

    /// True when the active physical broker signs complete PDFs through iText.
    bool CanSignPdf => false;

    /// Mock HMAC signing or a physical PIN/capability probe; never a detached physical report signature.
    /// PIN is used only for this call and is not stored.
    Task<CredentialSignResult> TrySignPayloadAsync(
        byte[] payload,
        OperatorCredential credential,
        string? pin = null,
        CancellationToken cancellationToken = default);
}

/// Hardware-backed broker that lets the PDF library construct and embed a complete PAdES signature.
public interface IEmbeddedPdfSigningBroker
{
    Task<CredentialSignResult> TrySignPdfAsync(
        byte[] pdf,
        OperatorCredential credential,
        string? pin = null,
        DateTimeOffset? signingTime = null,
        CancellationToken cancellationToken = default);
}
