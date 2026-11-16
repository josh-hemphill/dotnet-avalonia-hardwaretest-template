using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.OfflineBench;

[Collection("OpenTapSerial")]
public sealed class OfflineBenchIntegrityTests
{
    [Fact]
    public void Complete_checked_program_and_transitive_archives_validate_without_modifying_inputs()
    {
        using var fixture = new BundleFixture();
        var before = fixture.Fingerprint();
        var manifest = OfflineBenchBundle.Validate(fixture.Root, "Linux", "x64");
        Assert.Equal(fixture.Manifest.ExportId, manifest.ExportId);
        Assert.Contains(manifest.Packages, package => package.Name == "Extension dependency");
        Assert.Contains("Extension dependency", OfflineBenchBundle.Describe(manifest));
        Assert.Equal(before, fixture.Fingerprint());
        var plan = Path.Combine(fixture.Root, OfflineBenchBundle.ProgramDirectory(BundleFixture.PlanId), BundleFixture.PlanId + ".TapPlan");
        Assert.Equal(BundleFixture.PlanId, BenchProgramHandoff.ValidateProgram(plan)!.PlanId);
    }

    [Theory]
    [InlineData("packages/Extension.TapPackage")]
    [InlineData("Programs/integrity-plan/integrity-plan.TapPlan")]
    [InlineData("Programs/integrity-plan/integrity-plan.program.json")]
    [InlineData("Programs/integrity-plan/bench-program.json")]
    public void Changed_archives_or_checked_pair_bytes_fail_before_any_installation(string relative)
    {
        using var fixture = new BundleFixture();
        File.AppendAllText(Path.Combine(fixture.Root, relative), "changed");
        var retained = fixture.Fingerprint();
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Root));
        Assert.Equal(retained, fixture.Fingerprint());
    }

    [Fact]
    public void Missing_transitive_package_is_rejected_even_after_transport_hashes_are_refreshed()
    {
        using var fixture = new BundleFixture();
        var dependency = fixture.Manifest.Packages.Single(package => package.Name == "Extension dependency");
        fixture.Manifest.Packages.Remove(dependency); File.Delete(Path.Combine(fixture.Root, dependency.Path)); fixture.Save();
        Assert.Contains("Extension dependency", Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Root)).Message);
    }

    [Fact]
    public void Archive_metadata_target_and_operator_build_are_checked_independently()
    {
        using var fixture = new BundleFixture();
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Root, "Windows", "x64"));
        var index = fixture.Manifest.Packages.FindIndex(package => package.Name == "Extension");
        fixture.Manifest.Packages[index] = fixture.Manifest.Packages[index] with { Version = "9.0.0" }; fixture.Save();
        Assert.Contains("metadata", Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Root)).Message);
        fixture.Manifest = fixture.Manifest with { RequiredOperatorBuild = "older-unsupported-build" }; fixture.Save();
        Assert.Contains("operator build", Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Root)).Message);
    }

    [Theory]
    [InlineData("case")]
    [InlineData("traversal")]
    [InlineData("link")]
    [InlineData("action")]
    public void Unsafe_archive_structure_is_rejected_after_valid_outer_hash_recalculation(string kind)
    {
        using var fixture = new BundleFixture();
        fixture.RewriteExtension(zip =>
        {
            if (kind == "action")
            {
                var prior = zip.GetEntry("package.xml")!;
                XElement xml; using (var input = prior.Open()) xml = XElement.Load(input);
                prior.Delete(); xml.Add(new XElement("InstallAction", "must never run"));
                using var output = new StreamWriter(zip.CreateEntry("package.xml").Open()); output.Write(xml);
            }
            else
            {
                var name = kind == "case" ? "PACKAGES/Extension/extension.dll" : kind == "traversal" ? "../outside.dll" : "symbolic-link";
                var entry = zip.CreateEntry(name);
                if (kind == "link") entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                using var output = new StreamWriter(entry.Open()); output.Write("unsafe");
            }
        });
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Root));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.Root)!, "outside.dll")));
    }

    [Fact]
    public void Case_colliding_or_undeclared_transport_files_are_preserved_and_rejected()
    {
        using var fixture = new BundleFixture();
        var original = fixture.Manifest.Files[0];
        fixture.Manifest.Files.Add(original with { Path = original.Path.ToUpperInvariant() }); fixture.WriteManifest();
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Root));
        fixture.Manifest.Files.RemoveAt(fixture.Manifest.Files.Count - 1); fixture.WriteManifest();
        var extra = Path.Combine(fixture.Root, "keep-unrelated.txt"); File.WriteAllText(extra, "keep");
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Root)); Assert.Equal("keep", File.ReadAllText(extra));
    }

    [Fact]
    public void Canceled_or_existing_destination_installation_never_writes_or_overwrites_station_data()
    {
        using var fixture = new BundleFixture();
        using var paths = new TempDataDirectory();
        var source = Path.Combine(paths.Path, "source"); Directory.CreateDirectory(source);
        var destination = Path.Combine(paths.Path, "new-app"); var data = Path.Combine(paths.Path, "new-data");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => OfflineBenchInstaller.Install(fixture.Root, source, destination, data, canceled.Token));
        Assert.False(Directory.Exists(destination)); Assert.False(Directory.Exists(data));
        Directory.CreateDirectory(destination); var sentinel = Path.Combine(destination, "existing-settings.json"); File.WriteAllText(sentinel, "retain original settings");
        Assert.Throws<InvalidDataException>(() => OfflineBenchInstaller.Install(fixture.Root, source, destination, data));
        Assert.Equal("retain original settings", File.ReadAllText(sentinel)); Assert.False(Directory.Exists(data));
        Assert.DoesNotContain(Directory.EnumerateDirectories(paths.Path), path => Path.GetFileName(path).StartsWith(".offline-", StringComparison.Ordinal));
    }

    private sealed class BundleFixture : IDisposable
    {
        private readonly TempDataDirectory _directory = new();
        internal const string PlanId = "integrity-plan";
        internal string Root => _directory.Path;
        internal OfflineBenchBundleManifest Manifest { get; set; }

        internal BundleFixture()
        {
            var exportId = Guid.NewGuid().ToString("N");
            Manifest = new()
            {
                PlanId = PlanId,
                ExportId = exportId,
                TargetOS = "Linux",
                TargetArchitecture = "x64",
                CreatedAt = DateTimeOffset.UtcNow,
                RequiredOperatorBuild = OfflineBenchBundle.CurrentOperatorBuild,
                Requirements = [new() { Name = "Extension", VersionRange = "^1.0.0" }]
            };
            var program = Path.Combine(Root, OfflineBenchBundle.ProgramDirectory(PlanId)); Directory.CreateDirectory(program);
            SampleProgramFactory.SaveBeside(program);
            var plan = Path.Combine(program, PlanId + ".TapPlan"); File.Move(Path.Combine(program, SampleProgramFactory.EmbeddedName), plan);
            var sidecar = Path.Combine(program, PlanId + ".program.json");
            File.WriteAllText(sidecar, JsonSerializer.Serialize(new ProgramSidecar { DisplayName = "Integrity fixture", SelectionIncludesCleanup = true }, ProgramCatalogJsonContext.Default.ProgramSidecar));
            var receipt = new BenchProgramReceipt { PlanId = PlanId, ExportId = exportId, ExportedAt = DateTimeOffset.UtcNow, PlanSha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(plan)), SidecarSha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(sidecar)) };
            File.WriteAllText(Path.Combine(program, BenchProgramHandoff.ReceiptFileName), JsonSerializer.Serialize(receipt, BenchHandoffJsonContext.Default.BenchProgramReceipt));
            AddPackage("Extension", "1.0.0", [new() { Name = "Extension dependency", VersionRange = "^1.0.0" }]);
            AddPackage("Extension dependency", "1.0.0", []);
            // Synthetic package payloads support integrity/graph tests, not a runtime execution claim.
            foreach (var dependency in XDocument.Load(plan).Descendants().Where(element => element.Name.LocalName == "Package" && element.Parent?.Name.LocalName == "Package.Dependencies"))
            {
                var name = (string)dependency.Attribute("Name")!; var range = (string)dependency.Attribute("Version")!;
                var version = System.Text.RegularExpressions.Regex.Match(range, @"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.+-]+)?").Value;
                if (!Manifest.Packages.Any(package => package.Name == name)) AddPackage(name, string.IsNullOrEmpty(version) ? "1.0.0" : version, []);
                Manifest.Requirements.Add(new() { Name = name, VersionRange = range });
            }
            Save();
        }

        private void AddPackage(string name, string version, List<OfflineBenchRequirement> dependencies)
        {
            var relative = "packages/" + name + ".TapPackage"; var path = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = "Packages/" + name + "/" + (name == "Extension" ? "extension.dll" : "payload.dll");
            var xml = new XElement("Package", new XAttribute("Name", name), new XAttribute("Version", version), new XAttribute("OS", "Linux"), new XAttribute("Architecture", "x64"),
                new XElement("Dependencies", dependencies.Select(dependency => new XElement("PackageDependency", new XAttribute("Package", dependency.Name), new XAttribute("Version", dependency.VersionRange)))),
                new XElement("Files", new XElement("File", new XAttribute("Path", payload))));
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("package.xml").Open(), new UTF8Encoding(false))) writer.Write(xml);
                using (var writer = new StreamWriter(zip.CreateEntry(payload).Open(), new UTF8Encoding(false))) writer.Write("synthetic unit-test payload");
            }
            Manifest.Packages.Add(new() { Name = name, Version = version, OS = "Linux", Architecture = "x64", Path = relative, Dependencies = dependencies, Sha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(path)) });
        }

        internal void RewriteExtension(Action<ZipArchive> edit)
        {
            var index = Manifest.Packages.FindIndex(package => package.Name == "Extension"); var package = Manifest.Packages[index];
            using (var zip = ZipFile.Open(Path.Combine(Root, package.Path), ZipArchiveMode.Update)) edit(zip);
            Manifest.Packages[index] = package with { Sha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(Path.Combine(Root, package.Path))) }; Save();
        }

        internal void Save()
        {
            Manifest.Files.Clear();
            foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != OfflineBenchBundle.ManifestFileName).Order(StringComparer.Ordinal))
                Manifest.Files.Add(new() { Path = Path.GetRelativePath(Root, path).Replace('\\', '/'), Length = new FileInfo(path).Length, Sha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(path)) });
            WriteManifest();
        }
        internal void WriteManifest() => File.WriteAllText(Path.Combine(Root, OfflineBenchBundle.ManifestFileName), JsonSerializer.Serialize(Manifest, OfflineBenchJsonContext.Default.OfflineBenchBundleManifest));
        internal string Fingerprint() => string.Join('\n', Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(Root, path) + ":" + BenchProgramHandoff.Hash(File.ReadAllBytes(path))));
        public void Dispose() => _directory.Dispose();
    }
}
