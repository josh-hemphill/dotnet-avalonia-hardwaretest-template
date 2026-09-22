using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Settings;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class Pkcs11OperatorCredentialBrokerTests
{
    [Fact]
    public async Task Pdf_signing_without_pin_requests_pin_before_loading_native_module()
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

        Assert.True(result.PinRequired);
        Assert.False(result.Succeeded);
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
}
