using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Examples.AdvancedProcedure;

internal static class ProcedureWorkspace
{
    internal static void Create(string sourceDirectory, string workspace)
    {
        if (Directory.Exists(workspace) && Directory.EnumerateFileSystemEntries(workspace).Any())
            throw new IOException("Choose an empty directory for the example Authoring workspace.");
        const string packName = "HardwareTest Advanced Fixture";
        const string tableDirectory = "tables/advanced-procedure";
        const string planId = "advanced-procedure";
        var assets = new[] { tableDirectory + "/station.json", tableDirectory + "/tests.json" };
        var plans = Path.Combine(workspace, "plans");
        Directory.CreateDirectory(Path.Combine(plans, tableDirectory));
        foreach (var file in assets.Concat([planId + ".TapPlan", planId + ".program.json"]))
            File.Copy(Path.Combine(sourceDirectory, file), Path.Combine(plans, file));

        static string Version(Type type) => type.Assembly.GetName().Version!.ToString(3);
        var version = Version(typeof(FixtureProcedureStep));
        var dependencies = new[]
        {
            new { package = "OpenTAP", version = Version(typeof(TestPlan)) },
            new { package = "HardwareTest Basic", version = Version(typeof(MockDmmInstrument)) },
            new { package = "HardwareTest Mixins", version = Version(typeof(HardwareTest.OpenTap.Plugins.Mixins.PresentationMixinBuilder)) },
            new { package = packName, version }
        };
        var fileName = $"{packName.Replace(' ', '.')}.{version}.TapPackage";
        var manifest = new
        {
            schemaVersion = 2,
            displayName = "Advanced Fixture Procedure",
            plansDirectory = "plans",
            package = new { name = "Advanced Fixture Programs", version, os = CurrentOperatingSystem() },
            dependencies,
            pluginProjects = new[] { fileName },
            catalogs = new
            {
                hardware = new[]
                {
                    SerialProfile("ac5ae705-5e41-454d-9002-a217260085ad", "Simulated DUT Serial", 115200, 1000),
                    SerialProfile("ac5ae705-5e41-454d-9002-a217260085ae", "Simulation spare serial", 57600, 1250)
                }
            },
            programAssets = new Dictionary<string, string[]> { [planId] = assets }
        };
        File.WriteAllText(Path.Combine(workspace, "authoring.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() }) + "\n");
        var payload = new[] { typeof(FixtureProcedureStep).Assembly.Location, typeof(System.IO.Ports.SerialPort).Assembly.Location }
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "*System.IO.Ports.Native*"))
            .Select(path => (Path: path, Entry: $"Packages/{packName}/{Path.GetFileName(path)}")).ToArray();
        XNamespace xml = "http://opentap.io/schemas/package";
        var metadata = new XDocument(new XElement(xml + "Package", new XAttribute("Name", packName),
            new XAttribute("Version", version), new XAttribute("Architecture", "AnyCPU"), new XAttribute("OS", CurrentOperatingSystem()),
            new XElement(xml + "Dependencies", dependencies.Take(3).Select(dep => new XElement(xml + "PackageDependency",
                new XAttribute("Package", dep.package), new XAttribute("Version", dep.version)))),
            new XElement(xml + "Files", payload.Select(file => new XElement(xml + "File", new XAttribute("Path", file.Entry))))));
        using var archive = ZipFile.Open(Path.Combine(workspace, fileName), ZipArchiveMode.Create);
        foreach (var file in payload) archive.CreateEntryFromFile(file.Path, file.Entry);
        using var stream = archive.CreateEntry($"Packages/{packName}/package.xml").Open();
        metadata.Save(stream);
    }

    private static object SerialProfile(string id, string name, int baudRate, int timeoutMs) => new
    {
        id,
        name,
        typeId = typeof(SerialDutPort).FullName,
        address = SerialDutPort.MockAddress,
        settings = new Dictionary<string, string>
        {
            ["BaudRate"] = baudRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TimeoutMs"] = timeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }
    };

    private static string CurrentOperatingSystem() => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "MacOS" : "Linux";
}
