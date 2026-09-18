using HardwareTest.Core.Settings;

namespace HardwareTest.Core.Credentials;

/// Routes chip/tap capture to the mock or PC/SC broker from live settings.
public sealed class SettingsBackedCredentialBroker : IOperatorCredentialBroker
{
    private readonly AppSettings _settings;
    private readonly IOperatorCredentialBroker _mock;
    private readonly IOperatorCredentialBroker _pcsc;

    public SettingsBackedCredentialBroker(
        AppSettings settings,
        IOperatorCredentialBroker mock,
        IOperatorCredentialBroker pcsc)
    {
        _settings = settings;
        _mock = mock;
        _pcsc = pcsc;
    }

    public bool IsMock => Active.IsMock;
    public bool CanSign => Active.CanSign;
    public bool ProducesCms => Active.ProducesCms;
    public string? SigningAlgorithm => Active.SigningAlgorithm;
    public string StatusText => Active.StatusText;

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

    public Task<CredentialSignResult> TrySignDocumentAsync(
        byte[] document,
        OperatorCredential credential,
        string? pin = null,
        DateTimeOffset? signingTime = null,
        CancellationToken cancellationToken = default)
        => Active.TrySignDocumentAsync(document, credential, pin, signingTime, cancellationToken);

    private IOperatorCredentialBroker Active
        => _settings.UseMockOperatorCredential ? _mock : _pcsc;
}
