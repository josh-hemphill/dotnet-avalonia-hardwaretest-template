using HardwareTest.Core.Credentials;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Reporting;
using Net.Pkcs11Interop.Common;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class Pkcs11OperatorCredentialBrokerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_module_reports_configuration_before_requesting_token_pin(bool pdf)
    {
        var result = await SignAsync(CreateBroker(), pdf, CapturedBadge());

        Assert.False(result.PinRequired);
        Assert.False(result.Succeeded);
        Assert.False(result.PresenceFallbackAllowed);
        Assert.Equal(CredentialFailureKind.ConfigurationError, result.FailureKind);
        Assert.Equal("module-load", result.Stage);
    }

    [Fact]
    public void Configured_pkcs11_module_path_wins_discovery()
    {
        Assert.Equal("vendor-piv-module.so", Pkcs11ModuleResolver.Resolve(" vendor-piv-module.so "));
    }

    [Fact]
    public void Discovery_does_not_return_an_unloadable_bare_library_name()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "missing-opensc-pkcs11.so");

        var resolved = Pkcs11ModuleResolver.ResolveCandidates(
            [rooted, "missing-opensc-pkcs11.so"],
            _ => false,
            _ => false);

        Assert.Null(resolved);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "123456")]
    [InlineData(true, null)]
    [InlineData(true, "123456")]
    public async Task Missing_9c_thumbprint_fails_before_pin_and_allows_policy_fallback(bool pdf, string? pin)
    {
        var result = await SignAsync(CreateBroker(), pdf,
            new OperatorCredential { DisplayName = "Jane Certifier", Serial = "badge-1" }, pin);

        Assert.False(result.Succeeded);
        Assert.False(result.PinRequired);
        Assert.True(result.PresenceFallbackAllowed);
        Assert.Contains("cannot be bound safely", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_present_signing_candidates_allow_explicit_unavailable_policy_fallback()
    {
        var selection = Pkcs11OperatorCredentialBroker.SelectCapturedCertificate([], "001122");

        Assert.Null(selection.Index);
        Assert.NotNull(selection.Failure);
        Assert.False(selection.Failure.Succeeded);
        Assert.True(selection.Failure.PresenceFallbackAllowed);
    }

    [Fact]
    public void Different_signing_certificate_is_a_failure_without_policy_fallback()
    {
        var selection = Pkcs11OperatorCredentialBroker.SelectCapturedCertificate(["AABBCC"], "001122");

        Assert.Null(selection.Index);
        Assert.NotNull(selection.Failure);
        Assert.False(selection.Failure.Succeeded);
        Assert.False(selection.Failure.PresenceFallbackAllowed);
        Assert.Contains("does not match", selection.Failure.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_captured_certificates_are_ambiguous_without_policy_fallback()
    {
        var selection = Pkcs11OperatorCredentialBroker.SelectCapturedCertificate(["AABBCC", "aabbcc"], "AABBCC");

        Assert.Null(selection.Index);
        Assert.NotNull(selection.Failure);
        Assert.False(selection.Failure.PresenceFallbackAllowed);
        Assert.Contains("More than one", selection.Failure.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Unique_captured_certificate_is_selected_among_other_badges_case_insensitively()
    {
        var selection = Pkcs11OperatorCredentialBroker.SelectCapturedCertificate(["001122", "aabbcc", "DDEEFF"], "AABBCC");

        Assert.Equal(1, selection.Index);
        Assert.Null(selection.Failure);
    }

    [Fact]
    public void Signing_candidates_never_substitute_for_missing_captured_identity()
    {
        var selection = Pkcs11OperatorCredentialBroker.SelectCapturedCertificate(["AABBCC"], null);

        Assert.Null(selection.Index);
        Assert.NotNull(selection.Failure);
        Assert.True(selection.Failure.PresenceFallbackAllowed);
        Assert.Contains("cannot be bound safely", selection.Failure.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CKR.CKR_PIN_INCORRECT, CredentialFailureKind.WrongPin)]
    [InlineData(CKR.CKR_PIN_LOCKED, CredentialFailureKind.PinLocked)]
    [InlineData(CKR.CKR_USER_NOT_LOGGED_IN, CredentialFailureKind.SigningFailed)]
    public void Native_pin_errors_are_terminal_and_never_allow_presence_fallback(CKR code, CredentialFailureKind expected)
    {
        var result = Pkcs11OperatorCredentialBroker.MapFailure(
            new InvalidOperationException("Provider failed.", new Pkcs11Exception("C_Login", code)), "authentication");
        Assert.Equal(expected, result.FailureKind);
        Assert.False(result.PinRequired);
        Assert.False(result.PresenceFallbackAllowed);
        Assert.Equal((ulong)code, result.NativeCode);
    }

    [Fact]
    public void Generic_provider_message_cannot_be_classified_as_a_native_pin_request()
    {
        var result = Pkcs11OperatorCredentialBroker.MapFailure(new Exception("CKR_PIN_INCORRECT"), "signing");
        Assert.Equal(CredentialFailureKind.SigningFailed, result.FailureKind);
        Assert.False(result.PinRequired);
        Assert.False(result.PresenceFallbackAllowed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Signing_waits_for_presence_io_and_honors_cancellation(bool pdf)
    {
        var presence = new BlockingPresenceBroker();
        var broker = CreateBroker(presence);
        var capture = broker.WaitForPresenceAsync(TimeSpan.FromSeconds(30));
        await presence.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();

        try
        {
            var signing = SignAsync(broker, pdf, CapturedBadge(), "123456", cts.Token);
            Assert.False(signing.IsCompleted);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signing);
        }
        finally
        {
            presence.Release.TrySetResult();
            Assert.True((await capture).Succeeded);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Already_cancelled_signing_does_not_request_pin_or_access_module(bool pdf)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SignAsync(CreateBroker(), pdf, CapturedBadge(), cancellationToken: cts.Token));
    }

    [Fact]
    public void Setup_diagnostics_handles_invalid_explicit_path_without_identity_or_pin()
    {
        var result = SigningSetupDiagnostics.Check(new AppSettings { Pkcs11LibraryPath = "invalid\0module" });
        Assert.False(result.Available); Assert.Equal("module-discovery", result.Stage);
        Assert.DoesNotContain("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(System.Runtime.InteropServices.Architecture.X86, (ushort)0x14c)]
    [InlineData(System.Runtime.InteropServices.Architecture.X64, (ushort)0x8664)]
    [InlineData(System.Runtime.InteropServices.Architecture.Arm64, (ushort)0xaa64)]
    public void Module_pe_machine_must_match_process_architecture(System.Runtime.InteropServices.Architecture architecture, ushort machine)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            var image = File.ReadAllBytes(typeof(Pkcs11OperatorCredentialBroker).Assembly.Location);
            var peOffset = BitConverter.ToInt32(image, 0x3c);
            BitConverter.GetBytes(machine).CopyTo(image, peOffset + 4);
            File.WriteAllBytes(path, image);
            Assert.True(Pkcs11ModuleResolver.IsCompatibleArchitecture(path, architecture));
            var wrong = architecture == System.Runtime.InteropServices.Architecture.X86 ? System.Runtime.InteropServices.Architecture.X64 : System.Runtime.InteropServices.Architecture.X86;
            Assert.False(Pkcs11ModuleResolver.IsCompatibleArchitecture(path, wrong));
        }
        finally { File.Delete(path); }
    }

    private static Pkcs11OperatorCredentialBroker CreateBroker(IOperatorCredentialPresenceBroker? presence = null)
        => new(new AppSettings { Pkcs11LibraryPath = "definitely-not-a-real-pkcs11-module" }, presence: presence);

    private static OperatorCredential CapturedBadge()
        => new() { DisplayName = "Jane Certifier", Serial = "badge-1", Thumbprint = "001122" };

    private static Task<CredentialSignResult> SignAsync(
        Pkcs11OperatorCredentialBroker broker,
        bool pdf,
        OperatorCredential credential,
        string? pin = null,
        CancellationToken cancellationToken = default)
        => pdf
            ? broker.TrySignPdfAsync(PdfTestFixture.CreateMinimalPdf(), credential, pin,
                cancellationToken: cancellationToken)
            : broker.TrySignPayloadAsync("probe"u8.ToArray(), credential, pin, cancellationToken);

    private sealed class BlockingPresenceBroker : IOperatorCredentialPresenceBroker
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string StatusText => "Test presence.";

        public async Task<CredentialCaptureResult> WaitForPresenceAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            _ = timeout;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new CredentialCaptureResult { Credential = CapturedBadge() };
        }
    }
}
