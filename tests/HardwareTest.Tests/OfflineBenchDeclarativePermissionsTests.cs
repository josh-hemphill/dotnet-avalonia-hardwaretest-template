using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Tests;

public sealed class OfflineBenchDeclarativePermissionsTests
{
    private static readonly XNamespace Namespace = "http://opentap.io/schemas/package";

    [Theory]
    [InlineData("Linux")]
    [InlineData("Windows")]
    public void Exact_built_in_declaration_is_parsed_without_executing_a_process(string os)
    {
        var xml = Metadata(os);
        var archive = OfflineBenchArchive.Read(Archive(xml), os, "x64", default);
        var path = os == "Linux" ? "tap" : "Packages/OpenTAP/Tap.Upgrader.exe";
        Assert.Equal("unexecuted fixture payload", Encoding.UTF8.GetString(archive.Payload[path]));
        Assert.Equal(os == "Linux", archive.SetsTapExecutable);
        if (os == "Linux")
            Assert.Equal(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute,
                archive.ExecutableModes[path] & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));
    }

    [Theory]
    [InlineData("Linux", "ExeFile", "sh")]
    [InlineData("Linux", "ExpectedExitCodes", "0")]
    [InlineData("Linux", "Arguments", "+x tap; unexpected")]
    [InlineData("Linux", "Quiet", "true")]
    [InlineData("Linux", "Optional", "true")]
    [InlineData("Linux", "ActionName", "uninstall")]
    [InlineData("Linux", "Extra", "unsupported")]
    [InlineData("Windows", "ExeFile", "Other.Upgrader.exe")]
    [InlineData("Windows", "ExpectedExitCodes", "0,1")]
    [InlineData("Windows", "Arguments", "--install")]
    [InlineData("Windows", "Quiet", "true")]
    [InlineData("Windows", "Optional", "true")]
    [InlineData("Windows", "ActionName", "uninstall")]
    [InlineData("Windows", "Extra", "unsupported")]
    public void Any_changed_tuple_or_extra_attribute_is_refused(string os, string attribute, string value)
    {
        var xml = Metadata(os); xml.Element(Namespace + "PackageActionExtensions")!.Element(Namespace + "ActionStep")!.SetAttributeValue(attribute, value);
        Assert.Throws<InvalidDataException>(() => OfflineBenchArchive.Read(Archive(xml), os, "x64", default));
    }

    [Theory]
    [InlineData("Linux", "name")]
    [InlineData("Linux", "version")]
    [InlineData("Linux", "platform")]
    [InlineData("Linux", "missing-payload")]
    [InlineData("Linux", "duplicate")]
    [InlineData("Linux", "nested")]
    [InlineData("Linux", "extra-content")]
    [InlineData("Windows", "name")]
    [InlineData("Windows", "version")]
    [InlineData("Windows", "platform")]
    [InlineData("Windows", "missing-payload")]
    [InlineData("Windows", "duplicate")]
    [InlineData("Windows", "nested")]
    [InlineData("Windows", "extra-content")]
    public void Exception_requires_matching_engine_identity_platform_payload_and_exact_shape(string os, string change)
    {
        var xml = Metadata(os); var container = xml.Element(Namespace + "PackageActionExtensions")!;
        switch (change)
        {
            case "name": xml.SetAttributeValue("Name", "Other package"); break;
            case "version": xml.SetAttributeValue("Version", "0.0.1"); break;
            case "platform": xml.SetAttributeValue("OS", os == "Linux" ? "Windows" : "Linux"); break;
            case "missing-payload": xml.Element(Namespace + "Files")!.Remove(); break;
            case "duplicate": xml.Add(new XElement(container)); break;
            case "nested": container.Remove(); xml.Add(new XElement(Namespace + "Other", container)); break;
            case "extra-content": container.Element(Namespace + "ActionStep")!.Add(new XElement(Namespace + "Extra")); break;
        }
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.ValidatePackageMetadata(xml));
    }

    [Fact]
    public void Declaration_cannot_change_platform_or_other_payload_permissions()
    {
        var linux = Metadata("Linux");
        Assert.Equal((UnixFileMode)0x1FF, OfflineBenchBundle.DeclarativePayloadMode(linux, "tap", (UnixFileMode)0x1B6, "Linux"));
        Assert.Equal((UnixFileMode)0x180, OfflineBenchBundle.DeclarativePayloadMode(linux, "other-helper.sh", (UnixFileMode)0x180, "Linux"));
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.DeclarativePayloadMode(linux, "tap", 0, "Windows"));
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.DeclarativePayloadMode(Metadata("Windows"), "Packages/OpenTAP/Tap.Upgrader.exe", 0, "Linux"));
    }

    private static XElement Metadata(string os)
    {
        var linux = os == "Linux";
        var path = linux ? "tap" : "Packages/OpenTAP/Tap.Upgrader.exe";
        return new(Namespace + "Package", new XAttribute("Name", "OpenTAP"),
            new XAttribute("Version", typeof(global::OpenTap.TestPlan).Assembly.GetName().Version!.ToString(3)), new XAttribute("OS", os),
            new XElement(Namespace + "Files", new XElement(Namespace + "File", new XAttribute("Path", path))),
            new XElement(Namespace + "PackageActionExtensions", new XElement(Namespace + "ActionStep",
                new XAttribute("ExeFile", linux ? "chmod" : path), new XAttribute("ExpectedExitCodes", linux ? "0,1" : "0"),
                new XAttribute("Quiet", "false"), new XAttribute("Optional", "false"), new XAttribute("Arguments", linux ? "+x tap" : ""), new XAttribute("ActionName", "install"))));
    }

    private static byte[] Archive(XElement metadata)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var stream = zip.CreateEntry("package.xml").Open()) stream.Write(Encoding.UTF8.GetBytes(metadata.ToString()));
            foreach (var file in metadata.Elements(Namespace + "Files").Elements(Namespace + "File"))
            {
                var entry = zip.CreateEntry((string)file.Attribute("Path")!);
                entry.ExternalAttributes = (0x8000 | 0x1A4) << 16;
                using var stream = entry.Open(); stream.Write("unexecuted fixture payload"u8);
            }
        }
        return output.ToArray();
    }
}
