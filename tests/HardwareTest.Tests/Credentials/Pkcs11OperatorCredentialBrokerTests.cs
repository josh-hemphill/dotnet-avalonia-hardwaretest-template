using HardwareTest.Core.Credentials;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Reporting;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class Pkcs11OperatorCredentialBrokerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Signing_without_pin_requests_pin_before_loading_native_module(bool pdf)
    {
        var result = await SignAsync(CreateBroker(), pdf, CapturedBadge());

        Assert.True(result.PinRequired);
        Assert.False(result.Succeeded);
        Assert.False(result.PresenceFallbackAllowed);
        Assert.Contains("PIN", result.Error, StringComparison.OrdinalIgnoreCase);
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
    [InlineData("CKR_PIN_INCORRECT")]
    [InlineData("ckr_user_not_logged_in")]
    public void Nested_retryable_pin_failure_requests_pin_without_presence_fallback(string message)
    {
        var result = Pkcs11OperatorCredentialBroker.ClassifyPinFailure(
            new InvalidOperationException("Provider failed.", new Exception(message)));

        Assert.NotNull(result);
        Assert.True(result.PinRequired);
        Assert.False(result.PresenceFallbackAllowed);
        Assert.Null(result.PinRetriesRemaining);
    }

    [Fact]
    public void Nested_locked_pin_overrides_retryable_outer_error()
    {
        var result = Pkcs11OperatorCredentialBroker.ClassifyPinFailure(
            new InvalidOperationException("CKR_PIN_INCORRECT", new Exception("CKR_PIN_LOCKED")));

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.False(result.PinRequired);
        Assert.False(result.PresenceFallbackAllowed);
        Assert.Equal(0, result.PinRetriesRemaining);
        Assert.Equal("Badge PIN is locked.", result.Error);
    }

    [Fact]
    public void Generic_provider_failure_is_not_classified_as_pin_failure()
        => Assert.Null(Pkcs11OperatorCredentialBroker.ClassifyPinFailure(
            new InvalidOperationException("Provider failed.", new Exception("CKR_DEVICE_ERROR"))));

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
