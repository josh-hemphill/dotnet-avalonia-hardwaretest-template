using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Reporting;
using Net.Pkcs11Interop.Common;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class PreparedPkcs11Tests
{
    [Fact]
    public async Task Discovery_reads_all_public_certificates_before_login_and_never_authenticates_unrelated_token()
    {
        using var selected = new TestToken(); using var unrelated = new TestToken();
        var backend = new TestBackend(unrelated, selected);
        selected.BeforeLogin = () => { Assert.True(unrelated.PublicRead); Assert.True(selected.PublicRead); Assert.Equal(0, unrelated.LoginCount); };
        var broker = Broker(backend);
        var preparation = await broker.PrepareSigningAsync(selected.Credential, "1234");
        using var session = preparation.Session;
        Assert.NotNull(session); Assert.True(selected.Bound); Assert.Equal(0, selected.SignCount);
        Assert.True(unrelated.Closed); Assert.False(selected.Closed);
        Assert.All(selected.LastPin!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Ordinary_selected_token_requires_app_pin_after_discovery()
    {
        using var selected = new TestToken(); var backend = new TestBackend(selected);
        var result = await Broker(backend).PrepareSigningAsync(selected.Credential);
        Assert.Equal(CredentialFailureKind.PinRequired, result.Failure!.FailureKind);
        Assert.True(selected.PublicRead); Assert.Equal(0, selected.LoginCount); Assert.True(selected.Closed); Assert.True(backend.Closed);
    }

    [Fact]
    public async Task Protected_path_uses_null_token_and_context_pin_without_app_pin()
    {
        using var selected = new TestToken { ProtectedAuthenticationPath = true, AlwaysAuthenticate = true };
        var result = await Broker(new TestBackend(selected)).PrepareSigningAsync(selected.Credential);
        using var session = result.Session;
        Assert.NotNull(session); Assert.Null(selected.LastPin);
        var signed = await session.TrySignPayloadAsync("payload"u8.ToArray(), selected.Credential);
        Assert.True(signed.Succeeded); Assert.Equal(1, selected.ContextSignCount); Assert.Null(selected.LastContextPin);
    }

    [Theory]
    [InlineData(CKR.CKR_PIN_INCORRECT, CredentialFailureKind.WrongPin)]
    [InlineData(CKR.CKR_PIN_LOCKED, CredentialFailureKind.PinLocked)]
    [InlineData(CKR.CKR_FUNCTION_CANCELED, CredentialFailureKind.Cancelled)]
    [InlineData(CKR.CKR_DEVICE_REMOVED, CredentialFailureKind.CardRemoved)]
    public async Task Authentication_failure_is_terminal_and_releases_every_resource(CKR code, CredentialFailureKind kind)
    {
        using var selected = new TestToken { LoginFailure = code }; var backend = new TestBackend(selected);
        var broker = Broker(backend);
        var result = await broker.PrepareSigningAsync(selected.Credential, "1234");
        Assert.Equal(kind, result.Failure!.FailureKind); Assert.Equal((ulong)code, result.Failure.NativeCode);
        Assert.Equal("authentication", result.Failure.Stage); Assert.False(result.Failure.PinRequired); Assert.False(result.Failure.PresenceFallbackAllowed);
        Assert.Equal(1, selected.LoginCount); Assert.True(selected.Closed); Assert.True(backend.Closed);
        Assert.All(selected.LastPin!, value => Assert.Equal(0, value));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var again = await broker.PrepareSigningAsync(selected.Credential, "1234", timeout.Token);
        Assert.Null(again.Session);
    }

    [Fact]
    public async Task Certificate_mismatch_and_duplicate_match_fail_before_any_login()
    {
        using var selected = new TestToken(); using var unrelated = new TestToken();
        var mismatch = await Broker(new TestBackend(unrelated)).PrepareSigningAsync(selected.Credential, "1234");
        Assert.Equal(CredentialFailureKind.CertificateMismatch, mismatch.Failure!.FailureKind); Assert.Equal(0, unrelated.LoginCount);
        var ambiguous = await Broker(new TestBackend(selected, selected)).PrepareSigningAsync(selected.Credential, "1234");
        Assert.Equal(CredentialFailureKind.CertificateMismatch, ambiguous.Failure!.FailureKind); Assert.Equal(0, selected.LoginCount);
    }

    [Fact]
    public async Task Prepared_session_owns_gate_until_disposal_and_context_pin_is_zeroed()
    {
        using var selected = new TestToken { AlwaysAuthenticate = true }; var backend = new TestBackend(selected);
        var broker = Broker(backend);
        var preparation = await broker.PrepareSigningAsync(selected.Credential, "1234");
        var session = preparation.Session!;
        using var ct = new CancellationTokenSource();
        var pending = broker.WaitForPresenceAsync(TimeSpan.FromSeconds(1), ct.Token);
        Assert.False(pending.IsCompleted);
        var signed = await session.TrySignPayloadAsync("payload"u8.ToArray(), selected.Credential, "1234");
        Assert.True(signed.Succeeded); Assert.Equal(1, selected.ContextSignCount);
        Assert.All(selected.LastContextPin!, value => Assert.Equal(0, value));
        session.Dispose(); Assert.True(selected.Closed); Assert.True(backend.Closed); Assert.True((await pending).Succeeded);
    }

    [Fact]
    public async Task Prepared_signer_rejects_changed_captured_identity_without_using_key()
    {
        using var selected = new TestToken();
        var prepared = await Broker(new TestBackend(selected)).PrepareSigningAsync(selected.Credential, "1234");
        using var session = prepared.Session!;
        var changedIdentity = new OperatorCredential { Thumbprint = selected.Credential.Thumbprint, Serial = "different" };
        var result = await ((IEmbeddedPdfSigningBroker)session).TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), changedIdentity);
        Assert.Equal(CredentialFailureKind.CertificateMismatch, result.FailureKind);
        Assert.Equal(0, selected.SignCount);
        Assert.Null(result.SignedPdf);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_pdf_signature_is_called_once_and_verifies(bool ecc)
    {
        using var selected = new TestToken(ecc); var broker = Broker(new TestBackend(selected));
        var prepared = await broker.PrepareSigningAsync(selected.Credential, "1234");
        using var session = prepared.Session;
        var result = await ((IEmbeddedPdfSigningBroker)session!).TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), selected.Credential, "1234");
        Assert.True(result.Succeeded, result.Error); Assert.Equal(1, selected.SignCount);
        Assert.True(ITextPadesSignature.TryVerify(result.SignedPdf!, selected.Credential.Thumbprint, out var error), error);
    }

    [Fact]
    public async Task Pdf_callback_preserves_locked_pin_native_failure()
    {
        using var selected = new TestToken { SignFailure = CKR.CKR_PIN_LOCKED };
        var result = await Broker(new TestBackend(selected)).TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), selected.Credential, "1234");
        Assert.Equal(CredentialFailureKind.PinLocked, result.FailureKind); Assert.Equal((ulong)CKR.CKR_PIN_LOCKED, result.NativeCode);
        Assert.False(result.PresenceFallbackAllowed); Assert.True(selected.Closed); Assert.Equal(1, selected.SignCount);
    }

    [Fact]
    public async Task Cancellation_after_blocking_login_cleans_up_before_return()
    {
        using var selected = new TestToken(); using var ct = new CancellationTokenSource();
        selected.BeforeLogin = () => ct.Cancel(); var backend = new TestBackend(selected);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Broker(backend).PrepareSigningAsync(selected.Credential, "1234", ct.Token));
        Assert.True(selected.Closed); Assert.True(backend.Closed); Assert.Equal(0, selected.SignCount);
    }

    [Fact]
    public async Task Settings_route_is_snapshotted_for_prepared_session()
    {
        using var selected = new TestToken();
        var settings = new AppSettings { UseMockOperatorCredential = false };
        var router = new SettingsBackedCredentialBroker(settings, new MockOperatorCredentialBroker(), Broker(new TestBackend(selected)));
        var prepared = await router.PrepareSigningAsync(selected.Credential, "1234"); using var session = prepared.Session;
        settings.UseMockOperatorCredential = true;
        var result = await session!.TrySignPayloadAsync("payload"u8.ToArray(), selected.Credential, "1234");
        Assert.True(result.Succeeded); Assert.Equal(AttestationAlgorithm.PivRsaPkcs1Sha256, result.Algorithm);
    }

    [Fact]
    public async Task Cleanup_failure_preserves_authentication_error_and_attempts_all_resources()
    {
        using var selected = new TestToken { LoginFailure = CKR.CKR_PIN_LOCKED, DisposeFailure = true };
        using var other = new TestToken { DisposeFailure = true };
        var backend = new TestBackend(other, selected) { DisposeFailure = true };
        var broker = Broker(backend);
        var result = await broker.PrepareSigningAsync(selected.Credential, "1234");
        Assert.Equal(CredentialFailureKind.PinLocked, result.Failure!.FailureKind);
        Assert.True(selected.Closed); Assert.True(other.Closed); Assert.True(backend.Closed);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.True((await broker.WaitForPresenceAsync(TimeSpan.FromSeconds(1), timeout.Token)).Succeeded);
    }

    [Fact]
    public async Task Prepared_cleanup_failure_preserves_success_and_releases_gate()
    {
        using var selected = new TestToken { DisposeFailure = true };
        var backend = new TestBackend(selected) { DisposeFailure = true };
        var broker = Broker(backend);
        var prepared = await broker.PrepareSigningAsync(selected.Credential, "1234");
        var result = await prepared.Session!.TrySignPayloadAsync("payload"u8.ToArray(), selected.Credential);
        Assert.True(result.Succeeded);
        prepared.Session.Dispose();
        Assert.True(selected.Closed); Assert.True(backend.Closed);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.True((await broker.WaitForPresenceAsync(TimeSpan.FromSeconds(1), timeout.Token)).Succeeded);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(94)]
    [InlineData(95)]
    [InlineData(102)]
    public async Task Malformed_ecc_signature_is_rejected_by_every_signing_api(int length)
    {
        using var selected = new TestToken(true) { SignatureOverride = new byte[length] };
        var broker = Broker(new TestBackend(selected));
        var prepared = await broker.PrepareSigningAsync(selected.Credential, "1234");
        using var session = prepared.Session!;
        var data = "document"u8.ToArray();
        Assert.False((await session.TrySignPayloadAsync(data, selected.Credential)).Succeeded);
        Assert.False((await ((IEmbeddedPdfSigningBroker)session).TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), selected.Credential)).Succeeded);
    }

    [Fact]
    public async Task P256_pdf_uses_sha256_and_verifies_with_one_signature()
    {
        using var selected = new TestToken(true, 256);
        var broker = Broker(new TestBackend(selected));
        var prepared = await broker.PrepareSigningAsync(selected.Credential, "1234");
        using var session = prepared.Session!;
        var pdf = await ((IEmbeddedPdfSigningBroker)session).TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), selected.Credential);
        Assert.True(pdf.Succeeded, pdf.Error); Assert.Equal(1, selected.SignCount);
        Assert.Equal(AttestationAlgorithm.PivEcdsaSha256, pdf.Algorithm);
        Assert.True(ITextPadesSignature.TryVerify(pdf.SignedPdf!, selected.Credential.Thumbprint, out var error), error);
    }

    [Fact]
    public async Task Native_der_ecdsa_is_rejected_before_pdf_encoding()
    {
        using var selected = new TestToken(true) { ReturnDerSignature = true };
        var result = await Broker(new TestBackend(selected)).TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), selected.Credential, "1234");
        Assert.False(result.Succeeded); Assert.Null(result.Signature); Assert.Equal(1, selected.SignCount);
    }

    private static Pkcs11OperatorCredentialBroker Broker(TestBackend backend)
        => new(new AppSettings { Pkcs11LibraryPath = "injected-test-module" }, _ => backend, presence: new MockOperatorCredentialBroker());

    internal sealed class TestBackend(params IPkcs11Token[] tokens) : IPkcs11Backend
    {
        public bool Closed { get; private set; }
        public bool DisposeFailure { get; init; }
        public IReadOnlyList<IPkcs11Token> OpenTokens() => tokens;
        public void Dispose() { Closed = true; if (DisposeFailure) throw new IOException("Synthetic cleanup failure."); }
    }
    internal sealed class TestToken : IPkcs11Token
    {
        private readonly RSA? _rsa;
        private readonly ECDsa? _ec;
        private readonly X509Certificate2 _cert;
        public TestToken(bool ecc = false, int ecKeySize = 384)
        {
            if (ecc) { _ec = ECDsa.Create(ecKeySize == 256 ? ECCurve.NamedCurves.nistP256 : ECCurve.NamedCurves.nistP384); _cert = new CertificateRequest("CN=Synthetic", _ec, ecKeySize == 256 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA384).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)); }
            else { _rsa = RSA.Create(2048); _cert = new CertificateRequest("CN=Synthetic", _rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)); }
        }
        public OperatorCredential Credential => new() { Thumbprint = _cert.Thumbprint, DisplayName = "Synthetic", Serial = "synthetic" };
        public bool ProtectedAuthenticationPath { get; set; }
        public bool AlwaysAuthenticate { get; set; }
        public bool Closed { get; private set; }
        public bool PublicRead { get; private set; }
        public bool Bound { get; private set; }
        public int LoginCount { get; private set; }
        public int SignCount { get; private set; }
        public int ContextSignCount { get; private set; }
        public byte[]? LastPin { get; private set; }
        public byte[]? LastContextPin { get; private set; }
        public Action? BeforeLogin { get; set; }
        public Action? BeforeSign { get; set; }
        public CKR? LoginFailure { get; set; }
        public CKR? SignFailure { get; set; }
        public bool DisposeFailure { get; init; }
        public byte[]? SignatureOverride { get; init; }
        public bool ReturnDerSignature { get; init; }
        public IReadOnlyList<Pkcs11Certificate> ReadPublicCertificates() { Assert.Equal(0, LoginCount); PublicRead = true; return [new(_cert.RawData, [2])]; }
        public void Login(byte[]? pin) { BeforeLogin?.Invoke(); LoginCount++; LastPin = pin; if (LoginFailure is { } code) throw new Pkcs11Exception("C_Login", code); }
        public void BindPrivateKey(byte[] certificateId) { Assert.True(LoginCount > 0); Assert.Equal(new byte[] { 2 }, certificateId); Bound = true; }
        public byte[] Sign(CKM mechanism, byte[] data, byte[]? contextPin)
        {
            Assert.True(Bound); BeforeSign?.Invoke(); SignCount++; LastContextPin = contextPin; if (AlwaysAuthenticate) ContextSignCount++;
            if (SignFailure is { } code) throw new Pkcs11Exception("C_Sign", code);
            if (SignatureOverride is { } signature) return signature;
            return _rsa is not null ? _rsa.SignHash(data[^32..], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                : _ec!.SignHash(data, ReturnDerSignature ? DSASignatureFormat.Rfc3279DerSequence : DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        public void Dispose() { Closed = true; if (DisposeFailure && _throwOnDispose) { _throwOnDispose = false; throw new IOException("Synthetic cleanup failure."); } }
        private bool _throwOnDispose = true;
    }
}
