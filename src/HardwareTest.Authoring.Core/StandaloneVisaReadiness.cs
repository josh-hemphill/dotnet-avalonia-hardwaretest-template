using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Assesses installed bytes and versions only; never loads a provider or probes vendor VISA.
public static class StandaloneVisaReadiness
{
    public static bool RequiresStandaloneReadiness(OpenTapHome home)
    {
        var inspection = InspectClaims(home);
        return inspection.HasClaims || inspection.Issue is not null;
    }

    internal static string? ExecutionPrerequisite(OpenTapHome home, bool requiresInstrumentLibrary)
    {
        var inspection = InspectClaims(home);
        if (inspection.Issue is not null) return inspection.Issue;
        if (requiresInstrumentLibrary && !inspection.HasClaims)
            return "This workspace requires InstrumentComponents.OpenTap, but the selected home has no installed library. Prepare or import the library and standalone VISA counterpart before opening the TUI.";
        return inspection.HasClaims && AssessClaimedHome(home) is { Available: false } unavailable ? unavailable.Reason : null;
    }

    public static AuthoringInstrumentAvailability Assess(OpenTapHome home)
    {
        var inspection = InspectClaims(home);
        if (inspection.Issue is not null) return new(false, inspection.Issue);
        if (!inspection.HasClaims) return new(false, "Standalone VISA is optional; ordinary mock plans do not need it.");
        return AssessClaimedHome(home);
    }

    private static AuthoringInstrumentAvailability AssessClaimedHome(OpenTapHome home)
    {
        if (SupportedDependencies(home) is { } reason) return new(false, reason);
        var manifest = new AuthoringManifest { Dependencies = [new() { Package = StandaloneVisaPackage.PackageName, Version = StandaloneVisaPackage.Version }] };
        var counterpart = AuthoringEnvironmentAssessment.Packages(manifest, home).Single();
        if (!counterpart.Satisfied || !AuthoringEnvironmentAssessment.RuntimeFileAvailable(home, StandaloneVisaPackage.WrapperFileName) || !OwnedPayloadMatches(home))
            return new(false, "Standalone VISA counterpart is missing, incompatible, or incomplete. Prepare the selected home before standalone/TUI execution.");
        return new(true, "Standalone VISA provider ready. Physical execution also requires a vendor VISA runtime; preparation does not probe or install it.");
    }

    private static (bool HasClaims, string? Issue) InspectClaims(OpenTapHome home)
    {
        var claimed = false;
        try
        {
            if (File.Exists(home.Root) && !Directory.Exists(home.Root)) throw new IOException("The selected home is a file, not a directory.");
            ExecutionLibraryHome.Validate(home.Root, entry =>
            {
                var name = Path.GetFileName(entry);
                if (InstrumentLibraryMetadata.Files.Contains(name, StringComparer.OrdinalIgnoreCase)
                    || name.Equals("InstrumentComponents.OpenTap.Visa.dll", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("InstrumentComponents.Visa.dll", StringComparison.OrdinalIgnoreCase)
                    || name.Equals(StandaloneVisaPackage.WrapperFileName, StringComparison.OrdinalIgnoreCase)) claimed = true;
                if (!name.Equals("package.xml", StringComparison.OrdinalIgnoreCase)) return;
                using var reader = XmlReader.Create(entry, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 1_048_576
                });
                var package = ((string?)XDocument.Load(reader).Root?.Attribute("Name"))?.Trim();
                if (string.Equals(package, PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(package, StandaloneVisaPackage.PackageName, StringComparison.OrdinalIgnoreCase)) claimed = true;
            });
            return (claimed, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or XmlException or NotSupportedException)
        {
            return (claimed, $"Cannot inspect the selected standalone/TUI home: {error.Message} Prepare or repair a supported library home before opening the TUI.");
        }
    }

    private static bool OwnedPayloadMatches(OpenTapHome home)
    {
        try
        {
            using var stream = StandaloneVisaPackage.OpenArchive();
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (!AuthoringEnvironmentAssessment.RuntimeFileAvailable(home, entry.FullName)) return false;
                using var expected = entry.Open();
                using var actual = File.OpenRead(Path.Combine(home.Root, entry.FullName));
                if (!SHA256.HashData(expected).SequenceEqual(SHA256.HashData(actual))) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    internal static string? SupportedDependencies(OpenTapHome home)
    {
        if (AuthoringEnvironmentAssessment.UnsafeInstalledPaths(home).Count != 0) return "Standalone VISA cannot use an unsafe installed home.";
        var installed = OpenTapHomeBootstrapper.ListInstalledPackages(home);
        bool Exact(string name, string version)
        {
            var identities = installed.Where(package => package.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            var canonical = Path.GetFullPath(Path.Combine(home.Root, "Packages", name));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return identities.Length == 1 && identities[0].Path.Equals(canonical, comparison)
                && identities[0].Version.Split('+')[0] == version;
        }
        if (!Exact(PublishedInstrumentComponents.PackageName, StandaloneVisaPackage.BaseVersion)
            || !Exact("OpenTAP", StandaloneVisaPackage.OpenTapVersion))
            return "Standalone VISA counterpart requires InstrumentComponents.OpenTap 0.1.1 and OpenTAP 9.35.0. The selected dependency versions are unsupported; prepare or import the current packages.";
        var manifest = new AuthoringManifest { Dependencies = [new() { Package = PublishedInstrumentComponents.PackageName, Version = StandaloneVisaPackage.BaseVersion }, new() { Package = "OpenTAP", Version = StandaloneVisaPackage.OpenTapVersion }] };
        if (OpenTapRuntimeIssue(home) is { } runtimeIssue) return runtimeIssue;
        return BasePayloadValid(home) && AuthoringEnvironmentAssessment.Packages(manifest, home).All(package => package.Satisfied)
            ? null : "Standalone VISA requires complete selected base/OpenTAP payloads. Prepare or repair the selected home.";
    }

    // The standalone profile uses the pinned runtime shipped with these tools, not custom engine builds.
    // Compare actual managed identities and bytes to that source; NuGet versions are not CLR versions.
    internal static string? OpenTapRuntimeIssue(OpenTapHome home, bool allowMissing = false)
    {
        try
        {
            var source = OpenTapHomeBootstrapper.ResolveOpenTapRuntimeDirectory();
            var metadataPath = Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml");
            var mandatory = new[] { "OpenTap.dll", "OpenTap.Package.dll", "tap.dll", "tap.runtimeconfig.json" };
            foreach (var file in mandatory)
            {
                var actual = Path.Combine(home.Root, file);
                if (!File.Exists(actual) && allowMissing) continue;
                if (!AuthoringEnvironmentAssessment.RuntimeFileAvailable(home, file)) return $"OpenTAP runtime payload '{file}' is missing or unsafe.";
                var expected = Path.Combine(source, file);
                if (file.EndsWith(".dll", StringComparison.Ordinal))
                {
                    if (ManagedIdentity(actual) != ManagedIdentity(expected)) return $"OpenTAP runtime assembly '{file}' has an unsupported managed identity or version.";
                }
                else
                {
                    using var config = JsonDocument.Parse(File.ReadAllBytes(actual));
                    var framework = config.RootElement.GetProperty("runtimeOptions").GetProperty("framework");
                    // The genuine Windows engine declares Desktop; its host resolves
                    // the transitive Core framework. Exact RID-source bytes are checked below.
                    if (framework.GetProperty("name").GetString() is not ("Microsoft.NETCore.App" or "Microsoft.WindowsDesktop.App")
                        || !Version.TryParse(framework.GetProperty("version").GetString(), out var frameworkVersion)
                        || frameworkVersion.Major <= 0 || frameworkVersion.Build < 0 || frameworkVersion.Revision >= 0)
                        return "OpenTAP runtime configuration must declare Microsoft.NETCore.App or Microsoft.WindowsDesktop.App with a positive major.minor.patch version.";
                }
                if (!SHA256.HashData(File.ReadAllBytes(actual)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(expected))))
                    return $"OpenTAP runtime payload '{file}' differs from the supported pinned runtime.";
            }
            if (!File.Exists(metadataPath)) return allowMissing ? null : "OpenTAP runtime metadata is missing.";
            if (!AuthoringEnvironmentAssessment.RuntimeFileAvailable(home, "Packages/OpenTAP/package.xml")) return "OpenTAP runtime metadata is unsafe.";
            var package = XDocument.Load(metadataPath).Root;
            if (package?.Name.LocalName != "Package" || (string?)package.Attribute("Name") != "OpenTAP"
                || global::OpenTap.SemanticVersion.Parse((string?)package.Attribute("Version") ?? "").ToString().Split('+')[0] != StandaloneVisaPackage.OpenTapVersion)
                return "OpenTAP runtime package version is unsupported.";
            var declarations = package.Descendants().Where(element => element.Name.LocalName == "File").ToArray();
            if (!mandatory.All(file => declarations.Any(element => (string?)element.Attribute("Path") == file)))
                return "OpenTAP runtime metadata does not declare its complete mandatory payload.";
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var declaration in declarations)
            {
                var relative = ((string?)declaration.Attribute("Path"))?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')
                    || relative.Split('/').Any(part => part is "" or "." or "..") || !paths.Add(relative))
                    return "OpenTAP runtime metadata declares an invalid or duplicate path.";
                var path = Path.Combine(home.Root, relative.Replace('/', Path.DirectorySeparatorChar));
                var hashes = declaration.Descendants().Where(element => element.Name.LocalName == "Hash").ToArray();
                if (hashes.Length > 1 || hashes.Any(hash => hash.Parent != declaration || hash.HasElements
                    || hash.Value.Trim().Length != 40 || hash.Value.Trim().Any(character => !Uri.IsHexDigit(character))))
                    return $"OpenTAP runtime payload '{relative}' has malformed hash metadata.";
                if (!File.Exists(path) && allowMissing) continue;
                if (!AuthoringEnvironmentAssessment.RuntimeFileAvailable(home, relative)) return $"OpenTAP declared runtime payload '{relative}' is missing or unsafe.";
                if (hashes.Length == 1 && !Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(path))).Equals(hashes[0].Value.Trim(), StringComparison.OrdinalIgnoreCase))
                    return $"OpenTAP runtime payload '{relative}' does not match its declared hash.";
            }
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException
            or ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException or JsonException or System.Xml.XmlException)
        { return $"OpenTAP runtime cannot be validated: {error.Message}"; }

        static (string Name, Version Version) ManagedIdentity(string path)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) throw new BadImageFormatException("Runtime is not a managed assembly.");
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) throw new BadImageFormatException("Runtime has no assembly identity.");
            var assembly = metadata.GetAssemblyDefinition();
            return (metadata.GetString(assembly.Name), assembly.Version);
        }
    }

    private static bool BasePayloadValid(OpenTapHome home)
    {
        if (!AuthoringInstrumentCatalog.LibraryPayloadAvailability(home).Available) return false;
        try
        {
            foreach (var name in new[] { "InstrumentComponents", "InstrumentComponents.OpenTap" })
            {
                var identity = AssemblyName.GetAssemblyName(AuthoringAdapterPayloadInspection.LibraryPayloadPath(home, name + ".dll"));
                if (identity.Name != name || identity.Version != new Version(0, 1, 1, 0)) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException) { return false; }
    }

}
