using System.Text.Json.Serialization;

namespace HardwareTest.OpenTap.Host;

public sealed record OfflineBenchRequirement
{
    public string Name { get; init; } = string.Empty;
    public string VersionRange { get; init; } = string.Empty;
}

public sealed record OfflineBenchPackage
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string OS { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public List<OfflineBenchRequirement> Dependencies { get; init; } = [];
}

public sealed record OfflineBenchFile
{
    public string Path { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Length { get; init; }
}

public sealed record OfflineBenchBundleManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string PlanId { get; init; } = string.Empty;
    public string ExportId { get; init; } = string.Empty;
    public string TargetOS { get; init; } = string.Empty;
    public string TargetArchitecture { get; init; } = string.Empty;
    public string RequiredOperatorBuild { get; init; } = string.Empty;
    public string InstallerCapability { get; init; } = OfflineBenchBundle.InstallerCapability;
    public DateTimeOffset CreatedAt { get; init; }
    public List<OfflineBenchRequirement> Requirements { get; init; } = [];
    public List<OfflineBenchPackage> Packages { get; init; } = [];
    public List<OfflineBenchFile> Files { get; init; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OfflineBenchBundleManifest))]
[JsonSerializable(typeof(OfflineBenchCapabilityReceipt))]
[JsonSerializable(typeof(OfflineBenchInstallation))]
[JsonSerializable(typeof(OfflineBenchDataOwnership))]
public partial class OfflineBenchJsonContext : JsonSerializerContext;

public sealed record OfflineBenchInstallResult(string Directory, OfflineBenchBundleManifest Manifest, string Summary);

public sealed record OfflineBenchCapabilityReceipt
{
    public int SchemaVersion { get; init; } = 1;
    public string Capability { get; init; } = OfflineBenchBundle.InstallerCapability;
    public string OperatorSha256 { get; init; } = string.Empty;
    public string HostSha256 { get; init; } = string.Empty;
    public string RuntimeIdentifier { get; init; } = string.Empty;
}

public sealed record OfflineBenchInstallation
{
    public int SchemaVersion { get; init; } = 1;
    public string Capability { get; init; } = OfflineBenchBundle.InstallerCapability;
    public string InstallId { get; init; } = string.Empty;
    public string AppDirectory { get; init; } = string.Empty;
    public string FreshDataDirectory { get; init; } = string.Empty;
    public string LockSha256 { get; init; } = string.Empty;
    public string RequiredOperatorBuild { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string ExportId { get; init; } = string.Empty;
    public List<OfflineBenchFile> RuntimeFiles { get; init; } = [];
}

public sealed record OfflineBenchDataOwnership
{
    public int SchemaVersion { get; init; } = 1;
    public string InstallId { get; init; } = string.Empty;
    public string AppDirectory { get; init; } = string.Empty;
    public string InstallationSha256 { get; init; } = string.Empty;
}
