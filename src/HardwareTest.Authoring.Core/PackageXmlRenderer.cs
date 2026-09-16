using System.Xml.Linq;

namespace HardwareTest.Authoring;

/// Renders a program pack package.xml from the authoring manifest and discovered files.
public static class PackageXmlRenderer
{
    public const string TemplatePackageName = "HardwareTest Template Program";
    public static readonly XNamespace PackageNs = "http://opentap.io/schemas/package";

    public static readonly string[] TemplatePackFiles =
    [
        "sample.TapPlan",
        "sample.program.json",
        "template.program.json",
        "program.schema.json",
    ];

    public static string Render(AuthoringWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var spec = workspace.Manifest.Package;
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                PackageNs + "Package",
                new XAttribute("Name", spec.Name),
                new XAttribute("Version", spec.Version),
                new XAttribute("OS", spec.Os),
                new XElement(PackageNs + "Description", workspace.Manifest.DisplayName),
                new XElement(PackageNs + "Owner", "HardwareTest"),
                new XElement(
                    PackageNs + "Dependencies",
                    workspace.Manifest.Dependencies.Select(d =>
                        new XElement(
                            PackageNs + "PackageDependency",
                            new XAttribute("Package", d.Package),
                            new XAttribute("Version", d.Version)))),
                new XElement(
                    PackageNs + "Files",
                    EnumeratePackFiles(workspace).Select(file =>
                        new XElement(PackageNs + "File", new XAttribute("Path", file))))));
        return doc.Declaration + Environment.NewLine + doc.ToString() + Environment.NewLine;
    }

    public static IReadOnlyList<string> EnumeratePackFiles(AuthoringWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.Equals(workspace.Manifest.Package.Name, TemplatePackageName, StringComparison.Ordinal))
        {
            return TemplatePackFiles;
        }

        var files = new List<string>();
        foreach (var plan in workspace.TapPlanPaths)
        {
            var name = Path.GetFileName(plan);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            files.Add(name);
            var sidecar = Path.GetFileNameWithoutExtension(name) + ".program.json";
            var sidecarPath = Path.Combine(Path.GetDirectoryName(plan) ?? string.Empty, sidecar);
            if (File.Exists(sidecarPath))
            {
                files.Add(sidecar);
            }
        }

        return files;
    }
}
