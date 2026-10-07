using System.Xml.Linq;
using HardwareTest.Authoring.Tests;

namespace HardwareTest.Authoring.UI.Tests;

internal static class CurrentHardwareUiFixture
{
    internal const string TypeId = "InstrumentComponents.OpenTap.DcPowerSupplyInstrument";

    internal static void WritePackage(string home, bool includePayload = true)
    {
        var directory = Path.Combine(home, "Packages", AuthoringInstrumentCatalog.LibraryPackage);
        Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(PublishedLibraryFixture.PackageRoot, "package.xml"), Path.Combine(directory, "package.xml"), overwrite: true);
        if (includePayload)
            foreach (var file in new[] { "InstrumentComponents.OpenTap.dll", "InstrumentComponents.dll" })
                File.Copy(Path.Combine(PublishedLibraryFixture.PackageRoot, file), Path.Combine(home, file), overwrite: true);
    }

    internal static void Discover(AuthoringUiFixture fixture)
    {
        var home = Path.Combine(fixture.WorkspaceRoot, "discovery-home");
        WritePackage(home);
        AuthoringInstrumentCatalog.Discover(new(home));
    }

    internal static void Prepare(AuthoringUiFixture fixture)
    {
        if (fixture.ViewModel.CanDeclareLibraryDependency) fixture.ViewModel.DeclareLibraryDependency();
        var home = Path.Combine(fixture.WorkspaceRoot, "available-home");
        WritePackage(home);
        var basic = Path.Combine(home, "Packages", "HardwareTest Basic");
        Directory.CreateDirectory(basic);
        const string file = "HardwareTest.OpenTap.Plugins.Basic.dll";
        new XElement("Package", new XAttribute("Name", "HardwareTest Basic"),
            new XElement("Files", new XElement("File", new XAttribute("Path", file))))
            .Save(Path.Combine(basic, "package.xml"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(basic, file), overwrite: true);
        fixture.ViewModel.OpenTapHomeOverride = home;
        AuthoringUiFixture.Drain();
    }
}
