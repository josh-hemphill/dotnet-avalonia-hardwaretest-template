namespace HardwareTest.Core.Credentials;

public enum CredentialFailureKind
{
    Unavailable, CertificateMismatch, PinRequired, WrongPin, PinLocked, Cancelled,
    CardRemoved, SigningFailed, ConfigurationError,
}

/// Owns a selected, authenticated signer until report compilation and issuance finish.
public interface IPreparedCredentialSession : IOperatorCredentialBroker, IDisposable { }

public sealed record CredentialPreparationResult(IPreparedCredentialSession? Session, CredentialSignResult? Failure)
{
    public static CredentialPreparationResult Ready(IPreparedCredentialSession session) => new(session, null);
    public static CredentialPreparationResult Failed(CredentialSignResult failure) => new(null, failure);

    internal static Task<CredentialPreparationResult> PrepareLegacyAsync(
        IOperatorCredentialBroker broker, OperatorCredential credential, string? pin, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!broker.CanSign) return Task.FromResult(Failed(CredentialSignResult.Unavailable("This credential cannot sign.")));
        if (broker.RequiresPin && string.IsNullOrEmpty(pin))
            return Task.FromResult(Failed(CredentialSignResult.NeedPin("Enter badge PIN to sign.")));
        IPreparedCredentialSession session = broker is IEmbeddedPdfSigningBroker embedded
            ? new LegacyEmbeddedSession(broker, embedded) : new LegacySession(broker);
        return Task.FromResult(Ready(session));
    }

    private class LegacySession(IOperatorCredentialBroker broker) : IPreparedCredentialSession
    {
        public bool IsMock => broker.IsMock;
        public bool CanSign => broker.CanSign;
        public bool ProducesCms => broker.ProducesCms;
        public string? SigningAlgorithm => broker.SigningAlgorithm;
        public string StatusText => broker.StatusText;
        public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("A prepared signer cannot capture another card.");
        public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
            => broker.TrySignPayloadAsync(payload, credential, pin, cancellationToken);
        public Task<CredentialSignResult> TrySignDocumentAsync(byte[] document, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
            => broker.TrySignDocumentAsync(document, credential, pin, signingTime, cancellationToken);
        public void Dispose() { }
    }
    private sealed class LegacyEmbeddedSession(IOperatorCredentialBroker broker, IEmbeddedPdfSigningBroker embedded)
        : LegacySession(broker), IEmbeddedPdfSigningBroker
    {
        public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
            => embedded.TrySignPdfAsync(pdf, credential, pin, signingTime, cancellationToken);
    }
}
