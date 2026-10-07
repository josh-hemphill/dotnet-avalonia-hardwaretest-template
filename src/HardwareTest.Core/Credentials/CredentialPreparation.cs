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

    internal static async Task<CredentialPreparationResult> PrepareMockAsync(
        IOperatorCredentialBroker broker, OperatorCredential credential, string? pin, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!broker.IsMock || !broker.CanSign)
            return Failed(CredentialSignResult.Failure(CredentialFailureKind.ConfigurationError,
                "This broker does not support prepared signing.", "preparation"));
        var probe = await broker.TrySignPayloadAsync("mock-preparation"u8.ToArray(), credential, pin, cancellationToken).ConfigureAwait(false);
        if (!probe.Succeeded) return Failed(probe);
        return Ready(new MockSession(broker, credential));
    }

    private sealed class MockSession(IOperatorCredentialBroker broker, OperatorCredential selected) : IPreparedCredentialSession
    {
        private bool _disposed;
        public bool IsMock => true;
        public bool CanSign => true;
        public string? SigningAlgorithm => broker.SigningAlgorithm;
        public string StatusText => "Mock signing session prepared.";
        public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("A prepared signer cannot capture another card.");
        public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(selected.Serial, credential.Serial, StringComparison.Ordinal)
                || !string.Equals(selected.Thumbprint, credential.Thumbprint, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CredentialSignResult.Failure(CredentialFailureKind.CertificateMismatch,
                    "Mock signing session does not match the captured identity.", "signing"));
            return broker.TrySignPayloadAsync(payload, selected, pin, cancellationToken);
        }
        public void Dispose() => _disposed = true;
    }
}
