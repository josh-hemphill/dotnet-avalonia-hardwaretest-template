using HardwareTest.Core.Credentials;
using HardwareTest.Core.Settings;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class SmartCardSigningRouterTests
{
    [Theory]
    [InlineData(CredentialFailureKind.Unavailable, true)]
    [InlineData(CredentialFailureKind.WrongPin, false)]
    [InlineData(CredentialFailureKind.PinLocked, false)]
    [InlineData(CredentialFailureKind.Cancelled, false)]
    [InlineData(CredentialFailureKind.CardRemoved, false)]
    [InlineData(CredentialFailureKind.CertificateMismatch, false)]
    [InlineData(CredentialFailureKind.SigningFailed, false)]
    [InlineData(CredentialFailureKind.ConfigurationError, false)]
    public async Task Auto_falls_back_only_for_explicit_unavailable(CredentialFailureKind failure, bool fallback)
    {
        var windows = new Provider(failure); var pkcs = new Provider();
        var router = Router(new AppSettings(), windows, pkcs);
        var prepared = await router.PrepareSigningAsync(Credential, "app-pin"); using var session = prepared.Session;
        Assert.Equal(1, windows.PrepareCount); Assert.Null(windows.Pin); Assert.Equal(fallback ? 1 : 0, pkcs.PrepareCount);
        if (fallback) Assert.Equal("app-pin", pkcs.Pin); else Assert.Equal(failure, prepared.Failure!.FailureKind);
    }
    [Theory]
    [InlineData(SmartCardSigningProviderMode.Auto, "bad-explicit-module", true, false)]
    [InlineData(SmartCardSigningProviderMode.Pkcs11, "", true, false)]
    [InlineData(SmartCardSigningProviderMode.Windows, "explicit-module", true, true)]
    [InlineData(SmartCardSigningProviderMode.Auto, "", false, false)]
    [InlineData(SmartCardSigningProviderMode.Windows, "", false, true)]
    public async Task Explicit_provider_and_path_and_platform_choose_authoritative_backend(SmartCardSigningProviderMode mode, string path, bool isWindows, bool selectsWindows)
    {
        var windows = new Provider(CredentialFailureKind.Unavailable); var pkcs = new Provider(CredentialFailureKind.ConfigurationError);
        var router = Router(new AppSettings { SmartCardSigningProviderMode = mode, Pkcs11LibraryPath = path }, windows, pkcs, isWindows);
        var prepared = await router.PrepareSigningAsync(Credential);
        Assert.Null(prepared.Session); Assert.Equal(selectsWindows ? 1 : 0, windows.PrepareCount); Assert.Equal(selectsWindows ? 0 : 1, pkcs.PrepareCount);
    }
    [Fact]
    public async Task Settings_snapshot_and_outer_gate_survive_preparation_and_retained_session()
    {
        var settings = new AppSettings(); var windows = new Provider(); var pkcs = new Provider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        windows.BeforePrepare = async () => { entered.SetResult(); await release.Task; };
        AppSettings? snapshot = null;
        var router = new SmartCardSigningRouter(settings, new MockOperatorCredentialBroker(), captured => { snapshot = captured; return windows; }, _ => pkcs, () => true);
        var preparing = router.PrepareSigningAsync(Credential); await entered.Task;
        settings.SmartCardSigningProviderMode = SmartCardSigningProviderMode.Pkcs11; settings.Pkcs11LibraryPath = "new-module";
        var presence = router.WaitForPresenceAsync(TimeSpan.FromSeconds(1)); Assert.False(presence.IsCompleted);
        release.SetResult(); var result = await preparing; var session = result.Session!;
        Assert.Equal(SmartCardSigningProviderMode.Auto, snapshot!.SmartCardSigningProviderMode); Assert.Equal("", snapshot.Pkcs11LibraryPath);
        Assert.False(presence.IsCompleted); var signed = await session.TrySignPayloadAsync([1], Credential);
        Assert.True(signed.Succeeded); Assert.Equal(1, windows.SignCount); Assert.Equal(0, pkcs.SignCount);
        session.Dispose(); Assert.True((await presence).Succeeded);
    }
    [Theory]
    [InlineData(SmartCardSigningProviderMode.Auto)]
    [InlineData(SmartCardSigningProviderMode.Windows)]
    public void Windows_setup_diagnostic_checks_store_without_private_key_API(SmartCardSigningProviderMode mode)
    {
        var checks = 0;
        var diagnostic = SigningSetupDiagnostics.Check(new AppSettings { SmartCardSigningProviderMode = mode }, () => true, () => checks++);
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
    private static SmartCardSigningRouter Router(AppSettings settings, Provider windows, Provider pkcs, bool isWindows = true)
        => new(settings, new MockOperatorCredentialBroker(), _ => windows, _ => pkcs, () => isWindows);
    private sealed class Provider(CredentialFailureKind? failure = null) : IOperatorCredentialBroker, IPreparedCredentialSession
    {
        public int PrepareCount { get; private set; }
        public int SignCount { get; private set; }
        public string? Pin { get; private set; }
        public Func<Task>? BeforePrepare { get; set; }
        public bool IsMock => false;
        public bool CanSign => true;
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
        public void Dispose() { }
    }
}
