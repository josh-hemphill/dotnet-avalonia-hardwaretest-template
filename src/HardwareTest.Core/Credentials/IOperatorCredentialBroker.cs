namespace HardwareTest.Core.Credentials;

/// Cross-platform chip (contact) and tap (contactless) credential capture.
public interface IOperatorCredentialBroker
{
    /// True when this broker is the in-process mock (CI / no reader).
    bool IsMock { get; }

    /// True when this broker implements signing (PIN may still be required on the card).
    bool CanSign { get; }

    /// Algorithm id written on signed sidecars when the broker uses a fixed algorithm.
    string? SigningAlgorithm { get; }

    /// True when TrySignDocumentAsync returns a CMS/PKCS#7 (PAdES) rather than a raw payload MAC.
    bool ProducesCms => false;

    /// Operator-facing reader status (no reader, waiting, mock, …).
    string StatusText { get; }

    /// Waits for a chip insert or contactless tap and reads identity.
    Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// Signs payload with the presented credential. PIN is used only for this call and is not stored.
    Task<CredentialSignResult> TrySignPayloadAsync(
        byte[] payload,
        OperatorCredential credential,
        string? pin = null,
        CancellationToken cancellationToken = default);

    /// CMS/PKCS#7 detached signature over document bytes (PAdES ByteRange). PIN is not stored.
    Task<CredentialSignResult> TrySignDocumentAsync(
        byte[] document,
        OperatorCredential credential,
        string? pin = null,
        DateTimeOffset? signingTime = null,
        CancellationToken cancellationToken = default)
        => TrySignPayloadAsync(document, credential, pin, cancellationToken);
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
