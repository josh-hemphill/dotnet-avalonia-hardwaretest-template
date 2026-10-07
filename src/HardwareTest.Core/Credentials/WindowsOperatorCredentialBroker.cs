using System.Formats.Asn1;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.Credentials;

/// Acquires only the exact captured signing certificate; PIN entry belongs to Windows middleware.
public sealed class WindowsOperatorCredentialBroker : IOperatorCredentialBroker, IEmbeddedPdfSigningBroker
{
    internal const uint AcquisitionFlags = 0x20000 | 4 | 0x80;
    private readonly IWindowsSigningNative _native;
    private readonly IWindowsSigningCertificateSource _certificates;
    private readonly INativeSigningDialogOwner _owner;
    private readonly IOperatorCredentialPresenceBroker _presence;
    private readonly IClock _clock;
    private readonly Func<bool> _isWindows;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WindowsOperatorCredentialBroker(INativeSigningDialogOwner? owner = null, IClock? clock = null, IOperatorCredentialPresenceBroker? presence = null)
        : this(new WindowsSigningNative(), new WindowsSigningCertificateSource(), owner ?? new HeadlessSigningDialogOwner(), clock, presence, OperatingSystem.IsWindows) { }
    internal WindowsOperatorCredentialBroker(IWindowsSigningNative native, IWindowsSigningCertificateSource certificates, INativeSigningDialogOwner owner,
        IClock? clock = null, IOperatorCredentialPresenceBroker? presence = null, Func<bool>? isWindows = null)
    { _native = native; _certificates = certificates; _owner = owner; _clock = clock ?? SystemClock.Instance; _presence = presence ?? new PcscOperatorCredentialBroker(_clock); _isWindows = isWindows ?? OperatingSystem.IsWindows; }
    public bool IsMock => false;
    public bool CanSign => true;
    public bool CanSignPdf => true;
    public string? SigningAlgorithm => null;
    public string StatusText => "Windows smart-card signing uses middleware PIN dialogs.";
    public async Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await _presence.WaitForPresenceAsync(timeout, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    public async Task<CredentialPreparationResult> PrepareSigningAsync(OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        WindowsSigningWorker? worker = null;
        var transferred = false;
        try
        {
            if (!_isWindows()) return Unavailable("Windows smart-card signing requires Windows.");
            if (string.IsNullOrWhiteSpace(credential.Thumbprint)) return Unavailable("No PIV signing certificate (9C) was captured.");
            var owner = await _owner.GetWindowHandleAsync(cancellationToken).ConfigureAwait(false);
            if (owner == 0) return Unavailable("Windows smart-card signing needs an application dialog owner.");
            worker = new WindowsSigningWorker(_native);
            var retainedWorker = worker;
            var result = await worker.InvokeAsync(() => PrepareOnWorker(credential, owner, retainedWorker, cancellationToken)).ConfigureAwait(false);
            transferred = result.Session is not null;
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CredentialPreparationResult.Failed(MapFailure(ex, "preparation")); }
        finally { if (!transferred) { try { worker?.Dispose(); } finally { _gate.Release(); } } }
    }
    private CredentialPreparationResult PrepareOnWorker(OperatorCredential credential, nint owner, WindowsSigningWorker worker, CancellationToken ct)
    {
        IReadOnlyList<X509Certificate2> certificates = [];
        X509Certificate2? selected = null;
        WindowsSigningKey? key = null;
        var transferred = false;
        var stage = "certificate-discovery";
        try
        {
            ct.ThrowIfCancellationRequested();
            certificates = _certificates.ReadCertificates();
            var matches = certificates.Where(c => string.Equals(c.Thumbprint, credential.Thumbprint, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0) return Unavailable("The captured signing certificate is not available in the Windows personal store.");
            if (matches.Length != 1) return CredentialPreparationResult.Failed(CredentialSignResult.Failure(CredentialFailureKind.CertificateMismatch, "The Windows signing certificate is ambiguous.", stage));
            selected = matches[0];
            using var rsa = selected.GetRSAPublicKey(); using var ec = selected.GetECDsaPublicKey();
            if (rsa is null && (ec is null || ec.KeySize is not (256 or 384)))
                return Unavailable("The captured certificate signing algorithm is not supported.");
            ct.ThrowIfCancellationRequested();
            stage = "authentication";
            var ownerAddress = Marshal.AllocHGlobal(nint.Size);
            try { Marshal.WriteIntPtr(ownerAddress, owner); key = _native.Acquire(selected.Handle, AcquisitionFlags, ownerAddress); }
            finally { Marshal.FreeHGlobal(ownerAddress); }
            ct.ThrowIfCancellationRequested(); // Native dialogs cannot be aborted safely by abandoning their task.
            stage = "provider-properties";
            if (!_native.IsHardware(key.Value)) throw new WindowsSigningNativeException(0, "hardware-provenance");
            if (!key.Value.IsCng && (rsa is null || key.Value.KeySpec is not (1 or 2))) throw new WindowsSigningNativeException(0, "key-specification");
            var reader = _native.ReaderName(key.Value);
            if (!string.IsNullOrWhiteSpace(reader) && !string.IsNullOrWhiteSpace(credential.ReaderName) && !string.Equals(reader, credential.ReaderName, StringComparison.OrdinalIgnoreCase))
                return CredentialPreparationResult.Failed(CredentialSignResult.Failure(CredentialFailureKind.CertificateMismatch, "Windows selected a different signing reader.", "reader-binding"));
            ct.ThrowIfCancellationRequested();
            var session = new PreparedWindowsSession(_native, worker, key.Value, selected, credential, _gate, _clock);
            transferred = true;
            return CredentialPreparationResult.Ready(session);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return CredentialPreparationResult.Failed(MapFailure(ex, stage)); }
        finally
        {
            if (!transferred && key.HasValue) DisposeResource(() => _native.Release(key.Value));
            foreach (var certificate in certificates) if (!transferred || certificate != selected) DisposeResource(certificate.Dispose);
        }
    }
    private static CredentialPreparationResult Unavailable(string message) => CredentialPreparationResult.Failed(CredentialSignResult.Failure(CredentialFailureKind.Unavailable, message, "capability"));
    internal static void DisposeResource(Action dispose) { try { dispose(); } catch { /* Cleanup cannot turn a terminal failure into a retry. */ } }
    internal static CredentialSignResult MapFailure(Exception exception, string stage)
    {
        WindowsSigningNativeException? native = null;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is WindowsSigningNativeException found) { native = found; break; }
        var kind = native?.Code switch
        {
            1223 or 0x80090036 or 0x80100002 or 0x8010006e => CredentialFailureKind.Cancelled,
            0x8010006b => CredentialFailureKind.WrongPin,
            0x8010006c => CredentialFailureKind.PinLocked,
            0x80100069 => CredentialFailureKind.CardRemoved,
            0x80090015 => CredentialFailureKind.CertificateMismatch,
            _ => CredentialFailureKind.SigningFailed,
        };
        var message = kind switch
        {
            CredentialFailureKind.Cancelled => "Smart-card authentication was cancelled.",
            CredentialFailureKind.WrongPin => "Badge PIN was not accepted. No automatic retry was attempted.",
            CredentialFailureKind.PinLocked => "Badge PIN is locked. Contact your smart-card administrator.",
            CredentialFailureKind.CardRemoved => "The selected signing card was removed.",
            CredentialFailureKind.CertificateMismatch => "Windows private key does not match the captured signing certificate.",
            _ => "Windows smart-card operation failed.",
        };
        return CredentialSignResult.Failure(kind, message, native?.Stage ?? stage, native?.Code);
    }
    public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default) => SignAsync(payload, credential, null, false, cancellationToken);
    public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default) => SignAsync(pdf, credential, signingTime, true, cancellationToken);
    private async Task<CredentialSignResult> SignAsync(byte[] data, OperatorCredential credential, DateTimeOffset? at, bool pdf, CancellationToken ct)
    {
        if (data.Length == 0) return CredentialSignResult.Failed("Nothing to sign.");
        var prepared = await PrepareSigningAsync(credential, cancellationToken: ct).ConfigureAwait(false);
        using var session = prepared.Session;
        if (session is null) return prepared.Failure!;
        return pdf
            ? await ((IEmbeddedPdfSigningBroker)session).TrySignPdfAsync(data, credential, signingTime: at, cancellationToken: ct).ConfigureAwait(false)
            : await session.TrySignPayloadAsync(data, credential, cancellationToken: ct).ConfigureAwait(false);
    }
}

internal sealed class PreparedWindowsSession : IPreparedCredentialSession, IEmbeddedPdfSigningBroker
{
    private readonly IWindowsSigningNative _native;
    private readonly WindowsSigningWorker _worker;
    private readonly WindowsSigningKey _key;
    private readonly X509Certificate2 _certificate;
    private readonly OperatorCredential _credential;
    private readonly SemaphoreSlim _gate;
    private readonly IClock _clock;
    private readonly object _state = new();
    private bool _disposed;
    private readonly bool _rsa;
    private readonly int _keySize;
    private readonly HashAlgorithmName _digest;
    public PreparedWindowsSession(IWindowsSigningNative native, WindowsSigningWorker worker, WindowsSigningKey key, X509Certificate2 certificate, OperatorCredential credential, SemaphoreSlim gate, IClock clock)
    {
        _native = native; _worker = worker; _key = key; _certificate = certificate; _credential = credential; _gate = gate; _clock = clock;
        using var rsa = certificate.GetRSAPublicKey(); using var ec = certificate.GetECDsaPublicKey();
        _rsa = rsa is not null; _keySize = rsa?.KeySize ?? ec!.KeySize; _digest = !_rsa && _keySize == 384 ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA256;
    }
    public bool IsMock => false;
    public bool CanSign => true;
    public bool CanSignPdf => true;
    public string? SigningAlgorithm => _rsa ? AttestationAlgorithm.PivRsaPkcs1Sha256 : _keySize == 384 ? AttestationAlgorithm.PivEcdsaSha384 : AttestationAlgorithm.PivEcdsaSha256;
    public string StatusText => "Windows signing session prepared.";
    public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    private byte[] SignHash(byte[] hash, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var size = _rsa ? (_keySize + 7) / 8 : 2 * ((_keySize + 7) / 8);
        var raw = _native.SignHash(_key, hash, _rsa, size);
        ct.ThrowIfCancellationRequested();
        if (raw.Length != size) throw new WindowsSigningNativeException(0, "signature-format");
        return raw;
    }
    internal static byte[] EcdsaDer(byte[] raw)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER); writer.PushSequence();
        writer.WriteIntegerUnsigned(raw.AsSpan(0, raw.Length / 2)); writer.WriteIntegerUnsigned(raw.AsSpan(raw.Length / 2)); writer.PopSequence();
        return writer.Encode();
    }
    private byte[] SignMessage(byte[] message, CancellationToken ct)
    {
        var raw = SignHash(_digest == HashAlgorithmName.SHA384 ? SHA384.HashData(message) : SHA256.HashData(message), ct);
        return _rsa ? raw : EcdsaDer(raw);
    }
    private Task<CredentialSignResult> Execute(OperatorCredential expected, CancellationToken ct, Func<CredentialSignResult> action)
    {
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _worker.InvokeAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (!string.Equals(expected.Thumbprint, _certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(expected.Serial, _credential.Serial, StringComparison.OrdinalIgnoreCase))
                    return CredentialSignResult.Failure(CredentialFailureKind.CertificateMismatch, "Prepared signer does not match the captured certificate.", "signing");
                try { var result = action(); ct.ThrowIfCancellationRequested(); return result; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return WindowsOperatorCredentialBroker.MapFailure(ex, "signing"); }
            });
        }
    }
    public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
        => Execute(credential, cancellationToken, () => CredentialSignResult.Signed(SignMessage(payload, cancellationToken), SigningAlgorithm!, _certificate.RawData, _certificate.Thumbprint, _credential));
    public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
        => Execute(credential, cancellationToken, () =>
        {
            Exception? failure = null;
            var external = new WindowsExternalSignature(_rsa ? "RSA" : "ECDSA", _digest == HashAlgorithmName.SHA384 ? "SHA-384" : "SHA-256", message =>
            { try { return SignMessage(message, cancellationToken); } catch (Exception ex) { failure = ex; throw; } });
            if (!ITextPadesSignature.TrySign(pdf, _certificate, external, credential.DisplayName, signingTime ?? _clock.UtcNow, out var signed, out var cms, out var error))
            {
                if (failure is OperationCanceledException) cancellationToken.ThrowIfCancellationRequested();
                return failure is null ? CredentialSignResult.Failed(error ?? "PDF signing failed.") : WindowsOperatorCredentialBroker.MapFailure(failure, "signing");
            }
            return CredentialSignResult.SignedPdfDocument(signed, cms, SigningAlgorithm!, _certificate.RawData, _certificate.Thumbprint, _credential);
        });
    public void Dispose()
    {
        Task<bool> cleanup;
        lock (_state)
        {
            if (_disposed) return; _disposed = true;
            cleanup = _worker.InvokeAsync(() => { WindowsOperatorCredentialBroker.DisposeResource(() => _native.Release(_key)); WindowsOperatorCredentialBroker.DisposeResource(_certificate.Dispose); return true; });
        }
        try { cleanup.GetAwaiter().GetResult(); }
        finally { try { _worker.Dispose(); } finally { _gate.Release(); } }
    }
}

internal sealed class WindowsExternalSignature(string algorithm, string digest, Func<byte[], byte[]> sign) : iText.Signatures.IExternalSignature
{
    public string GetDigestAlgorithmName() => digest;
    public string GetSignatureAlgorithmName() => algorithm;
    public iText.Signatures.ISignatureMechanismParams? GetSignatureMechanismParameters() => null;
    public byte[] Sign(byte[] message) => sign(message);
}
