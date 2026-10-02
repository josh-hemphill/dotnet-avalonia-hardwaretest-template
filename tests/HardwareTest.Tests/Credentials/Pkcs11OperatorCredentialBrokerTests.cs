using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Settings;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class Pkcs11OperatorCredentialBrokerTests
{
    [Fact]
    public async Task Missing_native_module_fails_before_app_pin_prompt()
    {
        var broker = new Pkcs11OperatorCredentialBroker(new AppSettings
        {
            Pkcs11LibraryPath = "definitely-not-a-real-pkcs11-module",
        });

        var result = await broker.TrySignPdfAsync(
            PdfPadesSignature.CreateMinimalPdf(),
            new OperatorCredential
            {
                DisplayName = "Jane Certifier",
                Serial = "badge-1",
                Thumbprint = "001122",
            });

        Assert.False(result.PinRequired);
        Assert.False(result.Succeeded);
        Assert.Equal(CredentialFailureKind.ConfigurationError, result.FailureKind);
        Assert.False(result.PresenceFallbackAllowed);
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

    [Fact]
    public async Task Missing_9c_thumbprint_fails_before_pin_and_allows_policy_fallback()
    {
        var broker = new Pkcs11OperatorCredentialBroker(new AppSettings
        {
            Pkcs11LibraryPath = "vendor-piv-module.so",
        });

        var result = await broker.TrySignPdfAsync(
            PdfPadesSignature.CreateMinimalPdf(),
            new OperatorCredential { DisplayName = "Jane Certifier", Serial = "badge-1" });

        Assert.False(result.PinRequired);
        Assert.True(result.PresenceFallbackAllowed);
        Assert.Contains("cannot be bound safely", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signing_waits_for_presence_io_and_honors_cancellation()
    {
        var presence = new BlockingPresenceBroker();
        var broker = new Pkcs11OperatorCredentialBroker(
            new AppSettings { Pkcs11LibraryPath = "definitely-not-a-real-pkcs11-module" },
            presence: presence);
        var capture = broker.WaitForPresenceAsync(TimeSpan.FromSeconds(30));
        await presence.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();

        var signing = broker.TrySignPdfAsync(
            PdfPadesSignature.CreateMinimalPdf(),
            new OperatorCredential
            {
                DisplayName = "Jane Certifier",
                Serial = "badge-1",
                Thumbprint = "001122",
            },
            pin: "123456",
            cancellationToken: cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signing);
        presence.Release.TrySetResult();
        Assert.True((await capture).Succeeded);
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

    private sealed class BlockingPresenceBroker : IOperatorCredentialBroker
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsMock => false;
        public bool CanSign => false;
        public string? SigningAlgorithm => null;
        public string StatusText => "Test presence.";

        public async Task<CredentialCaptureResult> WaitForPresenceAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            _ = timeout;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new CredentialCaptureResult
            {
                Credential = new OperatorCredential { DisplayName = "Jane Certifier", Serial = "badge-1" },
            };
        }

        public Task<CredentialSignResult> TrySignPayloadAsync(
            byte[] payload,
            OperatorCredential credential,
            string? pin = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
