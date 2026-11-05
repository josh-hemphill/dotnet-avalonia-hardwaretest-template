using System.Text;
using System.Text.Json;
using HardwareTest.Core.BenchReferences;
using OpenTap;
using OpenTap.Package;

namespace HardwareTest.OpenTap.Host;

internal sealed record OfflineBenchSnapshot(OfflineBenchBundleManifest Manifest, byte[] ManifestBytes, string LockSha256,
    IReadOnlyDictionary<string, byte[]> Files, IReadOnlyDictionary<string, OfflineBenchArchiveData> Archives);

public static partial class OfflineBenchBundle
{
    public static string SafeRelative(string path) => OfflineBenchPaths.Relative(path);

    public static OfflineBenchBundleManifest Validate(string root, string? targetOS = null,
        string? targetArchitecture = null, CancellationToken cancellationToken = default)
        => Capture(root, targetOS, targetArchitecture, cancellationToken).Manifest;

    public static string Describe(OfflineBenchBundleManifest manifest)
    {
        var text = new StringBuilder();
        text.AppendLine($"Program: {manifest.PlanId}; export {manifest.ExportId}");
        text.AppendLine($"Target: {manifest.TargetOS}/{manifest.TargetArchitecture}; created {manifest.CreatedAt:O}");
        text.AppendLine($"Required operator build: {manifest.RequiredOperatorBuild}; capability {manifest.InstallerCapability}");
        text.AppendLine($"Verified inventory: {manifest.Files.Count} files; {manifest.Packages.Count} exact package archives.");
        foreach (var requirement in manifest.Requirements)
            text.AppendLine($"Requirement: {requirement.Name} {requirement.VersionRange}");
        foreach (var package in manifest.Packages)
        {
            text.AppendLine($"Package: {package.Name} {package.Version} ({package.OS}/{package.Architecture}){(IsRuntimeProvidedPackage(package.Name) ? " — runtime baseline; never replaced" : string.Empty)}");
            text.AppendLine($"  Archive: {package.Path}; SHA-256 {package.Sha256}");
            foreach (var dependency in package.Dependencies)
                text.AppendLine($"  Requires {dependency.Name} {dependency.VersionRange}");
        }
        if (manifest.TargetOS == "Windows") text.AppendLine("The engine is verified against the matching release, not upgraded. Its known in-place Windows upgrader is never invoked for this fresh same-version copy.");
        text.AppendLine("Inspection verifies package bytes and dependency closure. It does not install packages, execute actions or certify hardware.");
        return text.ToString().TrimEnd();
    }

    internal static OfflineBenchSnapshot Capture(string root, string? targetOS, string? targetArchitecture, CancellationToken cancellationToken)
    {
        root = Path.GetFullPath(root);
        var tree = OfflineBenchPaths.Tree(root);
        if (!tree.TryGetValue(ManifestFileName, out var manifestPath)) throw new InvalidDataException("Select a complete offline bench bundle containing offline-bench.json.");
        var manifestBytes = ReadOwned(manifestPath, 4 * 1024 * 1024, cancellationToken);
        RequireUniqueJson(manifestBytes);
        var manifest = JsonSerializer.Deserialize(manifestBytes, OfflineBenchJsonContext.Default.OfflineBenchBundleManifest)
            ?? throw new InvalidDataException("Offline bench manifest is empty.");
        if (manifest.SchemaVersion != 1 || manifest.InstallerCapability != InstallerCapability)
            throw new InvalidDataException("Unsupported offline bundle schema or installer capability. Use the matching operator release.");
        if (manifest.RequiredOperatorBuild != CurrentOperatorBuild)
            throw new InvalidDataException($"Offline bundle requires operator build '{manifest.RequiredOperatorBuild}'. This build is '{CurrentOperatorBuild}'. Use the matching operator release.");
        if (!IsSupportedTarget(manifest.TargetOS, manifest.TargetArchitecture))
            throw new InvalidDataException("Offline bundles currently support win-x64, linux-x64 and linux-arm64 only.");
        if (targetOS is not null && !manifest.TargetOS.Equals(targetOS, StringComparison.OrdinalIgnoreCase)
            || targetArchitecture is not null && !manifest.TargetArchitecture.Equals(targetArchitecture, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Bundle target {manifest.TargetOS}/{manifest.TargetArchitecture} does not match this operator. Inspect it on its intended target.");
        BenchProgramHandoff.RequireCustomPlanId(manifest.PlanId);
        if (!Guid.TryParseExact(manifest.ExportId, "N", out _) || manifest.CreatedAt == default
            || manifest.Files is null || manifest.Packages is null || manifest.Requirements is null
            || manifest.Files.Any(file => file is null) || manifest.Packages.Any(package => package is null)
            || manifest.Requirements.Any(requirement => requirement is null)
            || manifest.Packages.Count == 0 || manifest.Requirements.Count == 0)
            throw new InvalidDataException("Offline bundle identity, inventory and required package closure must be complete.");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = OfflineBenchPaths.Relative(file.Path);
            if (path.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase) || !files.TryAdd(path, []))
                throw new InvalidDataException("Offline bundle inventory contains duplicate or case-colliding files.");
            if (!OfflineBenchPaths.IsHash(file.Sha256) || file.Length < 0 || file.Length > OfflineBenchArchive.MaximumFileBytes
                || (total += file.Length) > OfflineBenchArchive.MaximumFileBytes * 2 || !tree.TryGetValue(path, out var source))
                throw new InvalidDataException($"Offline bundle file is missing or unsupported: {path}");
            var bytes = ReadOwned(source, OfflineBenchArchive.MaximumFileBytes, cancellationToken);
            if (bytes.LongLength != file.Length || !OfflineBenchPaths.HashBytes(bytes).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Offline bundle file changed or its hash is invalid: {path}");
            files[path] = bytes;
        }
        if (tree.Keys.Any(path => !path.Equals(ManifestFileName, StringComparison.Ordinal) && !files.ContainsKey(path)))
            throw new InvalidDataException("Offline bundle contains undeclared files. Re-export the complete owned bundle.");
        ValidateProgramPair(manifest, files);
        var archives = new Dictionary<string, OfflineBenchArchiveData>(StringComparer.OrdinalIgnoreCase);
        var expandedBudget = new OfflineBenchReadBudget();
        foreach (var package in manifest.Packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (package.Dependencies is null || package.Dependencies.Any(dependency => dependency is null || string.IsNullOrWhiteSpace(dependency.Name) || string.IsNullOrWhiteSpace(dependency.VersionRange)))
                throw new InvalidDataException("Offline package dependencies are incomplete.");
            var name = OfflineBenchArchive.Name(package.Name);
            var path = OfflineBenchPaths.Relative(package.Path);
            if (!files.TryGetValue(path, out var bytes) || !path.EndsWith(".TapPackage", StringComparison.OrdinalIgnoreCase)
                || !OfflineBenchPaths.HashBytes(bytes).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Required offline package archive is missing or changed: {name}");
            var archive = OfflineBenchArchive.Read(bytes, manifest.TargetOS, manifest.TargetArchitecture, cancellationToken, expandedBudget);
            if (!name.Equals(archive.Name, StringComparison.Ordinal) || SemanticVersion.Parse(package.Version).ToString() != archive.Version
                || !SamePlatform(package.OS, archive.OS, false) || !SamePlatform(package.Architecture, archive.Architecture, true)
                || !SameDependencies(package.Dependencies, archive.Dependencies) || !archives.TryAdd(name, archive))
                throw new InvalidDataException($"Offline package metadata disagrees with its actual archive or is duplicated: {name}");
        }
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Require(OfflineBenchRequirement requirement)
        {
            var name = OfflineBenchArchive.Name(requirement.Name);
            if (string.IsNullOrWhiteSpace(requirement.VersionRange) || !archives.TryGetValue(name, out var package))
                throw new InvalidDataException($"Required package '{name}' must have an explicit constraint and an exact bundled archive.");
            if (!OfflineBenchVersions.IsCompatible(requirement.VersionRange, package.Version))
                throw new InvalidDataException($"Required package '{name} {requirement.VersionRange}' is not satisfied by the bundled exact archives.");
            if (!reached.Add(name)) return;
            foreach (var dependency in package.Dependencies) Require(dependency);
        }
        foreach (var requirement in manifest.Requirements) Require(requirement);
        var plan = OfflineBenchArchive.ReadXml(files[$"{ProgramDirectory(manifest.PlanId)}/{manifest.PlanId}.TapPlan"]);
        foreach (var dependency in plan.Descendants().Where(element => element.Parent?.Name.LocalName == "Package.Dependencies" && element.Name.LocalName == "Package"))
            Require(new() { Name = (string?)dependency.Attribute("Name") ?? string.Empty, VersionRange = (string?)dependency.Attribute("Version") ?? string.Empty });
        var allowed = new HashSet<string>(manifest.Packages.Select(package => package.Path), StringComparer.Ordinal);
        var program = ProgramDirectory(manifest.PlanId) + "/";
        foreach (var name in new[] { manifest.PlanId + ".TapPlan", manifest.PlanId + ".program.json", BenchProgramHandoff.ReceiptFileName }) allowed.Add(program + name);
        foreach (var helper in new[] { "install-offline-bundle.sh", "check-offline-bundle.sh", "install-offline-bundle.cmd", "check-offline-bundle.cmd", "offline-bench-helper.ps1", "README.txt" }) allowed.Add(helper);
        if (files.Keys.Any(path => !allowed.Contains(path))) throw new InvalidDataException("Offline bundle contains unsupported files outside its checked program, package closure and installation helpers.");
        if (reached.Count != archives.Count) throw new InvalidDataException("Offline bundle contains unrelated package archives outside its required closure.");
        cancellationToken.ThrowIfCancellationRequested();
        return new(manifest, manifestBytes, OfflineBenchPaths.HashBytes(manifestBytes), files, archives);
    }

    private static void ValidateProgramPair(OfflineBenchBundleManifest manifest, IReadOnlyDictionary<string, byte[]> files)
    {
        var directory = ProgramDirectory(manifest.PlanId);
        byte[] Read(string name) => files.TryGetValue(directory + "/" + name, out var bytes) ? bytes
            : throw new InvalidDataException("Offline bundle is missing the checked compiled program pair and receipt.");
        var planBytes = Read(manifest.PlanId + ".TapPlan");
        var sidecarBytes = Read(manifest.PlanId + ".program.json");
        var receipt = JsonSerializer.Deserialize(Read(BenchProgramHandoff.ReceiptFileName), BenchHandoffJsonContext.Default.BenchProgramReceipt)
            ?? throw new InvalidDataException("Offline program receipt is empty.");
        if (receipt.SchemaVersion != 1 || receipt.PlanId != manifest.PlanId || receipt.ExportId != manifest.ExportId
            || receipt.PlanSha256 != OfflineBenchPaths.HashBytes(planBytes) || receipt.SidecarSha256 != OfflineBenchPaths.HashBytes(sidecarBytes))
            throw new InvalidDataException("Offline program pair does not match its prepared bench receipt.");
        RequireUniqueJson(sidecarBytes);
        ProgramReferenceDeclarations.RequireSupportedJson(Encoding.UTF8.GetString(sidecarBytes));
        var sidecar = JsonSerializer.Deserialize(sidecarBytes, ProgramCatalogJsonContext.Default.ProgramSidecar)
            ?? throw new InvalidDataException("Offline program sidecar is empty.");
        var rules = sidecar.ReferenceRules ?? [];
        var errors = BenchReferenceEvaluator.ValidateRules(rules);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        var plan = OfflineBenchArchive.ReadXml(planBytes);
        var guards = plan.Descendants().Where(element => element.Name.LocalName == "TestStep"
            && ((string?)element.Attribute("type") ?? string.Empty).Replace("emb:", string.Empty, StringComparison.Ordinal)
                .Split(',')[0].Trim() == typeof(HardwareTest.OpenTap.Plugins.Basic.RequireOperatorReferenceAcceptanceStep).FullName).ToArray();
        if (rules.Count > 0 && (receipt.RequiredOperatorCapability != "bench-references-v1" || guards.Length != 1)
            || rules.Count == 0 && (receipt.RequiredOperatorCapability != string.Empty || guards.Length != 0))
            throw new InvalidDataException("Offline reference criteria and runtime compatibility guard do not match. Compile and prepare the complete program again.");
        foreach (var rule in rules)
            if (!plan.Descendants().Any(element => element.Name.LocalName == "TestStep"
                && Guid.TryParse((string?)element.Attribute("Id"), out var id) && id == rule.ProducerStepId))
                throw new InvalidDataException("Offline reference criterion has no matching producer in the compiled program.");
    }

    private static bool SameDependencies(IEnumerable<OfflineBenchRequirement> left, IEnumerable<OfflineBenchRequirement> right)
        => left.Select(value => value.Name.ToLowerInvariant() + "\0" + value.VersionRange.Trim()).Order(StringComparer.Ordinal)
            .SequenceEqual(right.Select(value => value.Name.ToLowerInvariant() + "\0" + value.VersionRange.Trim()).Order(StringComparer.Ordinal));
    private static bool SamePlatform(string left, string right, bool architecture)
    {
        string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? architecture ? "anycpu" : string.Empty
            : string.Join(',', value.Split(',', StringSplitOptions.TrimEntries).Select(part => part.ToLowerInvariant()).Order(StringComparer.Ordinal));
        return Normalize(left) == Normalize(right);
    }
    private static byte[] ReadOwned(string path, long maximum, CancellationToken cancellationToken)
    {
        OfflineBenchPaths.RejectLinks(path);
        if (new FileInfo(path).Length > maximum) throw new InvalidDataException("Offline payload exceeds supported size.");
        using var stream = File.OpenRead(path);
        var bytes = OfflineBenchReadBudget.Read(stream, maximum, cancellationToken);
        OfflineBenchPaths.RejectLinks(path);
        return bytes;
    }
    private static void RequireUniqueJson(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        void Check(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidDataException("Offline JSON contains duplicate or case-colliding properties.");
                    Check(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Check(child);
        }
        Check(document.RootElement);
    }
}
