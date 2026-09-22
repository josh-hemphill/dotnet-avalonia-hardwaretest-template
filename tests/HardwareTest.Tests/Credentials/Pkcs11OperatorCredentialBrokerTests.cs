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
            new OperatorCredential { DisplayName = "Jane Certifier", Serial = "badge-1" });

        Assert.True(result.PinRequired);
        Assert.False(result.Succeeded);
        Assert.Contains("PIN", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Configured_pkcs11_module_path_wins_discovery()
    {
        Assert.Equal("vendor-piv-module.so", Pkcs11ModuleResolver.Resolve(" vendor-piv-module.so "));
    }
}
