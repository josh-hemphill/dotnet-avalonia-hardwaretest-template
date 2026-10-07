using HardwareTest.Core.Credentials;
using HardwareTest.Core.Settings;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class SmartCardSigningRouterTests
{
    [Theory]
    [InlineData(CredentialFailureKind.Unavailable)]
    [InlineData(CredentialFailureKind.WrongPin)]
    [InlineData(CredentialFailureKind.PinLocked)]
    [InlineData(CredentialFailureKind.Cancelled)]
    [InlineData(CredentialFailureKind.CardRemoved)]
    [InlineData(CredentialFailureKind.CertificateMismatch)]
    [InlineData(CredentialFailureKind.SigningFailed)]
    [InlineData(CredentialFailureKind.ConfigurationError)]
    public async Task Selected_backend_failure_never_substitutes_another_provider(CredentialFailureKind failure)
    {
        var windows = new Provider(failure); var pkcs = new Provider();
        var router = Router(new AppSettings { PhysicalSigningBackend = PhysicalSigningBackend.Windows }, windows, pkcs);
        var prepared = await router.PrepareSigningAsync(Credential, "app-pin");
        Assert.Null(prepared.Session);
        Assert.Equal(1, windows.PrepareCount);
        Assert.Null(windows.Pin);
        Assert.Equal(0, pkcs.PrepareCount);
        Assert.Equal(failure, prepared.Failure!.FailureKind);
    }

    [Theory]
    [InlineData(PhysicalSigningBackend.Pkcs11, "", false)]
    [InlineData(PhysicalSigningBackend.Pkcs11, "explicit-module", false)]
    [InlineData(PhysicalSigningBackend.Windows, "explicit-module", true)]
    [InlineData(PhysicalSigningBackend.Windows, "", true)]
    public async Task Explicit_backend_is_authoritative_regardless_of_pkcs11_path(PhysicalSigningBackend backend, string path, bool selectsWindows)
    {
        var windows = new Provider(); var pkcs = new Provider();
        var router = Router(new AppSettings { PhysicalSigningBackend = backend, Pkcs11LibraryPath = path }, windows, pkcs);
        var prepared = await router.PrepareSigningAsync(Credential, "app-pin");
        using var session = prepared.Session;
        Assert.NotNull(session);
        Assert.Equal(selectsWindows ? 1 : 0, windows.PrepareCount);
        Assert.Equal(selectsWindows ? 0 : 1, pkcs.PrepareCount);
        Assert.Equal(selectsWindows ? null : "app-pin", selectsWindows ? windows.Pin : pkcs.Pin);
    }

    [Fact]
    public async Task Unknown_backend_fails_before_querying_either_provider()
    {
        var windows = new Provider(); var pkcs = new Provider();
        var result = await Router(new AppSettings { PhysicalSigningBackend = (PhysicalSigningBackend)123 }, windows, pkcs)
            .PrepareSigningAsync(Credential);
        Assert.Equal(CredentialFailureKind.ConfigurationError, result.Failure!.FailureKind);
        Assert.Equal(0, windows.PrepareCount);
        Assert.Equal(0, pkcs.PrepareCount);
    }

    [Fact]
    public async Task Settings_snapshot_and_outer_gate_survive_preparation_and_retained_session()
    {
        var settings = new AppSettings { PhysicalSigningBackend = PhysicalSigningBackend.Windows }; var windows = new Provider(); var pkcs = new Provider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        windows.BeforePrepare = async () => { entered.SetResult(); await release.Task; };
        AppSettings? snapshot = null;
        var router = new SmartCardSigningRouter(settings, new MockOperatorCredentialBroker(), captured => { snapshot = captured; return windows; }, _ => pkcs);
        var preparing = router.PrepareSigningAsync(Credential); await entered.Task;
        settings.PhysicalSigningBackend = PhysicalSigningBackend.Pkcs11; settings.Pkcs11LibraryPath = "new-module";
        var presence = router.WaitForPresenceAsync(TimeSpan.FromSeconds(1)); Assert.False(presence.IsCompleted);
        release.SetResult(); var result = await preparing; var session = result.Session!;
        Assert.Equal(PhysicalSigningBackend.Windows, snapshot!.PhysicalSigningBackend); Assert.Equal("", snapshot.Pkcs11LibraryPath);
        Assert.False(presence.IsCompleted); var signed = await session.TrySignPayloadAsync([1], Credential);
        Assert.True(signed.Succeeded); Assert.Equal(1, windows.SignCount); Assert.Equal(0, pkcs.SignCount);
        session.Dispose(); Assert.True((await presence).Succeeded);
    }
    [Fact]
    public void Windows_setup_diagnostic_checks_store_without_private_key_API()
    {
        var checks = 0;
        var diagnostic = SigningSetupDiagnostics.Check(new AppSettings { PhysicalSigningBackend = PhysicalSigningBackend.Windows }, () => true, () => checks++);
        Assert.True(diagnostic.Available); Assert.Null(diagnostic.ResolvedModule); Assert.Equal("windows-store", diagnostic.Stage);
        Assert.Equal(1, checks); Assert.Contains("have not been tested", diagnostic.Message);
    }
    [Fact]
    public void Explicit_path_diagnostic_never_queries_Windows_store()
    {
        var diagnostic = SigningSetupDiagnostics.Check(new AppSettings { Pkcs11LibraryPath = "invalid\0module" }, () => true, () => throw new Exception("Should not inspect store"));
        Assert.False(diagnostic.Available); Assert.Equal("module-discovery", diagnostic.Stage);
    }
    private static OperatorCredential Credential => new() { Thumbprint = "synthetic" };
    private static SmartCardSigningRouter Router(AppSettings settings, Provider windows, Provider pkcs)
        => new(settings, new MockOperatorCredentialBroker(), _ => windows, _ => pkcs);
    private sealed class Provider(CredentialFailureKind? failure = null) : IOperatorCredentialBroker, IPreparedCredentialSession, IEmbeddedPdfSigningBroker
    {
        public int PrepareCount { get; private set; }
        public int SignCount { get; private set; }
        public string? Pin { get; private set; }
        public Func<Task>? BeforePrepare { get; set; }
        public bool IsMock => false;
        public bool CanSign => true;
        public bool CanSignPdf => true;
        public string? SigningAlgorithm => null;
        public string StatusText => "synthetic";
        public async Task<CredentialPreparationResult> PrepareSigningAsync(OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
        {
            PrepareCount++; Pin = pin; if (BeforePrepare is not null) await BeforePrepare();
            return failure is { } kind ? CredentialPreparationResult.Failed(CredentialSignResult.Failure(kind, "synthetic")) : CredentialPreparationResult.Ready(this);
        }
        public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential, string? pin = null, CancellationToken cancellationToken = default)
        { SignCount++; return Task.FromResult(CredentialSignResult.Signed([1], "synthetic")); }
        public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential, string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
            => Task.FromResult(CredentialSignResult.Failed("Routing fixture does not create PDF signatures."));
        public void Dispose() { }
    }
}
