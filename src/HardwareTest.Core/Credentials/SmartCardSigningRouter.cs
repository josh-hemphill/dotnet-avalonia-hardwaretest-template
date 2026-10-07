using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.Credentials;

/// Serializes card presence and retained signing sessions while selecting one provider from a settings snapshot.
public sealed class SmartCardSigningRouter : IOperatorCredentialBroker, IEmbeddedPdfSigningBroker
{
    private readonly AppSettings _settings;
    private readonly IOperatorCredentialPresenceBroker _presence;
    private readonly Func<AppSettings, IOperatorCredentialBroker> _windows;
    private readonly Func<AppSettings, IOperatorCredentialBroker> _pkcs11;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public SmartCardSigningRouter(AppSettings settings, INativeSigningDialogOwner? owner = null, IClock? clock = null, IOperatorCredentialPresenceBroker? presence = null)
    {
        _settings = settings; _presence = presence ?? new PcscOperatorCredentialBroker(clock);
        _windows = _ => new WindowsOperatorCredentialBroker(owner, clock, _presence);
        _pkcs11 = snapshot => new Pkcs11OperatorCredentialBroker(snapshot, clock, _presence);
    }
    internal SmartCardSigningRouter(AppSettings settings, IOperatorCredentialPresenceBroker presence,
        Func<AppSettings, IOperatorCredentialBroker> windows, Func<AppSettings, IOperatorCredentialBroker> pkcs11)
    { _settings = settings; _presence = presence; _windows = windows; _pkcs11 = pkcs11; }
    public bool IsMock => false;
    public bool CanSign => true;
    public bool CanSignPdf => true;
    public string? SigningAlgorithm => null;
    public string StatusText => _presence.StatusText;
    public async Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await _presence.WaitForPresenceAsync(timeout, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    public async Task<CredentialPreparationResult> PrepareSigningAsync(OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var transferred = false;
        try
        {
            var snapshot = new AppSettings { PhysicalSigningBackend = _settings.PhysicalSigningBackend, Pkcs11LibraryPath = _settings.Pkcs11LibraryPath };
            if (!Enum.IsDefined(snapshot.PhysicalSigningBackend))
                return CredentialPreparationResult.Failed(CredentialSignResult.Failure(CredentialFailureKind.ConfigurationError,
                    "Unknown physical signing backend.", "configuration"));
            var result = snapshot.PhysicalSigningBackend == PhysicalSigningBackend.Pkcs11
                ? await _pkcs11(snapshot).PrepareSigningAsync(credential, pin, cancellationToken).ConfigureAwait(false)
                : await _windows(snapshot).PrepareSigningAsync(credential, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.Session is null) return result;
            if (result.Session is not { IsMock: false, CanSign: true, CanSignPdf: true }
                || result.Session is not IEmbeddedPdfSigningBroker)
            {
                Pkcs11OperatorCredentialBroker.DisposeResource(result.Session);
                return CredentialPreparationResult.Failed(CredentialSignResult.Failure(CredentialFailureKind.ConfigurationError,
                    "The selected physical backend does not support prepared embedded PDF signing.", "preparation"));
            }
            var retained = new RoutedSession(result.Session, _gate);
            transferred = true;
            return CredentialPreparationResult.Ready(retained);
        }
        finally { if (!transferred) _gate.Release(); }
    }
    public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default) => SignAsync(payload, credential, pin, null, false, cancellationToken);
    public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default) => SignAsync(pdf, credential, pin, signingTime, true, cancellationToken);
    private async Task<CredentialSignResult> SignAsync(byte[] data, OperatorCredential credential, string? pin, DateTimeOffset? at, bool pdf, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (data.Length == 0) return CredentialSignResult.Failed("Nothing to sign.");
        var prepared = await PrepareSigningAsync(credential, pin, ct).ConfigureAwait(false);
        using var session = prepared.Session;
        if (session is null) return prepared.Failure!;
        return pdf
            ? await ((IEmbeddedPdfSigningBroker)session).TrySignPdfAsync(data, credential, pin, at, ct).ConfigureAwait(false)
            : await session.TrySignPayloadAsync(data, credential, pin, ct).ConfigureAwait(false);
    }
    private sealed class RoutedSession(IPreparedCredentialSession session, SemaphoreSlim gate) : IPreparedCredentialSession, IEmbeddedPdfSigningBroker
    {
        private int _disposed;
        public bool IsMock => session.IsMock;
        public bool CanSign => session.CanSign;
        public bool CanSignPdf => session.CanSignPdf;
        public string? SigningAlgorithm => session.SigningAlgorithm;
        public string StatusText => session.StatusText;
        public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default) => session.TrySignPayloadAsync(payload, credential, pin, cancellationToken);
        public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
            => session is IEmbeddedPdfSigningBroker embedded ? embedded.TrySignPdfAsync(pdf, credential, pin, signingTime, cancellationToken) : Task.FromResult(CredentialSignResult.Failed("Selected provider cannot sign PDFs."));
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) { try { Pkcs11OperatorCredentialBroker.DisposeResource(session); } finally { gate.Release(); } } }
    }
}
