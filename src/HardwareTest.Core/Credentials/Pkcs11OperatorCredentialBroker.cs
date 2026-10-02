using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace HardwareTest.Core.Credentials;

/// PC/SC captures identity; PKCS#11 prepares only the matching signing token.
public sealed class Pkcs11OperatorCredentialBroker : IOperatorCredentialBroker, IEmbeddedPdfSigningBroker
{
    private readonly AppSettings _settings;
    private readonly IOperatorCredentialBroker _presence;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<string, IPkcs11Backend> _open;

    public Pkcs11OperatorCredentialBroker(AppSettings settings, IClock? clock = null, IOperatorCredentialBroker? presence = null)
        : this(settings, module => new Pkcs11Backend(module), clock, presence) { }

    internal Pkcs11OperatorCredentialBroker(AppSettings settings, Func<string, IPkcs11Backend> open,
        IClock? clock = null, IOperatorCredentialBroker? presence = null)
    {
        _settings = settings; _open = open; _clock = clock ?? SystemClock.Instance;
        _presence = presence ?? new PcscOperatorCredentialBroker(_clock);
    }
    public bool IsMock => false;
    public bool CanSign => true;
    public bool ProducesCms => true;
    public string? SigningAlgorithm => null;
    public string StatusText { get; private set; } = "Signing provider not queried yet.";

    public async Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await _presence.WaitForPresenceAsync(timeout, cancellationToken).ConfigureAwait(false); }
        finally { StatusText = _presence.StatusText; _gate.Release(); }
    }

    public async Task<CredentialPreparationResult> PrepareSigningAsync(OperatorCredential credential,
        string? pin = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IPkcs11Backend? backend = null;
        var tokens = new List<IPkcs11Token>();
        var transferred = false;
        var stage = "configuration";
        try
        {
            if (string.IsNullOrWhiteSpace(credential.Thumbprint))
                return Fail(CredentialFailureKind.Unavailable, "No PIV signing certificate (9C) was captured from this badge; signing cannot be bound safely.", stage);
            if (_settings.SmartCardSigningProviderMode == SmartCardSigningProviderMode.Windows)
                return Fail(CredentialFailureKind.Unavailable, "Windows smart-card signing is not supported by this provider yet.", stage);
            if (!Enum.IsDefined(_settings.SmartCardSigningProviderMode))
                return Fail(CredentialFailureKind.ConfigurationError, "Unknown smart-card signing provider mode.", stage);
            var module = Pkcs11ModuleResolver.Resolve(_settings.Pkcs11LibraryPath);
            if (module is null) return Fail(CredentialFailureKind.Unavailable, "No compatible PKCS#11 middleware was found. Configure its library path.", stage);
            if (!Pkcs11ModuleResolver.IsCompatibleArchitecture(module))
                return Fail(CredentialFailureKind.ConfigurationError, "PKCS#11 module architecture does not match the application process. Select matching middleware.", stage);
            stage = "module-load";
            backend = _open(module);
            stage = "public-certificate-discovery";
            tokens.AddRange(backend.OpenTokens());
            var matches = new List<(IPkcs11Token Token, Pkcs11Certificate Certificate)>();
            foreach (var token in tokens)
            {
                foreach (var candidate in token.ReadPublicCertificates())
                {
                    using var certificate = X509CertificateLoader.LoadCertificate(candidate.Der);
                    if (string.Equals(certificate.Thumbprint, credential.Thumbprint, StringComparison.OrdinalIgnoreCase))
                        matches.Add((token, candidate));
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (matches.Count != 1)
                return Fail(CredentialFailureKind.CertificateMismatch,
                    matches.Count == 0 ? "The available signing badge does not match the captured badge." : "More than one matching signing certificate is present. Leave only the certifier badge inserted.", stage);
            var selected = matches[0];
            if (selected.Certificate.Id.Length == 0)
                return Fail(CredentialFailureKind.ConfigurationError, "Selected signing certificate has no key binding identifier.", "private-key-binding");
            foreach (var other in tokens.Where(t => t != selected.Token)) DisposeResource(other);
            tokens.RemoveAll(t => t != selected.Token);
            using (var certificate = X509CertificateLoader.LoadCertificate(selected.Certificate.Der))
            {
                using var rsa = certificate.GetRSAPublicKey();
                using var ec = certificate.GetECDsaPublicKey();
                if ((rsa is null && ec is null) || certificate.GetKeyAlgorithm() is not ("1.2.840.113549.1.1.1" or "1.2.840.10045.2.1"))
                    return Fail(CredentialFailureKind.ConfigurationError, "Signing certificate must use RSA or ECDSA.", "certificate-algorithm");
            }
            stage = "authentication";
            if (!selected.Token.ProtectedAuthenticationPath && string.IsNullOrEmpty(pin))
                return Fail(CredentialFailureKind.PinRequired, "Enter badge PIN to sign.", stage);
            byte[]? pinBytes = selected.Token.ProtectedAuthenticationPath ? null : Encoding.UTF8.GetBytes(pin!);
            try { selected.Token.Login(pinBytes); }
            finally { if (pinBytes is not null) CryptographicOperations.ZeroMemory(pinBytes); }
            cancellationToken.ThrowIfCancellationRequested();
            stage = "private-key-binding";
            selected.Token.BindPrivateKey(selected.Certificate.Id);
            cancellationToken.ThrowIfCancellationRequested();
            var session = new PreparedSession(backend, selected.Token, selected.Certificate.Der, credential, _gate, _clock);
            transferred = true;
            return CredentialPreparationResult.Ready(session);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CredentialPreparationResult.Failed(MapFailure(ex, stage)); }
        finally
        {
            if (!transferred)
            {
                try
                {
                    foreach (var token in tokens) DisposeResource(token);
                    DisposeResource(backend);
                }
                finally { _gate.Release(); }
            }
        }
    }

    private static CredentialPreparationResult Fail(CredentialFailureKind kind, string message, string stage)
        => CredentialPreparationResult.Failed(CredentialSignResult.Failure(kind, message, stage));

    internal static void DisposeResource(IDisposable? resource)
    {
        try { resource?.Dispose(); }
        catch (Exception)
        {
            // Middleware teardown must not replace the signing result or prevent other cleanup.
            System.Diagnostics.Trace.TraceWarning("Smart-card middleware resource cleanup failed.");
        }
    }

    internal static CredentialSignResult MapFailure(Exception exception, string stage)
    {
        Pkcs11Exception? native = null;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is Pkcs11Exception found) { native = found; break; }
        }
        var kind = native?.RV switch
        {
            CKR.CKR_PIN_INCORRECT or CKR.CKR_PIN_INVALID or CKR.CKR_PIN_LEN_RANGE => CredentialFailureKind.WrongPin,
            CKR.CKR_PIN_LOCKED => CredentialFailureKind.PinLocked,
            CKR.CKR_FUNCTION_CANCELED or CKR.CKR_CANCEL => CredentialFailureKind.Cancelled,
            CKR.CKR_DEVICE_REMOVED or CKR.CKR_TOKEN_NOT_PRESENT => CredentialFailureKind.CardRemoved,
            CKR.CKR_USER_NOT_LOGGED_IN => CredentialFailureKind.SigningFailed,
            _ => stage == "module-load" || exception is DllNotFoundException or BadImageFormatException or FileNotFoundException or ArgumentException
                ? CredentialFailureKind.ConfigurationError : CredentialFailureKind.SigningFailed,
        };
        var message = kind switch
        {
            CredentialFailureKind.WrongPin => "Badge PIN was not accepted. No automatic retry was attempted.",
            CredentialFailureKind.PinLocked => "Badge PIN is locked. Contact your smart-card administrator.",
            CredentialFailureKind.Cancelled => "Smart-card authentication was cancelled.",
            CredentialFailureKind.CardRemoved => "The selected signing card was removed.",
            CredentialFailureKind.ConfigurationError => "PKCS#11 middleware could not be loaded. Check the configured path and process architecture.",
            _ => "Smart-card operation failed.",
        };
        return CredentialSignResult.Failure(kind, message, stage, native is null ? null : (ulong)native.RV);
    }

    public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
        => SignAsync(payload, credential, pin, null, 0, cancellationToken);
    public Task<CredentialSignResult> TrySignDocumentAsync(byte[] document, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
        => SignAsync(document, credential, pin, signingTime, 1, cancellationToken);
    public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
        => SignAsync(pdf, credential, pin, signingTime, 2, cancellationToken);
    private async Task<CredentialSignResult> SignAsync(byte[] data, OperatorCredential credential, string? pin, DateTimeOffset? at, int kind, CancellationToken ct)
    {
        if (data.Length == 0) return CredentialSignResult.Failed("Nothing to sign.");
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

    private sealed class PreparedSession : IPreparedCredentialSession, IEmbeddedPdfSigningBroker
    {
        private readonly IPkcs11Backend _backend;
        private readonly IPkcs11Token _token;
        private readonly X509Certificate2 _certificate;
        private readonly OperatorCredential _credential;
        private readonly SemaphoreSlim _gate;
        private readonly IClock _clock;
        private readonly object _operation = new();
        private bool _disposed;
        public PreparedSession(IPkcs11Backend backend, IPkcs11Token token, byte[] der, OperatorCredential credential, SemaphoreSlim gate, IClock clock)
        { _backend = backend; _token = token; _certificate = X509CertificateLoader.LoadCertificate(der); _credential = credential; _gate = gate; _clock = clock; }
        public bool IsMock => false;
        public bool CanSign => true;
        public bool ProducesCms => true;
        private bool Rsa => _certificate.GetKeyAlgorithm() == "1.2.840.113549.1.1.1";
        private bool Sha384 { get { using var ec = _certificate.GetECDsaPublicKey(); return ec?.KeySize >= 384; } }
        public string? SigningAlgorithm => Rsa ? AttestationAlgorithm.PivRsaPkcs1Sha256 : Sha384 ? AttestationAlgorithm.PivEcdsaSha384 : AttestationAlgorithm.PivEcdsaSha256;
        public string StatusText => "Selected signing session prepared.";
        public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private byte[] Sign(byte[] message, string? pin, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            byte[]? keyPin = _token.ProtectedAuthenticationPath || !_token.AlwaysAuthenticate || pin is null ? null : Encoding.UTF8.GetBytes(pin);
            try
            {
                var hash = Sha384 && !Rsa ? SHA384.HashData(message) : SHA256.HashData(message);
                byte[] data = Rsa ? Convert.FromHexString("3031300D060960864801650304020105000420").Concat(hash).ToArray() : hash;
                var raw = _token.Sign(Rsa ? CKM.CKM_RSA_PKCS : CKM.CKM_ECDSA, data, keyPin);
                ct.ThrowIfCancellationRequested();
                ValidateSignatureLength(raw);
                if (Rsa) return raw;
                var writer = new AsnWriter(AsnEncodingRules.DER); writer.PushSequence();
                writer.WriteIntegerUnsigned(raw.AsSpan(0, raw.Length / 2)); writer.WriteIntegerUnsigned(raw.AsSpan(raw.Length / 2)); writer.PopSequence();
                return writer.Encode();
            }
            finally { if (keyPin is not null) CryptographicOperations.ZeroMemory(keyPin); }
        }
        private void ValidateSignatureLength(byte[] signature)
        {
            using AsymmetricAlgorithm key = Rsa ? _certificate.GetRSAPublicKey()! : _certificate.GetECDsaPublicKey()!;
            var expected = ((key.KeySize + 7) / 8) * (Rsa ? 1 : 2);
            if (signature.Length != expected) throw new CryptographicException("Invalid native signature length.");
        }
        private CredentialSignResult Execute(OperatorCredential expected, CancellationToken ct, Func<CredentialSignResult> operation)
        {
            lock (_operation)
            {
                ObjectDisposedException.ThrowIf(_disposed, this); ct.ThrowIfCancellationRequested();
                if (!string.Equals(expected.Thumbprint, _certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
                    return CredentialSignResult.Failure(CredentialFailureKind.CertificateMismatch, "Prepared signer does not match the captured certificate.", "signing");
                try { var result = operation(); ct.ThrowIfCancellationRequested(); return result; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return MapFailure(ex, "signing"); }
            }
        }
        public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Execute(credential, cancellationToken, () => CredentialSignResult.Signed(Sign(payload, pin, cancellationToken), SigningAlgorithm!, _certificate.RawData, _certificate.Thumbprint, _credential)));
        public Task<CredentialSignResult> TrySignDocumentAsync(byte[] document, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Execute(credential, cancellationToken, () =>
            {
                using AsymmetricAlgorithm key = Rsa ? new CallbackRsa(_certificate, hash => SignHash(hash, pin, cancellationToken)) : new CallbackEcdsa(_certificate, hash => SignHash(hash, pin, cancellationToken));
                var cms = new SignedCms(new ContentInfo(document), true);
                var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, _certificate, key)
                { DigestAlgorithm = new Oid(!Rsa && Sha384 ? "2.16.840.1.101.3.4.2.2" : "2.16.840.1.101.3.4.2.1"), IncludeOption = X509IncludeOption.EndCertOnly };
                cms.ComputeSignature(signer, true);
                return CredentialSignResult.Signed(cms.Encode(), SigningAlgorithm!, _certificate.RawData, _certificate.Thumbprint, _credential);
            }));
        private byte[] SignHash(byte[] hash, string? pin, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            byte[]? keyPin = _token.AlwaysAuthenticate && !_token.ProtectedAuthenticationPath && pin is not null ? Encoding.UTF8.GetBytes(pin) : null;
            try
            {
                var raw = _token.Sign(Rsa ? CKM.CKM_RSA_PKCS : CKM.CKM_ECDSA, Rsa ? Convert.FromHexString("3031300D060960864801650304020105000420").Concat(hash).ToArray() : hash, keyPin);
                ct.ThrowIfCancellationRequested(); ValidateSignatureLength(raw); return raw;
            }
            finally { if (keyPin is not null) CryptographicOperations.ZeroMemory(keyPin); }
        }
        public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Execute(credential, cancellationToken, () =>
            {
                Exception? callbackFailure = null;
                var external = new ExternalSignature(Rsa ? "RSA" : "ECDSA", !Rsa && Sha384 ? "SHA-384" : "SHA-256", bytes =>
                {
                    try { return Sign(bytes, pin, cancellationToken); }
                    catch (Exception ex) { callbackFailure = ex; throw; }
                });
                if (!ITextPadesSignature.TrySign(pdf, _certificate, external, credential.DisplayName, signingTime ?? _clock.UtcNow, out var signed, out var cms, out var error))
                {
                    if (callbackFailure is OperationCanceledException) cancellationToken.ThrowIfCancellationRequested();
                    return callbackFailure is null ? CredentialSignResult.Failed(error ?? "PDF signing failed.") : MapFailure(callbackFailure, "signing");
                }
                return CredentialSignResult.SignedPdfDocument(signed, cms, SigningAlgorithm!, _certificate.RawData, _certificate.Thumbprint, _credential);
            }));
        public void Dispose()
        {
            lock (_operation)
            {
                if (_disposed) return; _disposed = true;
                try
                {
                    DisposeResource(_token);
                    DisposeResource(_backend);
                    DisposeResource(_certificate);
                }
                finally { _gate.Release(); }
            }
        }
    }
    private sealed class ExternalSignature(string algorithm, string digest, Func<byte[], byte[]> sign) : iText.Signatures.IExternalSignature
    {
        public string GetDigestAlgorithmName() => digest;
        public string GetSignatureAlgorithmName() => algorithm;
        public iText.Signatures.ISignatureMechanismParams? GetSignatureMechanismParameters() => null;
        public byte[] Sign(byte[] message) => sign(message);
    }
    private sealed class CallbackRsa(X509Certificate2 certificate, Func<byte[], byte[]> sign) : RSA
    {
        public override int KeySize { get { using var key = certificate.GetRSAPublicKey()!; return key.KeySize; } set => throw new NotSupportedException(); }
        public override RSAParameters ExportParameters(bool includePrivateParameters) { if (includePrivateParameters) throw new NotSupportedException(); using var key = certificate.GetRSAPublicKey()!; return key.ExportParameters(false); }
        public override void ImportParameters(RSAParameters parameters) => throw new NotSupportedException();
        public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        { if (hashAlgorithm != HashAlgorithmName.SHA256 || padding != RSASignaturePadding.Pkcs1) throw new NotSupportedException(); return sign(hash); }
        public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) { using var key = certificate.GetRSAPublicKey()!; return key.VerifyHash(hash, signature, hashAlgorithm, padding); }
        public override byte[] Encrypt(byte[] data, RSAEncryptionPadding padding) => throw new NotSupportedException();
        public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding) => throw new NotSupportedException();
    }
    private sealed class CallbackEcdsa(X509Certificate2 certificate, Func<byte[], byte[]> sign) : ECDsa
    {
        public override int KeySize { get { using var key = certificate.GetECDsaPublicKey()!; return key.KeySize; } set => throw new NotSupportedException(); }
        public override byte[] SignHash(byte[] hash) => sign(hash);
        public override bool VerifyHash(byte[] hash, byte[] signature) { using var key = certificate.GetECDsaPublicKey()!; return key.VerifyHash(hash, signature); }
        public override ECParameters ExportParameters(bool includePrivateParameters) { if (includePrivateParameters) throw new NotSupportedException(); using var key = certificate.GetECDsaPublicKey()!; return key.ExportParameters(false); }
        public override void ImportParameters(ECParameters parameters) => throw new NotSupportedException();
    }
}

internal sealed record Pkcs11Certificate(byte[] Der, byte[] Id);
internal interface IPkcs11Backend : IDisposable { IReadOnlyList<IPkcs11Token> OpenTokens(); }
internal interface IPkcs11Token : IDisposable
{
    bool ProtectedAuthenticationPath { get; }
    bool AlwaysAuthenticate { get; }
    IReadOnlyList<Pkcs11Certificate> ReadPublicCertificates();
    void Login(byte[]? pin);
    void BindPrivateKey(byte[] certificateId);
    byte[] Sign(CKM mechanism, byte[] data, byte[]? contextPin);
}
internal sealed class Pkcs11Backend : IPkcs11Backend
{
    private readonly Pkcs11InteropFactories _factories = new();
    private readonly IPkcs11Library _library;
    public Pkcs11Backend(string module) => _library = _factories.Pkcs11LibraryFactory.LoadPkcs11Library(_factories, module, AppType.MultiThreaded);
    public IReadOnlyList<IPkcs11Token> OpenTokens()
    {
        var tokens = new List<IPkcs11Token>();
        try
        {
            foreach (var slot in _library.GetSlotList(SlotsType.WithTokenPresent))
            {
                var protectedPath = slot.GetTokenInfo().TokenFlags.ProtectedAuthenticationPath;
                tokens.Add(new Pkcs11Token(slot.OpenSession(SessionType.ReadOnly), protectedPath, _factories));
            }
            return tokens;
        }
        catch { foreach (var token in tokens) Pkcs11OperatorCredentialBroker.DisposeResource(token); throw; }
    }
    public void Dispose() => _library.Dispose();
}
internal sealed class Pkcs11Token(ISession session, bool protectedPath, Pkcs11InteropFactories factories) : IPkcs11Token
{
    private bool _ownsLogin;
    private IObjectHandle? _key;
    public bool ProtectedAuthenticationPath => protectedPath;
    public bool AlwaysAuthenticate { get; private set; }
    public IReadOnlyList<Pkcs11Certificate> ReadPublicCertificates()
    {
        var objects = session.FindAllObjects([factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE)]);
        return objects.Select(handle =>
        {
            var attributes = session.GetAttributeValue(handle, new List<CKA> { CKA.CKA_VALUE, CKA.CKA_ID });
            return new Pkcs11Certificate(attributes[0].GetValueAsByteArray(), attributes[1].GetValueAsByteArray());
        }).ToList();
    }
    public void Login(byte[]? pin)
    {
        try { session.Login(CKU.CKU_USER, pin!); _ownsLogin = true; }
        catch (Pkcs11Exception ex) when (ex.RV == CKR.CKR_USER_ALREADY_LOGGED_IN) { }
    }
    public void BindPrivateKey(byte[] certificateId)
    {
        var keys = session.FindAllObjects([factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY), factories.ObjectAttributeFactory.Create(CKA.CKA_ID, certificateId)]);
        if (keys.Count != 1) throw new CryptographicException("Selected certificate must bind exactly one private key.");
        _key = keys[0];
        AlwaysAuthenticate = session.GetAttributeValue(_key, new List<CKA> { CKA.CKA_ALWAYS_AUTHENTICATE })[0].GetValueAsBool();
    }
    public byte[] Sign(CKM mechanism, byte[] data, byte[]? contextPin)
    {
        using var mech = factories.MechanismFactory.Create(mechanism);
        return AlwaysAuthenticate ? session.Sign(mech, _key!, contextPin!, data) : session.Sign(mech, _key!, data);
    }
    public void Dispose()
    {
        try { if (_ownsLogin) { _ownsLogin = false; try { session.Logout(); } catch (Pkcs11Exception) { } } }
        finally { session.Dispose(); }
    }
}
