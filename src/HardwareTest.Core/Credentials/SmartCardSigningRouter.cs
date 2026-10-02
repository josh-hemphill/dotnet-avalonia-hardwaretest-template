using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.Credentials;

/// Serializes card presence and retained signing sessions while selecting one provider from a settings snapshot.
public sealed class SmartCardSigningRouter : IOperatorCredentialBroker, IEmbeddedPdfSigningBroker
{
    private readonly AppSettings _settings;
    private readonly IOperatorCredentialBroker _presence;
    private readonly Func<AppSettings, IOperatorCredentialBroker> _windows;
    private readonly Func<AppSettings, IOperatorCredentialBroker> _pkcs11;
    private readonly Func<bool> _isWindows;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public SmartCardSigningRouter(AppSettings settings, INativeSigningDialogOwner? owner = null, IClock? clock = null, IOperatorCredentialBroker? presence = null)
    {
        _settings = settings; _presence = presence ?? new PcscOperatorCredentialBroker(clock);
        _windows = _ => new WindowsOperatorCredentialBroker(owner, clock, _presence);
        _pkcs11 = snapshot => new Pkcs11OperatorCredentialBroker(snapshot, clock, _presence);
        _isWindows = OperatingSystem.IsWindows;
    }
    internal SmartCardSigningRouter(AppSettings settings, IOperatorCredentialBroker presence,
        Func<AppSettings, IOperatorCredentialBroker> windows, Func<AppSettings, IOperatorCredentialBroker> pkcs11, Func<bool> isWindows)
    { _settings = settings; _presence = presence; _windows = windows; _pkcs11 = pkcs11; _isWindows = isWindows; }
    public bool IsMock => false;
    public bool CanSign => true;
    public bool ProducesCms => true;
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
            var snapshot = new AppSettings { SmartCardSigningProviderMode = _settings.SmartCardSigningProviderMode, Pkcs11LibraryPath = _settings.Pkcs11LibraryPath };
            if (!Enum.IsDefined(snapshot.SmartCardSigningProviderMode))
                return CredentialPreparationResult.Failed(CredentialSignResult.Failure(CredentialFailureKind.ConfigurationError, "Unknown smart-card signing provider mode.", "configuration"));
            CredentialPreparationResult result;
            if (snapshot.SmartCardSigningProviderMode == SmartCardSigningProviderMode.Pkcs11 ||
                snapshot.SmartCardSigningProviderMode == SmartCardSigningProviderMode.Auto && (!string.IsNullOrWhiteSpace(snapshot.Pkcs11LibraryPath) || !_isWindows()))
                result = await _pkcs11(snapshot).PrepareSigningAsync(credential, pin, cancellationToken).ConfigureAwait(false);
            else
            {
                result = await _windows(snapshot).PrepareSigningAsync(credential, cancellationToken: cancellationToken).ConfigureAwait(false);
                // Only an explicit pre-authentication capability result permits switching providers.
                if (snapshot.SmartCardSigningProviderMode == SmartCardSigningProviderMode.Auto && result.Session is null &&
                    result.Failure is { FailureKind: CredentialFailureKind.Unavailable, PresenceFallbackAllowed: true })
                    result = await _pkcs11(snapshot).PrepareSigningAsync(credential, pin, cancellationToken).ConfigureAwait(false);
            }
            if (result.Session is null) return result;
            var retained = new RoutedSession(result.Session, _gate);
            transferred = true;
            return CredentialPreparationResult.Ready(retained);
        }
        finally { if (!transferred) _gate.Release(); }
    }
    public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default) => SignAsync(payload, credential, pin, null, 0, cancellationToken);
    public Task<CredentialSignResult> TrySignDocumentAsync(byte[] document, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default) => SignAsync(document, credential, pin, signingTime, 1, cancellationToken);
    public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default) => SignAsync(pdf, credential, pin, signingTime, 2, cancellationToken);
    private async Task<CredentialSignResult> SignAsync(byte[] data, OperatorCredential credential, string? pin, DateTimeOffset? at, int kind, CancellationToken ct)
    {
        var prepared = await PrepareSigningAsync(credential, pin, ct).ConfigureAwait(false);
        using var session = prepared.Session;
        if (session is null) return prepared.Failure!;
        return kind switch
        {
            0 => await session.TrySignPayloadAsync(data, credential, pin, ct).ConfigureAwait(false),
            1 => await session.TrySignDocumentAsync(data, credential, pin, at, ct).ConfigureAwait(false),
            _ => await ((IEmbeddedPdfSigningBroker)session).TrySignPdfAsync(data, credential, pin, at, ct).ConfigureAwait(false),
        };
    }
    private sealed class RoutedSession(IPreparedCredentialSession session, SemaphoreSlim gate) : IPreparedCredentialSession, IEmbeddedPdfSigningBroker
    {
        private int _disposed;
        public bool IsMock => session.IsMock;
        public bool CanSign => session.CanSign;
        public bool ProducesCms => session.ProducesCms;
        public bool RequiresPin => session.RequiresPin;
        public string? SigningAlgorithm => session.SigningAlgorithm;
        public string StatusText => session.StatusText;
        public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default) => session.TrySignPayloadAsync(payload, credential, pin, cancellationToken);
        public Task<CredentialSignResult> TrySignDocumentAsync(byte[] document, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default) => session.TrySignDocumentAsync(document, credential, pin, signingTime, cancellationToken);
        public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
            => session is IEmbeddedPdfSigningBroker embedded ? embedded.TrySignPdfAsync(pdf, credential, pin, signingTime, cancellationToken) : Task.FromResult(CredentialSignResult.Failed("Selected provider cannot sign PDFs."));
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) { try { session.Dispose(); } finally { gate.Release(); } } }
    }
}
