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
}
