using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Assesses installed bytes and versions only; never loads a provider or probes vendor VISA.
public static class StandaloneVisaReadiness
{
    public static bool IsLibraryHome(OpenTapHome home) => OpenTapHomeBootstrapper.ListInstalledPackages(home)
        .Any(package => package.Name.Equals(StandaloneVisaPackage.PackageName, StringComparison.OrdinalIgnoreCase)
            || package.Name.Equals(PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase))
        || new[] { "InstrumentComponents.dll", "InstrumentComponents.OpenTap.dll", "InstrumentComponents.OpenTap.Visa.dll", StandaloneVisaPackage.WrapperFileName }
            .Any(file => File.Exists(Path.Combine(home.Root, file)))
        || HasPackageDirectoryLibraryPayload(home);

    private static bool HasPackageDirectoryLibraryPayload(OpenTapHome home)
    {
        var packages = Path.Combine(home.Root, "Packages");
        return Directory.Exists(packages) && Directory.EnumerateFiles(packages, "*", SearchOption.AllDirectories)
            .Any(file => Path.GetFileName(file) is "InstrumentComponents.dll" or "InstrumentComponents.OpenTap.dll");
    }

    public static AuthoringInstrumentAvailability Assess(OpenTapHome home)
    {
        if (!IsLibraryHome(home)) return new(false, "Standalone VISA is optional; ordinary mock plans do not need it.");
        if (SupportedDependencies(home) is { } reason) return new(false, reason);
        var manifest = new AuthoringManifest { Dependencies = [new() { Package = StandaloneVisaPackage.PackageName, Version = StandaloneVisaPackage.Version }] };
        var counterpart = AuthoringEnvironmentAssessment.Packages(manifest, home).Single();
        if (!counterpart.Satisfied || !AuthoringEnvironmentAssessment.RuntimeFileAvailable(home, StandaloneVisaPackage.WrapperFileName) || !OwnedPayloadMatches(home))
            return new(false, "Standalone VISA counterpart is missing, incompatible, or incomplete. Prepare the selected home before standalone/TUI execution.");
        return new(true, "Standalone VISA provider ready. Physical execution also requires a vendor VISA runtime; preparation does not probe or install it.");
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
        return BasePayloadValid(home) && AuthoringEnvironmentAssessment.Packages(manifest, home).All(package => package.Satisfied)
            ? null : "Standalone VISA requires complete selected base/OpenTAP payloads. Prepare or repair the selected home.";
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
