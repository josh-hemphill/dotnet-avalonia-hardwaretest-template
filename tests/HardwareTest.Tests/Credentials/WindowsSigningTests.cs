using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using HardwareTest.Tests.Reporting;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class WindowsSigningTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Real_pdf_signature_verifies_once_on_retained_apartment(int algorithm)
    {
        using var fixture = new Fixture(algorithm);
        var prepared = await fixture.Broker.PrepareSigningAsync(fixture.Credential, "must-never-be-forwarded");
        using var session = prepared.Session;
        Assert.NotNull(session); Assert.True(session.CanSignPdf); Assert.Equal(0, fixture.Native.SignCount);
        Assert.Equal(WindowsOperatorCredentialBroker.AcquisitionFlags, fixture.Native.Flags);
        Assert.Equal((nint)1234, fixture.Native.Owner); Assert.Equal(fixture.Source.LastContext, fixture.Native.Context);
        var pdfResult = await ((IEmbeddedPdfSigningBroker)session).TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), fixture.Credential);
        Assert.True(pdfResult.Succeeded, pdfResult.Error); Assert.Equal(1, fixture.Native.SignCount);
        Assert.True(ITextPadesSignature.TryVerify(pdfResult.SignedPdf!, fixture.Credential.Thumbprint, out var error), error);
        session.Dispose(); Assert.Equal(1, fixture.Native.ReleaseCount); Assert.Equal(1, fixture.Native.UninitializeCount);
        Assert.Single(fixture.Native.ThreadIds.Distinct());
    }
    [Fact]
    public async Task Windows_prepared_pdf_issuance_uses_current_evidence_and_exact_crypto_validation()
    {
        using var fixture = new Fixture();
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = new TestRunRecord { RunId = "windows-current-issuance" };
        var working = Path.Combine(store.GetRunDirectory(run.RunId), "certification.pdf");
        var original = PdfTestFixture.CreateMinimalPdf("Windows current contract");
        await File.WriteAllBytesAsync(working, original);
        run.Reports = [new() { Kind = ReportKinds.Certification, PdfPath = working }];
        await store.SaveAsync(run);
        var service = new ReportAttestationService(fixture.Broker, store,
            new AppSettings { RequireAttestationBeforeExport = true, AllowPresenceInLieuOfSigning = true });
        var result = await service.AttestAsync(run, ReportKinds.Certification, fixture.Credential);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, fixture.Native.SignCount);
        Assert.Equal(1, fixture.Native.ReleaseCount);
        Assert.Equal(original, await File.ReadAllBytesAsync(working));
        var revision = Assert.Single(run.Reports, r => ReportArtifactRoles.IsIssued(r.Role));
        Assert.Equal(result.Attestation!.RevisionId, revision.RevisionId);
        Assert.True(service.HasValidAttestationForPdf(run, ReportKinds.Certification, revision.PdfPath));
        Assert.True(ITextPadesSignature.TryVerify(await File.ReadAllBytesAsync(revision.PdfPath), fixture.Credential.Thumbprint, out var error), error);
        await File.AppendAllTextAsync(revision.PdfPath, "unsigned trailing bytes");
        Assert.False(service.HasValidAttestationForPdf(run, ReportKinds.Certification, revision.PdfPath));
    }

    [Fact]
    public async Task Missing_and_ambiguous_certificates_never_acquire_another_private_key()
    {
        using var fixture = new Fixture();
        fixture.Source.Count = 0;
        var missing = await fixture.Broker.PrepareSigningAsync(fixture.Credential);
        Assert.Equal(CredentialFailureKind.Unavailable, missing.Failure!.FailureKind);
        fixture.Source.Count = 2;
        var duplicate = await fixture.Broker.PrepareSigningAsync(fixture.Credential);
        Assert.Equal(CredentialFailureKind.CertificateMismatch, duplicate.Failure!.FailureKind);
        Assert.Equal(0, fixture.Native.AcquireCount);
    }
    [Theory]
    [InlineData(1223u, CredentialFailureKind.Cancelled)]
    [InlineData(0x80090036u, CredentialFailureKind.Cancelled)]
    [InlineData(0x80100002u, CredentialFailureKind.Cancelled)]
    [InlineData(0x8010006eu, CredentialFailureKind.Cancelled)]
    [InlineData(0x8010006bu, CredentialFailureKind.WrongPin)]
    [InlineData(0x8010006cu, CredentialFailureKind.PinLocked)]
    [InlineData(0x80100069u, CredentialFailureKind.CardRemoved)]
    [InlineData(0x80090015u, CredentialFailureKind.CertificateMismatch)]
    [InlineData(0xdeadbeefu, CredentialFailureKind.SigningFailed)]
    public async Task Native_authentication_failures_are_terminal_redacted(uint code, CredentialFailureKind kind)
    {
        using var fixture = new Fixture(); fixture.Native.AcquireFailure = code;
        var result = await fixture.Broker.PrepareSigningAsync(fixture.Credential);
        Assert.Equal(kind, result.Failure!.FailureKind); Assert.False(result.Failure.PresenceFallbackAllowed);
        Assert.False(result.Failure.PinRequired); Assert.Equal((ulong)code, result.Failure.NativeCode);
        Assert.DoesNotContain(fixture.Credential.Thumbprint!, result.Failure.Error!);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hardware_and_reader_binding_reject_after_acquisition(bool differentReader)
    {
        using var fixture = new Fixture(); fixture.Native.Hardware = differentReader;
        fixture.Native.Reader = differentReader ? "other reader" : null;
        var result = await fixture.Broker.PrepareSigningAsync(fixture.Credential);
        Assert.Equal(differentReader ? CredentialFailureKind.CertificateMismatch : CredentialFailureKind.SigningFailed, result.Failure!.FailureKind);
        Assert.False(result.Failure.PresenceFallbackAllowed); Assert.Equal(1, fixture.Native.ReleaseCount);
    }
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(uint.MaxValue)]
    public async Task RSA_respects_returned_CSP_or_KSP_specification(uint spec)
    {
        using var fixture = new Fixture(); fixture.Native.KeySpec = spec;
        var result = await fixture.Broker.TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), fixture.Credential);
        Assert.True(result.Succeeded, result.Error); Assert.Equal(spec, fixture.Native.SignedSpec);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Invalid_signature_width_is_terminal_for_all_signing_APIs(int algorithm)
    {
        using var fixture = new Fixture(algorithm); fixture.Native.Malformed = true;
        var prepared = await fixture.Broker.PrepareSigningAsync(fixture.Credential); using var session = prepared.Session!;
        var payload = await session.TrySignPayloadAsync("document"u8.ToArray(), fixture.Credential);
        var pdf = await ((IEmbeddedPdfSigningBroker)session).TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), fixture.Credential);
        Assert.All(new[] { payload, pdf }, result => { Assert.Equal(CredentialFailureKind.SigningFailed, result.FailureKind); Assert.False(result.Succeeded); Assert.False(result.PresenceFallbackAllowed); });
    }
    [Fact]
    public async Task PDF_callback_preserves_wrong_PIN_failure()
    {
        using var fixture = new Fixture(); fixture.Native.SignFailure = 0x8010006b;
        var result = await fixture.Broker.TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), fixture.Credential);
        Assert.Equal(CredentialFailureKind.WrongPin, result.FailureKind); Assert.Equal(1, fixture.Native.SignCount); Assert.Equal(1, fixture.Native.ReleaseCount);
    }
    [Fact]
    public async Task Blocking_acquire_cancellation_does_not_release_gate_or_resources_prematurely()
    {
        using var fixture = new Fixture(); using var entered = new ManualResetEventSlim(); using var unblock = new ManualResetEventSlim();
        using var ct = new CancellationTokenSource(); fixture.Native.BeforeAcquire = () => { entered.Set(); unblock.Wait(); };
        var pending = fixture.Broker.PrepareSigningAsync(fixture.Credential, cancellationToken: ct.Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5))); ct.Cancel();
        var presence = fixture.Broker.WaitForPresenceAsync(TimeSpan.FromSeconds(1));
        Assert.False(pending.IsCompleted); Assert.False(presence.IsCompleted); Assert.Equal(0, fixture.Native.ReleaseCount);
        unblock.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, fixture.Native.ReleaseCount); Assert.Equal(0, fixture.Native.SignCount); Assert.True((await presence).Succeeded);
    }
    [Fact]
    public async Task Dispose_waits_for_actual_native_signing_completion()
    {
        using var fixture = new Fixture(); using var entered = new ManualResetEventSlim(); using var unblock = new ManualResetEventSlim();
        using var ct = new CancellationTokenSource(); var prepared = await fixture.Broker.PrepareSigningAsync(fixture.Credential); var session = prepared.Session!;
        fixture.Native.BeforeSign = () => { entered.Set(); unblock.Wait(); };
        var signing = session.TrySignPayloadAsync("document"u8.ToArray(), fixture.Credential, cancellationToken: ct.Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5))); ct.Cancel();
        var disposing = Task.Run(session.Dispose); var presence = fixture.Broker.WaitForPresenceAsync(TimeSpan.FromSeconds(1));
        Assert.False(signing.IsCompleted); Assert.False(presence.IsCompleted); Assert.Equal(0, fixture.Native.ReleaseCount);
        unblock.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signing); await disposing;
        Assert.Equal(1, fixture.Native.ReleaseCount); Assert.True((await presence).Succeeded);
    }
    [Fact]
    public async Task Headless_and_nonWindows_are_explicit_capability_results()
    {
        using var fixture = new Fixture();
        var headless = new WindowsOperatorCredentialBroker(fixture.Native, fixture.Source, new HeadlessSigningDialogOwner(), isWindows: () => true);
        var unsupported = new WindowsOperatorCredentialBroker(fixture.Native, fixture.Source, new Owner(), isWindows: () => false);
        Assert.Equal(CredentialFailureKind.Unavailable, (await headless.PrepareSigningAsync(fixture.Credential)).Failure!.FailureKind);
        Assert.Equal(CredentialFailureKind.Unavailable, (await unsupported.PrepareSigningAsync(fixture.Credential)).Failure!.FailureKind);
        Assert.Equal(0, fixture.Native.AcquireCount);
    }
    [Fact]
    public async Task Changed_expected_certificate_cannot_sign_prepared_key()
    {
        using var fixture = new Fixture(); var preparation = await fixture.Broker.PrepareSigningAsync(fixture.Credential); using var session = preparation.Session!;
        var result = await session.TrySignPayloadAsync([1], new OperatorCredential { Thumbprint = "another-certificate" });
        Assert.Equal(CredentialFailureKind.CertificateMismatch, result.FailureKind); Assert.Equal(0, fixture.Native.SignCount);
    }
    [Fact]
    public async Task Cleanup_failures_preserve_primary_failure_and_release_gate()
    {
        using var fixture = new Fixture(); fixture.Native.Hardware = false; fixture.Native.CleanupThrows = true;
        var result = await fixture.Broker.PrepareSigningAsync(fixture.Credential);
        Assert.Equal("hardware-provenance", result.Failure!.Stage); Assert.Equal(CredentialFailureKind.SigningFailed, result.Failure.FailureKind);
        Assert.True((await fixture.Broker.WaitForPresenceAsync(TimeSpan.FromSeconds(1))).Succeeded);
        Assert.Equal(1, fixture.Native.UninitializeCount);
    }

    internal sealed class Owner : INativeSigningDialogOwner
    { public Task<nint> GetWindowHandleAsync(CancellationToken cancellationToken = default) => Task.FromResult<nint>(1234); }
    internal sealed class Source(byte[] der) : IWindowsSigningCertificateSource
    {
        public int Count { get; set; } = 1;
        public nint LastContext { get; private set; }
        public IReadOnlyList<X509Certificate2> ReadCertificates()
        {
            var certificates = Enumerable.Range(0, Count).Select(_ => X509CertificateLoader.LoadCertificate(der)).ToArray();
            if (certificates.Length > 0) LastContext = certificates[0].Handle;
            return certificates;
        }
    }
    internal sealed class Fixture : IDisposable
    {
        private readonly RSA? _rsa;
        private readonly ECDsa? _ec;
        private readonly X509Certificate2 _certificate;
        public Fixture(int algorithm = 0)
        {
            if (algorithm == 0) { _rsa = RSA.Create(2048); _certificate = new CertificateRequest("CN=Synthetic", _rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)); }
            else { _ec = ECDsa.Create(algorithm == 1 ? ECCurve.NamedCurves.nistP256 : ECCurve.NamedCurves.nistP384); _certificate = new CertificateRequest("CN=Synthetic", _ec, algorithm == 1 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA384).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)); }
            Source = new(_certificate.RawData); Native = new(_rsa, _ec); Broker = new(Native, Source, new Owner(), presence: new MockOperatorCredentialBroker(), isWindows: () => true);
        }
        public Source Source { get; }
        public Native Native { get; }
        public WindowsOperatorCredentialBroker Broker { get; }
        public OperatorCredential Credential => new() { Thumbprint = _certificate.Thumbprint, ReaderName = "synthetic reader", Serial = "windows-fixture", DisplayName = "Synthetic" };
        public void Dispose() { _certificate.Dispose(); _rsa?.Dispose(); _ec?.Dispose(); }
    }
    internal sealed class Native(RSA? rsa, ECDsa? ec) : IWindowsSigningNative
    {
        public List<int> ThreadIds { get; } = [];
        public uint Flags { get; private set; }
        public nint Owner { get; private set; }
        public nint Context { get; private set; }
        public int AcquireCount { get; private set; }
        public int SignCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public int UninitializeCount { get; private set; }
        public uint KeySpec { get; set; } = uint.MaxValue;
        public uint SignedSpec { get; private set; }
        public uint? AcquireFailure { get; set; }
        public uint? SignFailure { get; set; }
        public bool Hardware { get; set; } = true;
        public bool Malformed { get; set; }
        public bool CleanupThrows { get; set; }
        public string? Reader { get; set; }
        public Action? BeforeAcquire { get; set; }
        public Action? BeforeSign { get; set; }
        private void Record() => ThreadIds.Add(Environment.CurrentManagedThreadId);
        public void InitializeApartment() => Record();
        public void UninitializeApartment() { Record(); UninitializeCount++; if (CleanupThrows) throw new Exception("Uninitialization failed"); }
        public WindowsSigningKey Acquire(nint certificateContext, uint flags, nint ownerAddress)
        {
            Record(); AcquireCount++; Flags = flags; Owner = Marshal.ReadIntPtr(ownerAddress); Context = certificateContext; BeforeAcquire?.Invoke();
            if (AcquireFailure is { } code) throw new WindowsSigningNativeException(code, "authentication");
            return new(4321, KeySpec, true);
        }
        public bool IsHardware(WindowsSigningKey key) { Record(); return Hardware; }
        public string? ReaderName(WindowsSigningKey key) { Record(); return Reader; }
        public byte[] SignHash(WindowsSigningKey key, byte[] hash, bool isRsa, int signatureSize)
        {
            Record(); SignCount++; SignedSpec = key.KeySpec; BeforeSign?.Invoke();
            if (SignFailure is { } code) throw new WindowsSigningNativeException(code, "signing");
            if (Malformed) return new byte[signatureSize - 1];
            var signature = isRsa ? rsa!.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) : ec!.SignHash(hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            Assert.Equal(signatureSize, signature.Length); return signature;
        }
        public void Release(WindowsSigningKey key) { Record(); ReleaseCount++; if (CleanupThrows) throw new Exception("Release failed"); }
    }
}
