using System.Security.Cryptography;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.Credentials;

/// In-process chip/tap stand-in for CI and benches without a reader.
public sealed class MockOperatorCredentialBroker : IOperatorCredentialBroker
{
    public const string MockSerial = "MOCK-CARD";
    public const string MockDisplayName = "Mock Operator";
    public const string MockAlgorithm = AttestationAlgorithm.MockHmac;

    private static readonly byte[] MockHmacKey =
        "HardwareTest.MockOperatorCredential"u8.ToArray();

    private readonly IClock _clock;
    private readonly string _transport;

    public MockOperatorCredentialBroker(IClock? clock = null, bool canSign = true, string? transport = null)
    {
        _clock = clock ?? SystemClock.Instance;
        CanSign = canSign;
        _transport = string.IsNullOrWhiteSpace(transport)
            ? CredentialTransport.Contactless
            : transport;
    }

    public bool IsMock => true;
    public bool CanSign { get; set; }
    public string? SigningAlgorithm => CanSign ? MockAlgorithm : null;
    public string StatusText => CanSign
        ? "Mock badge ready (tap presence and signing)."
        : "Mock badge ready (tap presence only).";

    public Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = timeout;
        var credential = new OperatorCredential
        {
            DisplayName = MockDisplayName,
            Serial = MockSerial,
            Transport = _transport,
            ReaderName = "MOCK::CREDENTIAL",
            Thumbprint = "mock-thumbprint",
            CapturedAt = _clock.UtcNow,
        };
        return Task.FromResult(new CredentialCaptureResult { Credential = credential });
    }

    public Task<byte[]?> TrySignPayloadAsync(
        byte[] payload,
        OperatorCredential credential,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanSign || payload.Length == 0 || credential.Serial != MockSerial)
        {
            return Task.FromResult<byte[]?>(null);
        }

        return Task.FromResult<byte[]?>(HMACSHA256.HashData(MockHmacKey, payload));
    }

    /// Verifies a mock HMAC produced by this broker.
    public static bool VerifyMockSignature(byte[] payload, byte[] signature)
        => CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(MockHmacKey, payload), signature);
}
