using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Tests;

public sealed class OfflineBenchBundleTests
{
    [Fact]
    public void Genuine_archive_closure_and_checked_pair_are_verified_without_execution()
    {
        using var fixture = new Fixture();
        var manifest = OfflineBenchBundle.Validate(fixture.Bundle, OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture);
        Assert.Equal("offline-fixture", manifest.PlanId);
        Assert.Equal(2, manifest.Packages.Count);
        Assert.Contains("Fixture dependency", OfflineBenchBundle.Describe(manifest));
        Assert.False(Directory.Exists(fixture.AppDestination));
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("sidecar")]
    [InlineData("receipt")]
    [InlineData("undeclared")]
    [InlineData("missing")]
    public void Corruption_missing_inventory_and_unchecked_programs_fail_closed(string target)
    {
        using var fixture = new Fixture();
        switch (target)
        {
            case "archive": File.AppendAllText(Path.Combine(fixture.Bundle, fixture.Manifest.Packages[0].Path), "changed"); break;
            case "sidecar": fixture.Write("Programs/offline-fixture/offline-fixture.program.json", "{\"referenceRules\":[]}"u8.ToArray()); fixture.Save(); break;
            case "receipt": fixture.Write("Programs/offline-fixture/bench-program.json", "{}"u8.ToArray()); fixture.Save(); break;
            case "undeclared": File.WriteAllText(Path.Combine(fixture.Bundle, "untracked.dll"), "unexpected"); break;
            case "missing": File.Delete(Path.Combine(fixture.Bundle, fixture.Manifest.Packages[1].Path)); break;
        }
        Assert.ThrowsAny<Exception>(() => OfflineBenchBundle.Validate(fixture.Bundle));
    }

    [Fact]
    public void Missing_transitive_dependencies_are_detected_from_archive_not_only_manifest()
    {
        using var fixture = new Fixture();
        var missing = fixture.Manifest.Packages[1];
        fixture.Manifest = fixture.Manifest with { Packages = [fixture.Manifest.Packages[0]], Files = fixture.Manifest.Files.Where(file => file.Path != missing.Path).ToList() };
        File.Delete(Path.Combine(fixture.Bundle, missing.Path)); fixture.Save();
        var error = Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Bundle));
        Assert.Contains("Fixture dependency", error.Message);
    }

    [Fact]
    public void Declared_constraint_and_actual_archive_identity_must_agree()
    {
        using var fixture = new Fixture();
        fixture.Manifest = fixture.Manifest with { Requirements = [new() { Name = "Fixture plugin", VersionRange = "^2.0.0" }] }; fixture.Save();
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Bundle));
        fixture.Manifest = fixture.Manifest with { Requirements = [new() { Name = "Fixture plugin", VersionRange = "^1.0.0" }], Packages = [fixture.Manifest.Packages[0] with { Version = "2.0.0" }, fixture.Manifest.Packages[1]] }; fixture.Save();
        Assert.Throws<InvalidDataException>(() => OfflineBenchBundle.Validate(fixture.Bundle));
    }

    [Theory]
    [InlineData("Action")]
    [InlineData("InstallAction")]
    [InlineData("ExternalDependencies")]
    [InlineData("Prerequisites")]
    [InlineData("RequiresAdministrator")]
    [InlineData("SystemDependencies")]
    public void Unsupported_actions_and_external_prerequisites_are_independently_refused(string element)
    {
        var bytes = Fixture.Archive("Unsafe", extra: new XElement(element, "do not run"));
        Assert.Throws<InvalidDataException>(() => OfflineBenchArchive.Read(bytes, "Linux", "x64", default));
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("a\\escape.dll")]
    [InlineData("CON.dll")]
    public void Unsafe_archive_paths_never_escape_owned_memory(string path)
    {
        var bytes = Fixture.Archive("Unsafe", payloadPath: path);
        Assert.Throws<InvalidDataException>(() => OfflineBenchArchive.Read(bytes, "Linux", "x64", default));
    }

    [Fact]
    public void Neutral_metadata_is_explicitly_supported_but_wrong_platform_is_not()
    {
        var bytes = Fixture.Archive("Portable");
        Assert.Equal("Portable", OfflineBenchArchive.Read(bytes, "Linux", "arm64", default).Name);
        bytes = Fixture.Archive("Windows only", os: "Windows", architecture: "x64");
        Assert.Throws<InvalidDataException>(() => OfflineBenchArchive.Read(bytes, "Linux", "x64", default));
    }

    [Fact]
    public void Shared_expansion_and_entry_budgets_apply_across_individually_small_archives()
    {
        var bytes = Fixture.Archive("Compressed", content: new byte[1024]);
        var expanded = 0L;
        using (var zip = new ZipArchive(new MemoryStream(bytes))) expanded = zip.Entries.Sum(entry => entry.Length);
        var shared = new OfflineBenchReadBudget(expanded * 2 - 1, 100);
        OfflineBenchArchive.Read(bytes, "Linux", "x64", default, shared);
        Assert.Throws<InvalidDataException>(() => OfflineBenchArchive.Read(bytes, "Linux", "x64", default, shared));
        var entries = new OfflineBenchReadBudget(1_000_000, 3);
        OfflineBenchArchive.Read(bytes, "Linux", "x64", default, entries);
        Assert.Throws<InvalidDataException>(() => OfflineBenchArchive.Read(bytes, "Linux", "x64", default, entries));
    }

    [Fact]
    public void Cancellation_stops_owned_stream_read_before_returning_payload()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => OfflineBenchReadBudget.Read(new MemoryStream(new byte[100]), 100, cancelled.Token));
    }

    [Fact]
    public void Isolated_installation_clones_release_inventory_and_binds_fresh_data_only()
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var result = OfflineBenchInstaller.Install(fixture.Bundle, fixture.Runtime, fixture.AppDestination, fixture.DataDestination);
        Assert.Equal(fixture.AppDestination, result.Directory);
        Assert.Equal("old station", File.ReadAllText(Path.Combine(fixture.Runtime, "settings.json")));
        Assert.False(File.Exists(Path.Combine(fixture.AppDestination, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(fixture.AppDestination, "runs")));
        Assert.True(File.Exists(Path.Combine(fixture.AppDestination, "Packages", "Fixture plugin", "Fixture plugin.dll")));
        Assert.Equal([OfflineBenchStartup.DataOwnershipFileName], Directory.GetFiles(fixture.DataDestination).Select(Path.GetFileName));
        var bound = OfflineBenchStartup.PrepareArguments([], fixture.AppDestination, new System.Collections.Hashtable());
        Assert.Contains(fixture.DataDestination, bound);
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".offline-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_application_or_data_is_never_reused(bool existingData)
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var existing = existingData ? fixture.DataDestination : fixture.AppDestination;
        Directory.CreateDirectory(existing); File.WriteAllText(Path.Combine(existing, "keep"), "evidence");
        Assert.Throws<InvalidDataException>(() => OfflineBenchInstaller.Install(fixture.Bundle, fixture.Runtime, fixture.AppDestination, fixture.DataDestination));
        Assert.Equal("evidence", File.ReadAllText(Path.Combine(existing, "keep")));
        Assert.False(Directory.Exists(existingData ? fixture.AppDestination : fixture.DataDestination));
    }

    [Fact]
    public void Conflicting_packages_leave_no_publication_or_owned_staging()
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var conflict = Fixture.Archive("Fixture dependency", payloadPath: "Packages/Fixture plugin/Fixture plugin.dll");
        // A package is never allowed to supply another identity's code, even before staging.
        var path = fixture.Manifest.Packages[1].Path; fixture.Write(path, conflict);
        fixture.Manifest = fixture.Manifest with { Packages = [fixture.Manifest.Packages[0], fixture.Manifest.Packages[1] with { Sha256 = BenchProgramHandoff.Hash(conflict) }] }; fixture.Save();
        Assert.Throws<InvalidDataException>(() => OfflineBenchInstaller.Install(fixture.Bundle, fixture.Runtime, fixture.AppDestination, fixture.DataDestination));
        Assert.False(Directory.Exists(fixture.AppDestination)); Assert.False(Directory.Exists(fixture.DataDestination));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".offline-*"));
    }

    [Fact]
    public void Conflicting_root_payloads_fail_read_only_preflight_without_staging()
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var packages = fixture.Manifest.Packages.Select(package =>
        {
            var bytes = Fixture.Archive(package.Name, dependencies: package.Dependencies,
                payloadPath: "SharedFixture.dll", content: Encoding.UTF8.GetBytes(package.Name));
            fixture.Write(package.Path, bytes);
            return package with { Sha256 = BenchProgramHandoff.Hash(bytes) };
        }).ToList();
        fixture.Manifest = fixture.Manifest with { Packages = packages }; fixture.Save();
        OfflineBenchBundle.Validate(fixture.Bundle);
        Assert.Throws<InvalidDataException>(() => OfflineBenchRuntime.CheckBundle(fixture.Bundle, fixture.Runtime));
        var error = Assert.Throws<InvalidDataException>(() => OfflineBenchInstaller.Install(
            fixture.Bundle, fixture.Runtime, fixture.AppDestination, fixture.DataDestination));
        Assert.Contains("conflicts", error.Message);
        Assert.False(Directory.Exists(fixture.AppDestination)); Assert.False(Directory.Exists(fixture.DataDestination));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".offline-*"));
        Assert.Equal("old station", File.ReadAllText(Path.Combine(fixture.Runtime, "settings.json")));
    }

    [Theory]
    [InlineData("Shared.dll", "shared.dll")]
    [InlineData("Shared.dll", "Shared.dll/child.dll")]
    [InlineData("assets/one.dll", "Assets/two.dll")]
    public void Merged_archive_payload_refuses_case_and_file_directory_conflicts(string first, string second)
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var index = 0;
        var packages = fixture.Manifest.Packages.Select(package =>
        {
            var bytes = Fixture.Archive(package.Name, dependencies: package.Dependencies,
                payloadPath: index++ == 0 ? first : second, content: "identical bytes"u8.ToArray());
            fixture.Write(package.Path, bytes);
            return package with { Sha256 = BenchProgramHandoff.Hash(bytes) };
        }).ToList();
        fixture.Manifest = fixture.Manifest with { Packages = packages }; fixture.Save();
        Assert.Throws<InvalidDataException>(() => OfflineBenchRuntime.CheckBundle(fixture.Bundle, fixture.Runtime));
        Assert.False(Directory.Exists(fixture.AppDestination)); Assert.False(Directory.Exists(fixture.DataDestination));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Concurrent_destination_owner_is_preserved_and_only_our_reservation_is_removed(bool ownsData, bool afterDataMove)
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var foreign = ownsData ? fixture.DataDestination : fixture.AppDestination;
        Assert.ThrowsAny<IOException>(() => OfflineBenchInstaller.Install(fixture.Bundle, fixture.Runtime,
            fixture.AppDestination, fixture.DataDestination, default, stage =>
            {
                if (stage != (afterDataMove ? OfflineBenchPublicationStage.AfterDataPublication : OfflineBenchPublicationStage.BeforeCommit)) return;
                Directory.CreateDirectory(foreign); File.WriteAllText(Path.Combine(foreign, "foreign-sentinel"), "concurrent actor");
            }));
        Assert.Equal("concurrent actor", File.ReadAllText(Path.Combine(foreign, "foreign-sentinel")));
        Assert.Equal(["foreign-sentinel"], Directory.GetFiles(foreign).Select(Path.GetFileName));
        Assert.False(Directory.Exists(ownsData ? fixture.AppDestination : fixture.DataDestination));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".offline-*"));
        Assert.Equal("old station", File.ReadAllText(Path.Combine(fixture.Runtime, "settings.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_before_commit_rolls_back_but_after_data_publication_finishes_bound_install(bool afterDataMove)
    {
        using var fixture = new Fixture(); fixture.CreateRuntime(); using var cancellation = new CancellationTokenSource();
        OfflineBenchInstallResult Install() => OfflineBenchInstaller.Install(fixture.Bundle, fixture.Runtime,
            fixture.AppDestination, fixture.DataDestination, cancellation.Token, stage =>
            {
                if (stage == (afterDataMove ? OfflineBenchPublicationStage.AfterDataPublication : OfflineBenchPublicationStage.BeforeCommit)) cancellation.Cancel();
            });
        if (afterDataMove)
        {
            var result = Install(); Assert.Equal(fixture.AppDestination, result.Directory);
            var args = OfflineBenchStartup.PrepareArguments([], fixture.AppDestination, new System.Collections.Hashtable());
            Assert.Contains(fixture.DataDestination, args);
            Assert.Single(Directory.GetFiles(fixture.DataDestination));
        }
        else
        {
            Assert.Throws<OperationCanceledException>(() => Install());
            Assert.False(Directory.Exists(fixture.AppDestination)); Assert.False(Directory.Exists(fixture.DataDestination));
        }
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".offline-*"));
    }

    [Theory]
    [InlineData("offline-bench-lock.json")]
    [InlineData("offline-bench-installation.json")]
    [InlineData(".offline-bench-data.json")]
    [InlineData("offline-bench-capability.json")]
    public void Archives_cannot_claim_installer_owned_paths(string path)
    {
        var archive = Fixture.Archive("Ownership conflict", payloadPath: path);
        Assert.Throws<InvalidDataException>(() => OfflineBenchArchive.Read(archive, "Linux", "x64", default));
    }

    [Theory]
    [InlineData("HardwareTest.OpenTap.Worker.dll")]
    [InlineData("HardwareTest.OpenTap.Worker.deps.json")]
    [InlineData("HardwareTest.OpenTap.Worker.runtimeconfig.json")]
    [InlineData("apphost")]
    public void Missing_worker_release_companions_fail_read_only_check(string name)
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        if (name == "apphost") name = OperatingSystem.IsWindows() ? "HardwareTest.OpenTap.Worker.exe" : "HardwareTest.OpenTap.Worker";
        File.Delete(Path.Combine(fixture.Runtime, name));
        Assert.Throws<InvalidDataException>(() => OfflineBenchRuntime.CheckBundle(fixture.Bundle, fixture.Runtime));
        Assert.False(Directory.Exists(fixture.AppDestination)); Assert.False(Directory.Exists(fixture.DataDestination));
    }

    [Fact]
    public void Cleanup_preserves_foreign_empty_directory_added_to_owned_data_reservation()
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        Assert.ThrowsAny<IOException>(() => OfflineBenchInstaller.Install(fixture.Bundle, fixture.Runtime,
            fixture.AppDestination, fixture.DataDestination, default, stage =>
            {
                if (stage != OfflineBenchPublicationStage.AfterDataPublication) return;
                Directory.CreateDirectory(Path.Combine(fixture.DataDestination, "foreign-empty-directory"));
                Directory.CreateDirectory(fixture.AppDestination);
                File.WriteAllText(Path.Combine(fixture.AppDestination, "foreign-sentinel"), "foreign");
            }));
        Assert.True(Directory.Exists(Path.Combine(fixture.DataDestination, "foreign-empty-directory")));
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(fixture.AppDestination, "foreign-sentinel")));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".offline-*"));
    }

    [Fact]
    public void Excluded_user_data_is_not_traversed_but_declared_release_asset_links_are_refused()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var external = Path.Combine(fixture.Root, "external-user-data"); Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "invalid:portable-name"), "station data");
        Directory.CreateSymbolicLink(Path.Combine(fixture.Runtime, "data"), external);
        OfflineBenchRuntime.CheckBundle(fixture.Bundle, fixture.Runtime);
        File.WriteAllText(Path.Combine(fixture.Runtime, "HardwareTest.deps.json"), "{\"targets\":{\"fixture\":{\"dependency/1\":{\"runtime\":{\"linked.dll\":{}}}}}}");
        var externalDll = Path.Combine(external, "asset.dll"); File.WriteAllText(externalDll, "external code");
        File.CreateSymbolicLink(Path.Combine(fixture.Runtime, "linked.dll"), externalDll);
        Assert.Throws<InvalidDataException>(() => OfflineBenchRuntime.CheckBundle(fixture.Bundle, fixture.Runtime));
        Assert.Equal("station data", File.ReadAllText(Path.Combine(external, "invalid:portable-name")));
    }

    [Fact]
    public void Owned_runtime_snapshot_must_match_its_own_capability_even_after_live_validation()
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var rid = OfflineBenchRuntime.RuntimeIdentifier(OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture);
        OfflineBenchRuntime.ValidateCapability(fixture.Runtime, rid);
        var captured = Directory.GetFiles(fixture.Runtime).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        captured["HardwareTest.dll"] = "changed after initial live verification"u8.ToArray();
        Assert.Throws<InvalidDataException>(() => OfflineBenchRuntime.ValidateCapturedCapability(captured, rid));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runtime_owned_adapter_archive_cannot_omit_release_payload(bool visa)
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        using var source = visa ? StandaloneVisaPackage.OpenArchive() : PublishedInstrumentComponents.OpenArchive();
        using var bytes = new MemoryStream(); source.CopyTo(bytes);
        var archive = OfflineBenchArchive.Read(bytes.ToArray(), OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture, default);
        var payload = archive.Payload.Where(item => item.Key.EndsWith("package.xml", StringComparison.OrdinalIgnoreCase))
            .Concat(archive.Payload.Where(item => !item.Key.EndsWith("package.xml", StringComparison.OrdinalIgnoreCase)).Take(1))
            .ToDictionary(item => item.Key, item => item.Value);
        Assert.True(payload.Count < archive.Payload.Count);
        var original = OfflineBenchBundle.Capture(fixture.Bundle, null, null, default);
        var partial = original with { Archives = new Dictionary<string, OfflineBenchArchiveData> { [archive.Name] = archive with { Payload = payload } } };
        Assert.Throws<InvalidDataException>(() => OfflineBenchRuntime.Capture(fixture.Runtime, partial, default));
    }

    [Fact]
    public void Identical_payload_with_conflicting_permissions_fails_shared_check_and_install()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var index = 0;
        var packages = fixture.Manifest.Packages.Select(package =>
        {
            var bytes = Fixture.Archive(package.Name, dependencies: package.Dependencies, payloadPath: "shared-helper.sh",
                content: "identical payload"u8.ToArray(), mode: (UnixFileMode)(index++ == 0 ? 0x1A4 : 0x1ED));
            fixture.Write(package.Path, bytes); return package with { Sha256 = BenchProgramHandoff.Hash(bytes) };
        }).ToList();
        fixture.Manifest = fixture.Manifest with { Packages = packages }; fixture.Save();
        var error = Assert.Throws<InvalidDataException>(() => OfflineBenchRuntime.CheckBundle(fixture.Bundle, fixture.Runtime));
        Assert.Contains("permissions", error.Message);
        Assert.Throws<InvalidDataException>(() => OfflineBenchInstaller.Install(fixture.Bundle, fixture.Runtime, fixture.AppDestination, fixture.DataDestination));
        Assert.False(Directory.Exists(fixture.AppDestination)); Assert.False(Directory.Exists(fixture.DataDestination));
    }

    [Fact]
    public void Managed_payload_coalesces_irrelevant_execute_bits_and_preserves_base_permissions()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var shared = Path.Combine(fixture.Runtime, "SharedManaged.dll"); var content = "identical managed payload"u8.ToArray();
        File.WriteAllBytes(shared, content); File.SetUnixFileMode(shared, (UnixFileMode)0x1C0);
        File.WriteAllText(Path.Combine(fixture.Runtime, "HardwareTest.deps.json"), "{\"targets\":{\"fixture\":{\"dependency/1\":{\"runtime\":{\"SharedManaged.dll\":{}}}}}}");
        var package = fixture.Manifest.Packages[0];
        var bytes = Fixture.Archive(package.Name, dependencies: package.Dependencies, payloadPath: "SharedManaged.dll", content: content, mode: (UnixFileMode)0x1A4);
        fixture.Write(package.Path, bytes);
        fixture.Manifest = fixture.Manifest with { Packages = [package with { Sha256 = BenchProgramHandoff.Hash(bytes) }, fixture.Manifest.Packages[1]] }; fixture.Save();
        OfflineBenchRuntime.CheckBundle(fixture.Bundle, fixture.Runtime);
        OfflineBenchInstaller.Install(fixture.Bundle, fixture.Runtime, fixture.AppDestination, fixture.DataDestination);
        Assert.Equal(content, File.ReadAllBytes(Path.Combine(fixture.AppDestination, "SharedManaged.dll")));
        Assert.Equal((UnixFileMode)0x1C0, File.GetUnixFileMode(Path.Combine(fixture.AppDestination, "SharedManaged.dll")) & (UnixFileMode)0x1FF);
    }

    [Fact]
    public void Changed_release_permissions_before_commit_fail_without_publication()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); fixture.CreateRuntime();
        var worker = Path.Combine(fixture.Runtime, "HardwareTest.OpenTap.Worker");
        var error = Assert.Throws<InvalidDataException>(() => OfflineBenchInstaller.Install(fixture.Bundle, fixture.Runtime,
            fixture.AppDestination, fixture.DataDestination, default, stage =>
            {
                if (stage == OfflineBenchPublicationStage.BeforeCommit && !OperatingSystem.IsWindows()) File.SetUnixFileMode(worker, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }));
        Assert.Contains("permissions", error.Message);
        Assert.False(Directory.Exists(fixture.AppDestination)); Assert.False(Directory.Exists(fixture.DataDestination));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".offline-*"));
    }

    [Fact]
    public void Capability_receipt_must_match_source_bytes_and_cli_errors_never_start_a_gui()
    {
        using var fixture = new Fixture(); fixture.CreateRuntime();
        File.AppendAllText(Path.Combine(fixture.Runtime, "HardwareTest.dll"), "tampered");
        Assert.Throws<InvalidDataException>(() => OfflineBenchRuntime.ValidateCapability(fixture.Runtime));
        using var output = new StringWriter();
        Assert.True(OfflineBenchCli.TryRun(["--install-offline-bundle", fixture.Bundle], fixture.Runtime, output, out var exit));
        Assert.Equal(1, exit); Assert.Contains("refused", output.ToString());
        Assert.False(Directory.Exists(fixture.AppDestination));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "offline-backend-" + Guid.NewGuid().ToString("N"));
        public string Bundle => Path.Combine(Root, "bundle");
        public string Runtime => Path.Combine(Root, "runtime");
        public string AppDestination => Path.Combine(Root, "fresh app");
        public string DataDestination => Path.Combine(Root, "fresh data");
        public OfflineBenchBundleManifest Manifest { get; set; }
        public Fixture()
        {
            Directory.CreateDirectory(Bundle);
            var export = Guid.NewGuid().ToString("N");
            var plan = "<TestPlan><Steps /></TestPlan>"u8.ToArray(); var sidecar = "{}"u8.ToArray();
            var receipt = new BenchProgramReceipt { PlanId = "offline-fixture", ExportId = export, ExportedAt = DateTimeOffset.UtcNow, PlanSha256 = BenchProgramHandoff.Hash(plan), SidecarSha256 = BenchProgramHandoff.Hash(sidecar) };
            Manifest = new()
            {
                PlanId = "offline-fixture",
                ExportId = export,
                CreatedAt = DateTimeOffset.UtcNow,
                TargetOS = OfflineBenchBundle.CurrentOS,
                TargetArchitecture = OfflineBenchBundle.CurrentArchitecture,
                RequiredOperatorBuild = OfflineBenchBundle.CurrentOperatorBuild,
                Requirements = [new() { Name = "Fixture plugin", VersionRange = "^1.0.0" }]
            };
            Write("Programs/offline-fixture/offline-fixture.TapPlan", plan); Write("Programs/offline-fixture/offline-fixture.program.json", sidecar);
            Write("Programs/offline-fixture/bench-program.json", JsonSerializer.SerializeToUtf8Bytes(receipt, BenchHandoffJsonContext.Default.BenchProgramReceipt));
            foreach (var name in new[] { "Fixture plugin", "Fixture dependency" })
            {
                var dependencies = name == "Fixture plugin" ? new List<OfflineBenchRequirement> { new() { Name = "Fixture dependency", VersionRange = "^1.0.0" } } : [];
                var bytes = Archive(name, dependencies: dependencies); var path = "Packages/" + name + ".TapPackage"; Write(path, bytes);
                Manifest.Packages.Add(new() { Name = name, Version = "1.0.0", Architecture = "AnyCPU", Path = path, Sha256 = BenchProgramHandoff.Hash(bytes), Dependencies = dependencies });
            }
            Save();
        }
        public void Write(string relative, byte[] bytes)
        {
            var path = Path.Combine(Bundle, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
            Manifest = Manifest with { Files = Manifest.Files.Where(file => file.Path != relative).Append(new OfflineBenchFile { Path = relative, Length = bytes.LongLength, Sha256 = BenchProgramHandoff.Hash(bytes) }).ToList() };
        }
        public void Save() => File.WriteAllBytes(Path.Combine(Bundle, OfflineBenchBundle.ManifestFileName), JsonSerializer.SerializeToUtf8Bytes(Manifest, OfflineBenchJsonContext.Default.OfflineBenchBundleManifest));
        public void CreateRuntime()
        {
            Directory.CreateDirectory(Runtime);
            File.WriteAllText(Path.Combine(Runtime, "HardwareTest.dll"), "synthetic operator bytes: backend copy tests, actual execution tested separately");
            File.WriteAllText(Path.Combine(Runtime, "HardwareTest.OpenTap.Host.dll"), "synthetic host bytes");
            var workerHost = Path.Combine(Runtime, OperatingSystem.IsWindows() ? "HardwareTest.OpenTap.Worker.exe" : "HardwareTest.OpenTap.Worker");
            File.WriteAllText(workerHost, "synthetic worker apphost");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(workerHost, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(Path.Combine(Runtime, "HardwareTest.OpenTap.Worker.dll"), "synthetic worker");
            File.WriteAllText(Path.Combine(Runtime, "HardwareTest.OpenTap.Worker.deps.json"), "{\"targets\":{}}");
            File.WriteAllText(Path.Combine(Runtime, "HardwareTest.OpenTap.Worker.runtimeconfig.json"), "{}");
            File.WriteAllText(Path.Combine(Runtime, "HardwareTest.deps.json"), "{\"targets\":{}}"); File.WriteAllText(Path.Combine(Runtime, "HardwareTest.runtimeconfig.json"), "{}");
            File.WriteAllText(Path.Combine(Runtime, "settings.json"), "old station"); Directory.CreateDirectory(Path.Combine(Runtime, "runs")); File.WriteAllText(Path.Combine(Runtime, "runs", "prior.json"), "prior evidence");
            var receipt = new OfflineBenchCapabilityReceipt { OperatorSha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(Path.Combine(Runtime, "HardwareTest.dll"))), HostSha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(Path.Combine(Runtime, "HardwareTest.OpenTap.Host.dll"))), RuntimeIdentifier = OfflineBenchRuntime.RuntimeIdentifier(OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture) };
            File.WriteAllBytes(Path.Combine(Runtime, OfflineBenchRuntime.CapabilityFileName), JsonSerializer.SerializeToUtf8Bytes(receipt, OfflineBenchJsonContext.Default.OfflineBenchCapabilityReceipt));
        }
        internal static byte[] Archive(string name, List<OfflineBenchRequirement>? dependencies = null, XElement? extra = null, string? payloadPath = null, byte[]? content = null, string os = "", string architecture = "AnyCPU", UnixFileMode? mode = null)
        {
            payloadPath ??= "Packages/" + name + "/" + name + ".dll"; content ??= Encoding.UTF8.GetBytes("fixture " + name);
            var xml = new XElement("Package", new XAttribute("Name", name), new XAttribute("Version", "1.0.0"), new XAttribute("OS", os), new XAttribute("Architecture", architecture),
                new XElement("Files", new XElement("File", new XAttribute("Path", payloadPath))),
                new XElement("Dependencies", (dependencies ?? []).Select(dep => new XElement("PackageDependency", new XAttribute("Package", dep.Name), new XAttribute("Version", dep.VersionRange)))), extra);
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var stream = zip.CreateEntry("package.xml").Open()) stream.Write(Encoding.UTF8.GetBytes(xml.ToString()));
                var payloadEntry = zip.CreateEntry(payloadPath);
                if (mode is not null) payloadEntry.ExternalAttributes = (0x8000 | (int)mode.Value) << 16;
                using (var stream = payloadEntry.Open()) stream.Write(content);
            }
            return output.ToArray();
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
