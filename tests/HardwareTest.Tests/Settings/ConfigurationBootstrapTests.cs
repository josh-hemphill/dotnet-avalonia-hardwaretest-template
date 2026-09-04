using System.Collections;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Settings;

public sealed class ConfigurationBootstrapTests
{
    [Fact]
    public async Task Precedence_file_beaten_by_env_beaten_by_command_line()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        store.AppSettings.LogMinimumLevel = "Warning";
        store.AppSettings.UseMockVisa = false;
        store.AppSettings.PlotRefreshHz = 11;
        store.AppSettings.OpenTapPluginDirectories = ["from-file"];
        await store.SaveAppSettingsAsync();

        var env = new Hashtable
        {
            ["HARDWARETEST_LOG_MINIMUM_LEVEL"] = "Debug",
            ["HARDWARETEST_USE_MOCK_VISA"] = "true",
            ["HARDWARETEST_PLOT_REFRESH_HZ"] = "22",
            ["HARDWARETEST_OPENTAP_PLUGIN_DIRS"] = "from-env",
        };
        var args = ConfigurationArgs.Parse(
        [
            "--log-level", "Error",
            "--mock-visa=false",
            "--plot-refresh-hz", "33",
            "--opentap-plugin-dirs", "from-cli",
        ]);

        var result = await ConfigurationBootstrap.ResolveAsync(args, env, defaultRoot: temp.Path);
        Assert.Equal("Error", result.Store.AppSettings.LogMinimumLevel);
        Assert.False(result.Store.AppSettings.UseMockVisa);
        Assert.Equal(33, result.Store.AppSettings.PlotRefreshHz);
        Assert.Equal(["from-cli"], result.Store.AppSettings.OpenTapPluginDirectories);
        Assert.Equal(SettingSource.CommandLine, result.Store.Provenance.Single(p => p.Key == "LogMinimumLevel").Source);
        Assert.Equal(SettingSource.CommandLine, result.Store.Provenance.Single(p => p.Key == "UseMockVisa").Source);
        Assert.Equal(SettingSource.CommandLine, result.Store.Provenance.Single(p => p.Key == "PlotRefreshHz").Source);
        Assert.Equal(SettingSource.CommandLine, result.Store.Provenance.Single(p => p.Key == "OpenTapPluginDirectories").Source);
    }

    [Fact]
    public async Task Malformed_env_keeps_prior_value_and_warns()
    {
        using var temp = new TempDataDirectory();
        var warnings = new List<string>();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["UseMockVisa"] = "not-a-bool",
                ["PlotRefreshHz"] = "NaN",
            },
            commandLineOverlays: null,
            warn: warnings.Add);

        Assert.True(store.AppSettings.UseMockVisa);
        Assert.Equal(20, store.AppSettings.PlotRefreshHz);
        Assert.Contains(warnings, w => w.Contains("UseMockVisa", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, w => w.Contains("PlotRefreshHz", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_settings_json_yields_defaults_without_throw()
    {
        using var temp = new TempDataDirectory();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        Assert.False(File.Exists(settingsPath));

        var store = new SettingsStore(temp.Path);
        await store.LoadAsync();

        Assert.False(File.Exists(settingsPath));
        Assert.True(store.AppSettings.UseMockVisa);
        Assert.All(
            store.Provenance.Where(p => AppSettingsEnvironmentBinder.Bindings.Any(b => b.Key == p.Key)),
            p => Assert.Equal(SettingSource.Default, p.Source));
    }

    [Fact]
    public async Task Read_only_settings_json_degrades_without_crash()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.SaveAppSettingsAsync();
        Assert.True(store.IsSettingsWritable);

        var path = store.SettingsPath;
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            store.AppSettings.ThemePreference = "Dark";
            await store.SaveAppSettingsAsync();
            Assert.False(store.IsSettingsWritable);
            Assert.False(string.IsNullOrWhiteSpace(store.LastPersistenceError));
            Assert.Equal("Dark", store.AppSettings.ThemePreference);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Print_config_contains_every_AppSettings_binder_key()
    {
        using var temp = new TempDataDirectory();
        var args = ConfigurationArgs.Parse(["--print-config"]);
        var result = await ConfigurationBootstrap.ResolveAsync(args, environment: null, defaultRoot: temp.Path);
        var text = ConfigurationBootstrap.FormatPrintConfig(result.Store);

        foreach (var binding in AppSettingsEnvironmentBinder.Bindings)
        {
            Assert.Contains(binding.Key, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Stage1_log_level_comes_from_env_before_file()
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "settings.json"),
            """{"logMinimumLevel":"Warning","useMockVisa":true,"dataDirectory":""}""");

        var env = new Hashtable { ["HARDWARETEST_LOG_MINIMUM_LEVEL"] = "Debug" };
        var args = ConfigurationArgs.Parse([]);
        var stage1 = ConfigurationBootstrap.ResolveStage1(args, env, defaultRoot: temp.Path);

        Assert.Equal("Debug", stage1.LogMinimumLevel);
        Assert.Equal(temp.Path, stage1.RootDirectory);
    }

    [Fact]
    public void Parse_version_flag_sets_print_version()
    {
        var longForm = ConfigurationArgs.Parse(["--version", "--log-level", "Debug"]);
        Assert.True(longForm.PrintVersion);
        Assert.Equal("Debug", longForm.Overlays["LogMinimumLevel"]);

        var shortForm = ConfigurationArgs.Parse(["-v"]);
        Assert.True(shortForm.PrintVersion);
    }

    [Fact]
    public void Parse_validate_plan_flag_sets_path_and_is_not_passthrough()
    {
        var spaced = ConfigurationArgs.Parse(["--validate-plan", "plans/opentap/sample.TapPlan", "--log-level", "Debug"]);
        Assert.True(spaced.ValidatePlan);
        Assert.Equal("plans/opentap/sample.TapPlan", spaced.ValidatePlanPath);
        Assert.Empty(spaced.PassthroughArgs);
        Assert.Equal("Debug", spaced.Overlays["LogMinimumLevel"]);

        var inline = ConfigurationArgs.Parse(["--validate-plan=fixtures/no-safe-shutdown.TapPlan"]);
        Assert.True(inline.ValidatePlan);
        Assert.Equal("fixtures/no-safe-shutdown.TapPlan", inline.ValidatePlanPath);
        Assert.Empty(inline.PassthroughArgs);
    }

    [Fact]
    public void Parse_bare_validate_plan_sets_flag_without_path()
    {
        var bare = ConfigurationArgs.Parse(["--validate-plan"]);
        Assert.True(bare.ValidatePlan);
        Assert.Null(bare.ValidatePlanPath);
        Assert.Empty(bare.PassthroughArgs);

        var empty = ConfigurationArgs.Parse(["--validate-plan="]);
        Assert.True(empty.ValidatePlan);
        Assert.Equal(string.Empty, empty.ValidatePlanPath);
        Assert.Empty(empty.PassthroughArgs);

        var other = ConfigurationArgs.Parse(["--print-config"]);
        Assert.False(other.ValidatePlan);
        Assert.Null(other.ValidatePlanPath);
    }

    [Fact]
    public async Task Env_override_is_not_persisted_on_save()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["UseMockVisa"] = "false",
            },
            commandLineOverlays: null);

        Assert.False(store.AppSettings.UseMockVisa);
        Assert.True(store.IsOverridden("UseMockVisa"));

        store.AppSettings.ThemePreference = "Dark";
        await store.SaveAppSettingsAsync();

        var reload = new SettingsStore(temp.Path);
        await reload.LoadAsync();
        Assert.True(reload.AppSettings.UseMockVisa); // file kept default true
        Assert.Equal("Dark", reload.AppSettings.ThemePreference);
    }
}
