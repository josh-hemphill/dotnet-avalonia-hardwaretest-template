using HardwareTest.Core.Settings;

namespace HardwareTest.Core.Credentials;

/// Routes chip/tap capture to the mock or PKCS11 broker from live settings.
public sealed class SettingsBackedCredentialBroker : IOperatorCredentialBroker, IEmbeddedPdfSigningBroker
{
    private readonly AppSettings _settings;
    private readonly IOperatorCredentialBroker _mock;
    private readonly IOperatorCredentialBroker _physical;

    public SettingsBackedCredentialBroker(
        AppSettings settings,
        IOperatorCredentialBroker mock,
        IOperatorCredentialBroker physical)
    {
        _settings = settings;
        _mock = mock;
        _physical = physical;
    }

    public bool IsMock => Active.IsMock;
    public bool CanSign => Active.CanSign;
    public bool CanSignPdf => !Active.IsMock && Active.CanSignPdf;
    public string? SigningAlgorithm => Active.SigningAlgorithm;
    public string StatusText => Active.StatusText;

    public Task<CredentialPreparationResult> PrepareSigningAsync(
        OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
    {
        var selected = Active;
        return selected.PrepareSigningAsync(credential, pin, cancellationToken);
    }

    public Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => Active.WaitForPresenceAsync(timeout, cancellationToken);

    public Task<CredentialSignResult> TrySignPayloadAsync(
        byte[] payload,
        OperatorCredential credential,
        string? pin = null,
        CancellationToken cancellationToken = default)
        => Active.TrySignPayloadAsync(payload, credential, pin, cancellationToken);

    public Task<CredentialSignResult> TrySignPdfAsync(
        byte[] pdf,
        OperatorCredential credential,
        string? pin = null,
        DateTimeOffset? signingTime = null,
        CancellationToken cancellationToken = default)
        => CanSignPdf && Active is IEmbeddedPdfSigningBroker embedded
            ? embedded.TrySignPdfAsync(pdf, credential, pin, signingTime, cancellationToken)
            : Task.FromResult(CredentialSignResult.Failed("The active credential does not support embedded PDF signing."));

    internal IOperatorCredentialBroker Snapshot() => Active;

    private IOperatorCredentialBroker Active
        => _settings.UseMockOperatorCredential ? _mock : _physical;
}
