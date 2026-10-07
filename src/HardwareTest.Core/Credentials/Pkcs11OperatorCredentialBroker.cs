using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
using Net.Pkcs11Interop.X509Store;

namespace HardwareTest.Core.Credentials;

/// Cross-platform PIV signer backed by an OpenSC/vendor PKCS#11 module.
/// PC/SC remains responsible only for presence and public identity reads.
public sealed class Pkcs11OperatorCredentialBroker : IOperatorCredentialBroker, IEmbeddedPdfSigningBroker
{
    private const string PivDigitalSignatureId = "02";
    private readonly AppSettings _settings;
    private readonly IOperatorCredentialPresenceBroker _presence;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _cardOperationGate = new(1, 1);

    public Pkcs11OperatorCredentialBroker(
        AppSettings settings,
        IClock? clock = null,
        IOperatorCredentialPresenceBroker? presence = null)
    {
        _settings = settings;
        _clock = clock ?? SystemClock.Instance;
        _presence = presence ?? new PcscOperatorCredentialBroker(_clock);
    }

    public bool IsMock => false;
    public bool CanSign => true;
    public bool CanSignPdf => true;
    public string? SigningAlgorithm => null;
    public string StatusText { get; private set; } = "PKCS#11 not queried yet.";

    public async Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        await _cardOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var captured = await _presence.WaitForPresenceAsync(timeout, cancellationToken).ConfigureAwait(false);
            StatusText = _presence.StatusText;
            return captured;
        }
        finally
        {
            _cardOperationGate.Release();
        }
    }

    public async Task<CredentialSignResult> TrySignPayloadAsync(
        byte[] payload,
        OperatorCredential credential,
        string? pin = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (payload.Length == 0)
        {
            return CredentialSignResult.Failed("Nothing to sign.");
        }

        var unavailable = CheckSigningPrerequisites(credential);
        if (unavailable is not null)
        {
            return unavailable;
        }

        if (string.IsNullOrEmpty(pin))
        {
            return CredentialSignResult.NeedPin("Enter badge PIN to sign.");
        }

        await _cardOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return WithSigningCertificate(pin, credential, (certificate, key, party) =>
            {
                var (signature, algorithm) = SignData(key, payload);
                return CredentialSignResult.Signed(
                    signature,
                    algorithm,
                    certificate.RawData,
                    certificate.Thumbprint,
                    party);
            });
        }
        finally
        {
            _cardOperationGate.Release();
        }
    }

    public async Task<CredentialSignResult> TrySignPdfAsync(
        byte[] pdf,
        OperatorCredential credential,
        string? pin = null,
        DateTimeOffset? signingTime = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pdf.Length == 0)
        {
            return CredentialSignResult.Failed("Nothing to sign.");
        }

        var unavailable = CheckSigningPrerequisites(credential);
        if (unavailable is not null)
        {
            return unavailable;
        }

        if (string.IsNullOrEmpty(pin))
        {
            return CredentialSignResult.NeedPin("Enter badge PIN to sign.");
        }

        await _cardOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return WithSigningCertificate(pin, credential, (certificate, key, party) =>
            {
                var at = signingTime ?? _clock.UtcNow;
                if (!ITextPadesSignature.TrySign(
                        pdf,
                        certificate,
                        key,
                        party.DisplayName,
                        at,
                        out var signedPdf,
                        out var cms,
                        out var error))
                {
                    return CredentialSignResult.Failed(error ?? "PAdES signing failed.");
                }

                return CredentialSignResult.SignedPdfDocument(
                    signedPdf,
                    cms,
                    AlgorithmName(key),
                    certificate.RawData,
                    certificate.Thumbprint,
                    party);
            });
        }
        finally
        {
            _cardOperationGate.Release();
        }
    }

    private CredentialSignResult WithSigningCertificate(
        string pin,
        OperatorCredential expected,
        Func<X509Certificate2, AsymmetricAlgorithm, OperatorCredential, CredentialSignResult> operation)
    {
        var module = Pkcs11ModuleResolver.Resolve(_settings.Pkcs11LibraryPath);
        if (module is null)
        {
            return CredentialSignResult.Unavailable(
                "OpenSC PKCS#11 module was not found. Install OpenSC or configure Pkcs11LibraryPath.");
        }

        try
        {
            using var pinProvider = new FixedPinProvider(pin);
            using var store = new Pkcs11X509Store(module, pinProvider);
            var candidates = store.Slots
                .Where(slot => slot.Token is { Info.Initialized: true })
                .SelectMany(slot => slot.Token!.Certificates)
                .Where(certificate => certificate.HasPrivateKeyObject)
                .Where(IsDigitalSignatureCertificate)
                .ToList();

            var selection = SelectCapturedCertificate(
                candidates.Select(certificate => certificate.Info.ParsedCertificate.Thumbprint).ToArray(),
                expected.Thumbprint);
            if (selection.Failure is not null)
            {
                return selection.Failure;
            }

            var selected = candidates[selection.Index!.Value];
            var parsed = selected.Info.ParsedCertificate;
            using var key = selected.GetPrivateKey();
            var party = new OperatorCredential
            {
                DisplayName = PivCertificateName.TryDisplayName(parsed.RawData) ?? expected.DisplayName,
                Serial = expected.Serial,
                Transport = expected.Transport,
                ReaderName = expected.ReaderName,
                Thumbprint = parsed.Thumbprint,
                CapturedAt = expected.CapturedAt,
            };
            var result = operation(parsed, key, party);
            StatusText = result.Succeeded
                ? $"Signed with {party.DisplayName}."
                : result.Error ?? "PKCS#11 signing failed.";
            return result;
        }
        catch (Exception ex)
        {
            var pinFailure = ClassifyPinFailure(ex);
            if (pinFailure is not null)
            {
                StatusText = pinFailure.Error!;
                return pinFailure;
            }

            StatusText = "PKCS#11 signing failed. " + ex.Message;
            return CredentialSignResult.Failed(StatusText);
        }
    }

    private CredentialSignResult? CheckSigningPrerequisites(OperatorCredential expected)
    {
        if (string.IsNullOrWhiteSpace(expected.Thumbprint))
        {
            return CredentialSignResult.Unavailable(
                "No PIV signing certificate (9C) was captured from this badge; signing cannot be bound safely.");
        }

        return Pkcs11ModuleResolver.Resolve(_settings.Pkcs11LibraryPath) is null
            ? CredentialSignResult.Unavailable(
                "OpenSC PKCS#11 module was not found. Install OpenSC or configure Pkcs11LibraryPath.")
            : null;
    }

    internal static (int? Index, CredentialSignResult? Failure) SelectCapturedCertificate(
        IReadOnlyList<string> candidateThumbprints,
        string? capturedThumbprint)
    {
        if (string.IsNullOrWhiteSpace(capturedThumbprint))
        {
            return (null, CredentialSignResult.Unavailable(
                "No PIV signing certificate (9C) was captured from this badge; signing cannot be bound safely."));
        }

        if (candidateThumbprints.Count == 0)
        {
            return (null, CredentialSignResult.Unavailable(
                "No PIV signing certificate (9C) with a private key was found."));
        }

        var matching = Enumerable.Range(0, candidateThumbprints.Count)
            .Where(index => string.Equals(candidateThumbprints[index], capturedThumbprint,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matching.Length switch
        {
            1 => (matching[0], null),
            0 => (null, CredentialSignResult.Failed(
                "The available signing badge does not match the badge that was captured.")),
            _ => (null, CredentialSignResult.Failed(
                "More than one signing badge is present. Leave only the certifier badge inserted.")),
        };
    }

    private static bool IsDigitalSignatureCertificate(Pkcs11X509Certificate certificate)
    {
        if (IsPivDigitalSignatureId(certificate.Info.Id)
            || IsSignatureLabel(certificate.Info.Label))
        {
            return true;
        }

        return false;
    }

    private static bool IsSignatureLabel(string label)
        => label.Contains("SIGNATURE", StringComparison.OrdinalIgnoreCase)
           || label.Contains("9C", StringComparison.OrdinalIgnoreCase);

    private static bool IsPivDigitalSignatureId(string id)
        => string.Equals(
            id.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace(":", string.Empty, StringComparison.Ordinal),
            PivDigitalSignatureId,
            StringComparison.OrdinalIgnoreCase);

    private static (byte[] Signature, string Algorithm) SignData(AsymmetricAlgorithm key, byte[] payload)
        => key switch
        {
            RSA rsa => (rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                AttestationAlgorithm.PivRsaPkcs1Sha256),
            ECDsa ecdsa when ecdsa.KeySize >= 384 =>
                (ecdsa.SignData(payload, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence),
                    AttestationAlgorithm.PivEcdsaSha384),
            ECDsa ecdsa =>
                (ecdsa.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
                    AttestationAlgorithm.PivEcdsaSha256),
            _ => throw new CryptographicException("The PIV signing key is not RSA or ECDSA."),
        };

    private static string AlgorithmName(AsymmetricAlgorithm key)
        => key switch
        {
            RSA => AttestationAlgorithm.PivRsaPkcs1Sha256,
            ECDsa { KeySize: >= 384 } => AttestationAlgorithm.PivEcdsaSha384,
            ECDsa => AttestationAlgorithm.PivEcdsaSha256,
            _ => throw new CryptographicException("The PIV signing key is not RSA or ECDSA."),
        };

    internal static CredentialSignResult? ClassifyPinFailure(Exception exception)
    {
        var retryable = false;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var text = current.Message;
            if (text.Contains("PIN_LOCKED", StringComparison.OrdinalIgnoreCase))
            {
                return CredentialSignResult.Failed("Badge PIN is locked.", pinRetriesRemaining: 0);
            }

            retryable |= text.Contains("PIN_INCORRECT", StringComparison.OrdinalIgnoreCase)
                         || text.Contains("USER_NOT_LOGGED_IN", StringComparison.OrdinalIgnoreCase);
        }

        return retryable ? CredentialSignResult.NeedPin("Badge PIN was not accepted.") : null;
    }

    private sealed class FixedPinProvider(string pin) : IPinProvider, IDisposable
    {
        private readonly byte[] _pin = Encoding.UTF8.GetBytes(pin);

        public GetPinResult GetTokenPin(
            Pkcs11X509StoreInfo storeInfo,
            Pkcs11SlotInfo slotInfo,
            Pkcs11TokenInfo tokenInfo)
        {
            _ = storeInfo;
            _ = slotInfo;
            return tokenInfo.HasProtectedAuthenticationPath
                ? new GetPinResult(cancel: false, pin: null)
                : new GetPinResult(cancel: false, pin: _pin);
        }

        public GetPinResult GetKeyPin(
            Pkcs11X509StoreInfo storeInfo,
            Pkcs11SlotInfo slotInfo,
            Pkcs11TokenInfo tokenInfo,
            Pkcs11X509CertificateInfo certificateInfo)
        {
            _ = storeInfo;
            _ = slotInfo;
            _ = certificateInfo;
            return tokenInfo.HasProtectedAuthenticationPath
                ? new GetPinResult(cancel: false, pin: null)
                : new GetPinResult(cancel: false, pin: _pin);
        }

        public void Dispose() => CryptographicOperations.ZeroMemory(_pin);
    }
}

internal static class Pkcs11ModuleResolver
{
    private static readonly string[] WindowsCandidates =
    [
        "opensc-pkcs11.dll",
        @"C:\Program Files\OpenSC Project\OpenSC\pkcs11\opensc-pkcs11.dll",
        @"C:\Program Files\OpenSC Project\OpenSC\pkcs11\opensc-pkcs11-x64.dll",
    ];

    private static readonly string[] LinuxCandidates =
    [
        "/usr/lib/x86_64-linux-gnu/opensc-pkcs11.so",
        "/usr/lib/aarch64-linux-gnu/opensc-pkcs11.so",
        "/usr/lib64/opensc-pkcs11.so",
        "/usr/lib/opensc-pkcs11.so",
        "opensc-pkcs11.so",
    ];

    private static readonly string[] MacCandidates =
    [
        "/Library/OpenSC/lib/opensc-pkcs11.so",
        "/opt/homebrew/lib/opensc-pkcs11.so",
        "/usr/local/lib/opensc-pkcs11.so",
        "opensc-pkcs11.so",
    ];

    public static string? Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var candidates = OperatingSystem.IsWindows()
            ? WindowsCandidates
            : OperatingSystem.IsMacOS() ? MacCandidates : LinuxCandidates;
        return ResolveCandidates(candidates, File.Exists, CanLoad);
    }

    internal static string? ResolveCandidates(
        IEnumerable<string> candidates,
        Func<string, bool> fileExists,
        Func<string, bool> canLoad)
        => candidates.FirstOrDefault(candidate => Path.IsPathRooted(candidate) && fileExists(candidate))
           ?? candidates.FirstOrDefault(candidate => !Path.IsPathRooted(candidate) && canLoad(candidate));

    private static bool CanLoad(string library)
    {
        if (!NativeLibrary.TryLoad(library, out var handle))
        {
            return false;
        }

        NativeLibrary.Free(handle);
        return true;
    }
}
