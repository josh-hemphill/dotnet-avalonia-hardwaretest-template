using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace HardwareTest.OpenTap.Host;

/// A portable, self-contained compiled program and its verified TapPackage closure.
public static partial class OfflineBenchBundle
{
    public const string ManifestFileName = "offline-bench.json";
    public const string InstallerCapability = "offline-bench-v1";
    public static string ProgramDirectory(string planId) => "Programs/" + planId;
    public static string CurrentOperatorBuild => typeof(OfflineBenchBundle).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("This operator build has no stable release identity.");
    public static string CurrentOS => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "MacOS";
    public static string CurrentArchitecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    public static bool IsSupportedTarget(string os, string architecture)
        => os == "Windows" && architecture == "x64" || os == "Linux" && architecture is "x64" or "arm64";
    public static bool IsRuntimeProvidedPackage(string name)
        => new[] { "OpenTAP", "HardwareTest Basic", "HardwareTest Mixins", PublishedInstrumentComponents.PackageName, StandaloneVisaPackage.PackageName }
            .Contains(name, StringComparer.OrdinalIgnoreCase);

    public static UnixFileMode EffectiveExecutableMode(string path, UnixFileMode mode, string targetOS)
    {
        if (targetOS == "Windows") return 0;
        var extension = Path.GetExtension(path);
        if (new[] { ".dll", ".so", ".dylib", ".json", ".xml", ".config", ".txt", ".md", ".pdb", ".png", ".ico", ".jpg", ".jpeg", ".svg", ".html", ".css", ".wasm" }.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || Path.GetFileName(path).Contains(".so.", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals("LICENSE", StringComparison.OrdinalIgnoreCase)) return 0;
        if (mode == 0) mode = (UnixFileMode)0x1A4;
        return mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    public static UnixFileMode DeclarativePayloadMode(XElement package, string path, UnixFileMode mode, string targetOS)
    {
        ValidatePackageMetadata(package);
        var action = KnownBuiltinAction(package);
        if (action is null) return mode;
        var chmod = (string?)action.Elements().Single().Attribute("ExeFile") == "chmod";
        if (targetOS != (chmod ? "Linux" : "Windows")) throw new InvalidDataException("The built-in OpenTAP declaration does not match the selected target platform.");
        if (!chmod || path != "tap") return mode;
        if (mode == 0) mode = (UnixFileMode)0x1A4;
        return mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
    }

    public static void ValidatePackageMetadata(XElement package)
    {
        var allowedAction = KnownBuiltinAction(package);
        foreach (var element in package.DescendantsAndSelf())
        {
            if (allowedAction is not null && (element == allowedAction || element.Parent == allowedAction)) continue;
            static bool Unsupported(string name)
                => name.Contains("Action", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("ExternalDependenc", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Prerequisit", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SystemDependencies", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("RequiresAdministrator", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("RequireAdministrator", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Eula", StringComparison.OrdinalIgnoreCase);
            if (Unsupported(element.Name.LocalName) || element.Attributes().Any(attribute => Unsupported(attribute.Name.LocalName)))
                throw new InvalidDataException("Offline packages with actions, external prerequisites or administrator/OS installation requirements are unsupported. Resolve them through station deployment before exporting.");
        }
    }

    private static XElement? KnownBuiltinAction(XElement package)
    {
        var containers = package.Elements().Where(element => element.Name.LocalName == "PackageActionExtensions").ToArray();
        if (containers.Length == 0) return null;
        var ns = package.Name.Namespace;
        var container = containers[0];
        var steps = container.Elements().ToArray();
        var step = steps.Length == 1 ? steps[0] : null;
        var chmod = (string?)step?.Attribute("ExeFile") == "chmod";
        var payload = chmod ? "tap" : "Packages/OpenTAP/Tap.Upgrader.exe";
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ExeFile"] = chmod ? "chmod" : payload,
            ["ExpectedExitCodes"] = chmod ? "0,1" : "0",
            ["Quiet"] = "false",
            ["Optional"] = "false",
            ["Arguments"] = chmod ? "+x tap" : "",
            ["ActionName"] = "install",
        };
        var attributes = step?.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray() ?? [];
        var matches = containers.Length == 1 && package.Name == XName.Get("Package", "http://opentap.io/schemas/package")
            && (string?)package.Attribute("Name") == "OpenTAP"
            && ((string?)package.Attribute("Version") ?? "").Split('+')[0] == typeof(global::OpenTap.TestPlan).Assembly.GetName().Version!.ToString(3)
            && (string?)package.Attribute("OS") == (chmod ? "Linux" : "Windows")
            && container.Name == ns + "PackageActionExtensions" && !container.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration)
            && container.Nodes().All(node => node is XElement || node is XText text && string.IsNullOrWhiteSpace(text.Value))
            && step is not null && step.Name == ns + "ActionStep" && !step.HasElements && string.IsNullOrWhiteSpace(step.Value)
            && attributes.Length == expected.Count && attributes.All(attribute => attribute.Name.Namespace == XNamespace.None
                && expected.TryGetValue(attribute.Name.LocalName, out var value) && value == attribute.Value)
            && package.Elements(ns + "Files").Elements(ns + "File").Count(file => (string?)file.Attribute("Path") == payload) == 1;
        if (!matches) throw new InvalidDataException("Only the matching OpenTAP release's exact built-in Linux tap permission or Windows fresh-copy upgrader declaration is supported; commands are never executed.");
        return container;
    }
}
