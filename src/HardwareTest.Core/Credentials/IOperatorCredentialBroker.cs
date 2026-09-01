namespace HardwareTest.Core.Credentials;

/// Cross-platform chip (contact) and tap (contactless) credential capture.
public interface IOperatorCredentialBroker
{
    /// True when this broker is the in-process mock (CI / no reader).
    bool IsMock { get; }

    /// True when TrySignPayloadAsync can produce a signature.
    bool CanSign { get; }

    /// Algorithm id written on signed sidecars; null when this broker cannot sign.
    string? SigningAlgorithm { get; }

    /// Operator-facing reader status (no reader, waiting, mock, …).
    string StatusText { get; }

    /// Waits for a chip insert or contactless tap and reads identity.
    Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// Signs payload with the presented credential. Returns null when signing is unavailable.
    Task<byte[]?> TrySignPayloadAsync(
        byte[] payload,
        OperatorCredential credential,
        CancellationToken cancellationToken = default);
}
