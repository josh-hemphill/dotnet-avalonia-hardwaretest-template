using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Tests;

public sealed class OfflineBenchStartupTests
{
    [Fact]
    public void Ordinary_operator_copies_keep_their_original_configuration_contract()
    {
        using var fixture = new StartupFixture(marked: false);
        string[] original = ["--data-directory", fixture.Root, "--settings", "external.json"];
        Assert.Same(original, OfflineBenchStartup.PrepareArguments(original, fixture.App, new Hashtable()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bound_copy_forces_its_fresh_data_folder_before_option_terminators_without_writing(bool terminator)
    {
        using var fixture = new StartupFixture();
        var args = OfflineBenchStartup.PrepareArguments(terminator ? ["--print-config", "--", "--data-directory", fixture.Data] : ["--print-config"], fixture.App, new Hashtable());
        var parsed = ConfigurationArgs.Parse(args);
        Assert.Equal(fixture.Data, ConfigurationBootstrap.ResolveStage1(parsed, new Hashtable()).RootDirectory);
        Assert.Equal(new[] { OfflineBenchStartup.DataOwnershipFileName }, Directory.GetFiles(fixture.Data).Select(Path.GetFileName).ToArray());
        Assert.False(Directory.Exists(Path.Combine(fixture.Data, "logs")));
        Assert.False(File.Exists(Path.Combine(fixture.Data, "settings.json")));
    }

    [Theory]
    [InlineData("--data-directory")]
    [InlineData("--DATA-DIRECTORY=")]
    [InlineData("--data-directory:")]
    [InlineData("--settings")]
    [InlineData("--settings=")]
    [InlineData("--SETTINGS:")]
    [InlineData("--opentap-plugin-dirs")]
    [InlineData("--OPENTAP-PLUGIN-DIRS=")]
    [InlineData("--opentap-plugin-dirs:")]
    public void External_configuration_cannot_redirect_an_installed_copy(string option)
    {
        using var fixture = new StartupFixture();
        var outside = Path.Combine(fixture.Root, "existing station"); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "preserve.txt"), "existing evidence");
        var args = option.EndsWith('=') || option.EndsWith(':') ? new[] { option + outside } : [option, outside];
        var before = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments(args, fixture.App, new Hashtable()));
        foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        Assert.Equal(before.Count, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Length);
    }

    [Theory]
    [InlineData("HARDWARETEST_DATA_DIRECTORY")]
    [InlineData("hardwaretest_data_directory")]
    [InlineData("HARDWARETEST_OPEN_TAP_PLUGIN_DIRECTORIES")]
    [InlineData("HARDWARETEST_OPEN_TAP_PLUGIN_DIRECTORIES__0")]
    public void Environment_overrides_cannot_reuse_data_or_external_packages(string key)
    {
        using var fixture = new StartupFixture();
        var environment = new Hashtable { [key] = Path.Combine(fixture.Root, "old station") };
        Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, environment));
    }

    [Fact]
    public void Matching_data_arguments_and_environment_preserve_other_launch_options()
    {
        using var fixture = new StartupFixture();
        string[] args = ["--DATA-DIRECTORY=" + fixture.Data, "--use-mock-visa", "false", "--log-level", "Warning"];
        var result = OfflineBenchStartup.PrepareArguments(args, fixture.App, new Hashtable { ["HARDWARETEST_DATA_DIRECTORY"] = fixture.Data });
        Assert.Contains("--use-mock-visa", result); Assert.Contains("Warning", result);
        Assert.Equal(fixture.Data, ConfigurationBootstrap.ResolveStage1(ConfigurationArgs.Parse(result), new Hashtable()).RootDirectory);
    }

    [Theory]
    [InlineData("installation")]
    [InlineData("data-owner")]
    [InlineData("lock")]
    [InlineData("missing-data-owner")]
    [InlineData("missing-data")]
    public void Missing_or_tampered_ownership_and_lock_fail_before_creating_any_recovery_folder(string target)
    {
        using var fixture = new StartupFixture();
        switch (target)
        {
            case "installation": File.AppendAllText(fixture.InstallationPath, " "); break;
            case "data-owner": File.WriteAllText(fixture.OwnerPath, "{}"); break;
            case "lock": File.AppendAllText(fixture.LockPath, "changed"); break;
            case "missing-data-owner": File.Delete(fixture.OwnerPath); break;
            case "missing-data": Directory.Delete(fixture.Data, true); break;
        }
        var before = Directory.GetFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var error = Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
        Assert.Contains("FRESH data", error.Message);
        Assert.Equal(before, Directory.GetFileSystemEntries(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Theory]
    [InlineData("build")]
    [InlineData("app")]
    [InlineData("capability")]
    [InlineData("id")]
    public void Even_consistent_marker_bytes_cannot_bind_the_wrong_build_app_or_schema_identity(string mismatch)
    {
        using var fixture = new StartupFixture();
        var marker = fixture.Installation;
        marker = mismatch switch
        {
            "build" => marker with { RequiredOperatorBuild = "old-build" },
            "app" => marker with { AppDirectory = Path.Combine(fixture.Root, "other app") },
            "capability" => marker with { Capability = "unknown" },
            _ => marker with { InstallId = "invalid-id" },
        };
        fixture.WriteMarkers(marker);
        Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
    }

    [Fact]
    public void Normal_saved_runs_do_not_revoke_the_bound_installations_data_ownership()
    {
        using var fixture = new StartupFixture(); Directory.CreateDirectory(Path.Combine(fixture.Data, "runs"));
        File.WriteAllText(Path.Combine(fixture.Data, "runs", "evidence.json"), "preserved run");
        var args = OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable());
        Assert.Contains(fixture.Data, args);
        Assert.Equal("preserved run", File.ReadAllText(Path.Combine(fixture.Data, "runs", "evidence.json")));
    }

    [Theory]
    [InlineData("HardwareTest.dll")]
    [InlineData("HardwareTest.OpenTap.Host.dll")]
    [InlineData("Programs/offline-startup-fixture/fixture.TapPlan")]
    public void In_place_runtime_or_program_changes_cannot_reuse_bound_qualification(string relative)
    {
        using var fixture = new StartupFixture();
        File.AppendAllText(Path.Combine(fixture.App, relative.Replace('/', Path.DirectorySeparatorChar)), "changed");
        Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
    }

    [Theory]
    [InlineData("extra.dll")]
    [InlineData("extra.EXE")]
    [InlineData("libextra.so")]
    [InlineData("libextra.so.2")]
    [InlineData("extra.dylib")]
    [InlineData("extra.deps.json")]
    [InlineData("extra.runtimeconfig.json")]
    [InlineData("HardwareTest.OpenTap.Worker")]
    public void Added_code_is_rejected_while_normal_application_caches_remain_allowed(string name)
    {
        using var fixture = new StartupFixture();
        File.WriteAllText(Path.Combine(fixture.App, "cache.txt"), "normal cache");
        Assert.Contains(fixture.Data, OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
        File.WriteAllText(Path.Combine(fixture.App, name), "new code");
        var error = Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
        Assert.Contains("Unverified code", error.Message);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("traversal")]
    [InlineData("null")]
    public void Invalid_immutable_inventory_cannot_be_approved_by_rehashing_the_ownership_marker(string kind)
    {
        using var fixture = new StartupFixture();
        var files = kind switch
        {
            "empty" => new List<OfflineBenchFile>(),
            "duplicate" => new List<OfflineBenchFile> { fixture.Installation.RuntimeFiles[0], fixture.Installation.RuntimeFiles[0] },
            "traversal" => [fixture.Installation.RuntimeFiles[0] with { Path = "../outside.dll" }],
            _ => new List<OfflineBenchFile> { null! },
        };
        fixture.WriteMarkers(fixture.Installation with { RuntimeFiles = files });
        Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("settings.json.bak")]
    public void Owned_settings_cannot_reintroduce_external_plugins_or_restore_them_from_backup(string file)
    {
        using var fixture = new StartupFixture();
        var path = Path.Combine(fixture.Data, file); var contents = "{\"openTapPluginDirectories\":[\"/old/station/plugins\"]}";
        File.WriteAllText(path, contents);
        var before = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var error = Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
        Assert.Contains("OpenTapPluginDirectories", error.Message); Assert.Equal(contents, File.ReadAllText(path));
        Assert.Equal(before, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Fact]
    public void Ordinary_owned_settings_with_empty_plugin_lists_remain_usable_without_rewriting_them()
    {
        using var fixture = new StartupFixture(); var path = Path.Combine(fixture.Data, "settings.json");
        const string contents = "{\"openTapPluginDirectories\":[],\"useMockVisa\":false}"; File.WriteAllText(path, contents);
        Assert.Contains(fixture.Data, OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
        Assert.Equal(contents, File.ReadAllText(path));
    }

    [Fact]
    public void Oversize_and_linked_owned_settings_fail_without_reading_an_external_settings_target()
    {
        using var fixture = new StartupFixture(); var path = Path.Combine(fixture.Data, "settings.json");
        using (var stream = File.Create(path)) stream.SetLength(4 * 1024 * 1024 + 1);
        Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
        File.Delete(path);
        if (OperatingSystem.IsWindows()) return;
        var outside = Path.Combine(fixture.Root, "old-settings.json"); File.WriteAllText(outside, "preserve old settings");
        File.CreateSymbolicLink(path, outside);
        Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
        Assert.Equal("preserve old settings", File.ReadAllText(outside));
    }

    [Fact]
    public void Linked_data_and_marker_paths_are_rejected()
    {
        if (OperatingSystem.IsWindows()) return; // Windows link creation requires a privilege outside this contract test.
        using var fixture = new StartupFixture();
        var alternate = Path.Combine(fixture.Root, "linked data"); Directory.Move(fixture.Data, alternate);
        Directory.CreateSymbolicLink(fixture.Data, alternate);
        try { Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable())); }
        finally { Directory.Delete(fixture.Data); Directory.Move(alternate, fixture.Data); }
        var owner = fixture.OwnerPath + ".original"; File.Move(fixture.OwnerPath, owner); File.CreateSymbolicLink(fixture.OwnerPath, owner);
        Assert.Throws<InvalidDataException>(() => OfflineBenchStartup.PrepareArguments([], fixture.App, new Hashtable()));
    }

    private sealed class StartupFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "offline-startup-" + Guid.NewGuid().ToString("N"));
        public string App => Path.Combine(Root, "new app");
        public string Data => Path.Combine(Root, "fresh data");
        public string InstallationPath => Path.Combine(App, OfflineBenchStartup.InstallationFileName);
        public string OwnerPath => Path.Combine(Data, OfflineBenchStartup.DataOwnershipFileName);
        public string LockPath => Path.Combine(App, OfflineBenchStartup.LockFileName);
        public OfflineBenchInstallation Installation { get; }
        public StartupFixture(bool marked = true)
        {
            Directory.CreateDirectory(App); Directory.CreateDirectory(Data);
            File.WriteAllText(LockPath, "immutable bundle lock");
            File.WriteAllText(Path.Combine(App, "HardwareTest.dll"), "synthetic operator bytes for ownership unit test");
            File.WriteAllText(Path.Combine(App, "HardwareTest.OpenTap.Host.dll"), "synthetic host bytes for ownership unit test");
            File.WriteAllText(Path.Combine(App, "offline-bench-capability.json"), JsonSerializer.Serialize(new OfflineBenchCapabilityReceipt
            {
                OperatorSha256 = Hash(File.ReadAllBytes(Path.Combine(App, "HardwareTest.dll"))),
                HostSha256 = Hash(File.ReadAllBytes(Path.Combine(App, "HardwareTest.OpenTap.Host.dll"))),
                RuntimeIdentifier = (OfflineBenchBundle.CurrentOS == "Windows" ? "win" : OfflineBenchBundle.CurrentOS.ToLowerInvariant()) + "-" + OfflineBenchBundle.CurrentArchitecture,
            }, OfflineBenchJsonContext.Default.OfflineBenchCapabilityReceipt));
            var program = Path.Combine(App, "Programs", "offline-startup-fixture"); Directory.CreateDirectory(program);
            File.WriteAllText(Path.Combine(program, "fixture.TapPlan"), "synthetic immutable program bytes");
            var inventory = Directory.GetFiles(App, "*", SearchOption.AllDirectories).Where(path => path != LockPath)
                .Select(path => new OfflineBenchFile
                {
                    Path = Path.GetRelativePath(App, path).Replace('\\', '/'),
                    Length = new FileInfo(path).Length,
                    Sha256 = Hash(File.ReadAllBytes(path)),
                }).ToList();
            Installation = new()
            {
                InstallId = Guid.NewGuid().ToString("N"),
                AppDirectory = App,
                FreshDataDirectory = Data,
                LockSha256 = Hash(File.ReadAllBytes(LockPath)),
                RequiredOperatorBuild = OfflineBenchBundle.CurrentOperatorBuild,
                PlanId = "offline-startup-fixture",
                ExportId = Guid.NewGuid().ToString("N"),
                RuntimeFiles = inventory,
            };
            if (marked) WriteMarkers(Installation);
        }
        public void WriteMarkers(OfflineBenchInstallation installation)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(installation, OfflineBenchJsonContext.Default.OfflineBenchInstallation);
            File.WriteAllBytes(InstallationPath, bytes);
            File.WriteAllText(OwnerPath, JsonSerializer.Serialize(new OfflineBenchDataOwnership
            { InstallId = installation.InstallId, AppDirectory = App, InstallationSha256 = Hash(bytes) }, OfflineBenchJsonContext.Default.OfflineBenchDataOwnership));
        }
        private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        public void Dispose() => Directory.Delete(Root, true);
    }
}
