using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Settings;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_pdf_signature_is_called_once_and_verifies(bool ecc)
    {
        using var selected = new TestToken(ecc); var broker = Broker(new TestBackend(selected));
        var prepared = await broker.PrepareSigningAsync(selected.Credential, "1234");
        using var session = prepared.Session;
        var result = await ((IEmbeddedPdfSigningBroker)session!).TrySignPdfAsync(PdfPadesSignature.CreateMinimalPdf(), selected.Credential, "1234");
        Assert.True(result.Succeeded, result.Error); Assert.Equal(1, selected.SignCount);
        Assert.True(ITextPadesSignature.TryVerify(result.SignedPdf!, out var error), error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Document_api_preserves_detached_cms(bool ecc)
    {
        using var selected = new TestToken(ecc); var broker = Broker(new TestBackend(selected));
        var data = "document"u8.ToArray();
        var result = await broker.TrySignDocumentAsync(data, selected.Credential, "1234");
        Assert.True(result.Succeeded, result.Error); Assert.Equal(1, selected.SignCount);
        var cms = new SignedCms(new ContentInfo(data), true); cms.Decode(result.Signature!); cms.CheckSignature(true);
    }

    [Fact]
    public async Task Pdf_callback_preserves_locked_pin_native_failure()
    {
        using var selected = new TestToken { SignFailure = CKR.CKR_PIN_LOCKED };
        var result = await Broker(new TestBackend(selected)).TrySignPdfAsync(PdfPadesSignature.CreateMinimalPdf(), selected.Credential, "1234");
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

    private static Pkcs11OperatorCredentialBroker Broker(TestBackend backend)
        => new(new AppSettings { Pkcs11LibraryPath = "injected-test-module" }, _ => backend, presence: new MockOperatorCredentialBroker());

    internal sealed class TestBackend(params IPkcs11Token[] tokens) : IPkcs11Backend
    {
        public bool Closed { get; private set; }
        public IReadOnlyList<IPkcs11Token> OpenTokens() => tokens;
        public void Dispose() => Closed = true;
    }
    internal sealed class TestToken : IPkcs11Token
    {
        private readonly RSA? _rsa;
        private readonly ECDsa? _ec;
        private readonly X509Certificate2 _cert;
        public TestToken(bool ecc = false)
        {
            if (ecc) { _ec = ECDsa.Create(ECCurve.NamedCurves.nistP384); _cert = new CertificateRequest("CN=Synthetic", _ec, HashAlgorithmName.SHA384).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)); }
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
        public IReadOnlyList<Pkcs11Certificate> ReadPublicCertificates() { Assert.Equal(0, LoginCount); PublicRead = true; return [new(_cert.RawData, [2])]; }
        public void Login(byte[]? pin) { BeforeLogin?.Invoke(); LoginCount++; LastPin = pin; if (LoginFailure is { } code) throw new Pkcs11Exception("C_Login", code); }
        public void BindPrivateKey(byte[] certificateId) { Assert.True(LoginCount > 0); Assert.Equal(new byte[] { 2 }, certificateId); Bound = true; }
        public byte[] Sign(CKM mechanism, byte[] data, byte[]? contextPin)
        {
            Assert.True(Bound); BeforeSign?.Invoke(); SignCount++; LastContextPin = contextPin; if (AlwaysAuthenticate) ContextSignCount++;
            if (SignFailure is { } code) throw new Pkcs11Exception("C_Sign", code);
            return _rsa is not null ? _rsa.SignHash(data[^32..], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                : _ec!.SignHash(data, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        public void Dispose() { Closed = true; }
    }
}
