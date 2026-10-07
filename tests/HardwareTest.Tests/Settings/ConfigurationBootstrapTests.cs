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
        store.AppSettings.ReportTemplateName = "file-report.typ";
        await store.SaveAppSettingsAsync();

        var env = new Hashtable
        {
            ["HARDWARETEST_LOG_MINIMUM_LEVEL"] = "Debug",
            ["HARDWARETEST_USE_MOCK_VISA"] = "true",
            ["HARDWARETEST_PLOT_REFRESH_HZ"] = "22",
            ["HARDWARETEST_OPEN_TAP_PLUGIN_DIRECTORIES"] = "from-env",
            ["HARDWARETEST_REPORT_TEMPLATE_NAME"] = "env-report.typ",
        };
        var args = ConfigurationArgs.Parse(
        [
            "--log-level", "Error",
            "--mock-visa=false",
            "--plot-refresh-hz", "33",
            "--opentap-plugin-dirs", "from-cli",
            "--report-template", "cli-report.typ",
        ]);

        var result = await ConfigurationBootstrap.ResolveAsync(args, env, defaultRoot: temp.Path);
        Assert.Equal("Error", result.Store.AppSettings.LogMinimumLevel);
        Assert.False(result.Store.AppSettings.UseMockVisa);
        Assert.Equal(33, result.Store.AppSettings.PlotRefreshHz);
        Assert.Equal(["from-cli"], result.Store.AppSettings.OpenTapPluginDirectories);
        Assert.Equal("cli-report.typ", result.Store.AppSettings.ReportTemplateName);
        var reportRow = Assert.Single(result.Store.Provenance, row => row.Key == nameof(AppSettings.ReportTemplateName));
        Assert.Equal(SettingSource.CommandLine, reportRow.Source);
        Assert.Equal("cli-report.typ", reportRow.EffectiveValue);
        await result.Store.SaveAppSettingsAsync();
        Assert.Equal("cli-report.typ", result.Store.AppSettings.ReportTemplateName);
        var reload = new SettingsStore(temp.Path);
        await reload.LoadAsync();
        Assert.Equal("file-report.typ", reload.AppSettings.ReportTemplateName);
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

    [Theory]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    public async Task Boolean_input_spellings_are_supported(string raw, bool expected)
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);

        await store.LoadAsync(
            new Dictionary<string, string> { ["UseMockVisa"] = raw },
            commandLineOverlays: null);

        Assert.Equal(expected, store.AppSettings.UseMockVisa);
    }

    [Fact]
    public async Task Indexed_environment_overlays_merge_into_existing_lists()
    {
        using var temp = new TempDataDirectory();
        var env = new Hashtable
        {
            ["HARDWARETEST_PLAN_SLOT_OVERRIDES__0__PLAN_ID"] = "sample",
            ["HARDWARETEST_PLAN_SLOT_OVERRIDES__0__SLOT_NAME"] = "Meter",
            ["HARDWARETEST_PLAN_SLOT_OVERRIDES__0__RESOURCE"] = "MOCK::BENCH",
            ["HARDWARETEST_PLAN_PARAMETER_OVERRIDES__0__PLAN_ID"] = "sample",
            ["HARDWARETEST_PLAN_PARAMETER_OVERRIDES__0__MEMBER_KEY"] = "acq/SampleCount",
            ["HARDWARETEST_PLAN_PARAMETER_OVERRIDES__0__VALUE"] = "32",
            ["HARDWARETEST_OPEN_TAP_PLUGIN_DIRECTORIES__0"] = "plugins/site",
        };

        var overlays = AppSettingsEnvironmentBinder.ReadEnvironment(env);
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync(overlays, commandLineOverlays: null);

        var slot = Assert.Single(store.AppSettings.PlanSlotOverrides);
        Assert.Equal("sample", slot.PlanId);
        Assert.Equal("Meter", slot.SlotName);
        Assert.Equal("MOCK::BENCH", slot.Resource);
        var parameter = Assert.Single(store.AppSettings.PlanParameterOverrides);
        Assert.Equal("sample", parameter.PlanId);
        Assert.Equal("acq/SampleCount", parameter.MemberKey);
        Assert.Equal("32", parameter.Value);
        Assert.Equal(["plugins/site"], store.AppSettings.OpenTapPluginDirectories);
        Assert.True(store.IsOverridden("PlanSlotOverrides"));
        Assert.True(store.IsOverridden("PlanParameterOverrides"));
        Assert.True(store.IsOverridden("OpenTapPluginDirectories"));
    }

    [Fact]
    public async Task Invalid_indexed_overlays_are_ignored_without_expanding_lists()
    {
        using var temp = new TempDataDirectory();
        var warnings = new List<string>();
        var store = new SettingsStore(temp.Path);

        await store.LoadAsync(
            new Dictionary<string, string>
            {
                ["OpenTapPluginDirectories[-1]"] = "negative",
                ["PlanSlotOverrides[100000].PlanId"] = "too-large",
            },
            commandLineOverlays: null,
            warn: warnings.Add);

        Assert.Empty(store.AppSettings.OpenTapPluginDirectories);
        Assert.Empty(store.AppSettings.PlanSlotOverrides);
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, warning => Assert.Contains("out of range", warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Scalar_inputs_are_normalized_after_binding()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);

        await store.LoadAsync(
            new Dictionary<string, string>
            {
                ["OpenTapWorkerKillTimeoutMilliseconds"] = "1",
                ["ClockSkewWarnThresholdMinutes"] = "99999",
                ["NtpHost"] = "  ntp.example.test  ",
                ["StationHealthProfileId"] = "   ",
                ["StationHealthGateOverride"] = "not-a-gate",
            },
            commandLineOverlays: null);

        Assert.Equal(AppSettings.MinOpenTapWorkerKillTimeoutMilliseconds, store.AppSettings.OpenTapWorkerKillTimeoutMilliseconds);
        Assert.Equal(AppSettings.MaxClockSkewWarnThresholdMinutes, store.AppSettings.ClockSkewWarnThresholdMinutes);
        Assert.Equal("ntp.example.test", store.AppSettings.NtpHost);
        Assert.Equal("default", store.AppSettings.StationHealthProfileId);
        Assert.Equal(string.Empty, store.AppSettings.StationHealthGateOverride);
        Assert.Equal(
            string.Empty,
            store.Provenance.Single(row => row.Key == "StationHealthGateOverride").EffectiveValue);
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
            """{"schemaVersion":1,"logMinimumLevel":"Warning","useMockVisa":true,"dataDirectory":""}""");

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
    public async Task Malformed_cli_bool_keeps_prior_value_and_warns()
    {
        using var temp = new TempDataDirectory();
        var warnings = new List<string>();
        var args = ConfigurationArgs.Parse(["--ALLOW-PRESENCE-IN-LIEU-OF-SIGNING=typo"]);
        Assert.Equal("typo", args.Overlays["AllowPresenceInLieuOfSigning"]);

        var store = new SettingsStore(temp.Path);
        await store.LoadAsync(
            environmentOverlays: null,
            commandLineOverlays: args.Overlays,
            warn: warnings.Add);

        Assert.True(store.AppSettings.AllowPresenceInLieuOfSigning);
        Assert.Contains(warnings, warning => warning.Contains(
            "AllowPresenceInLieuOfSigning",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Avalonia_help_options_remain_passthrough()
    {
        var args = ConfigurationArgs.Parse(["--HELP", "-?"]);

        Assert.Equal(["--HELP", "-?"], args.PassthroughArgs);
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
    public async Task Probe_badge_when_technician_focused_defaults_off_and_cli_enables()
    {
        using var temp = new TempDataDirectory();
        var defaults = await ConfigurationBootstrap.ResolveAsync(
            ConfigurationArgs.Parse([]),
            environment: null,
            defaultRoot: temp.Path);
        Assert.False(defaults.Store.AppSettings.ProbeBadgeWhenTechnicianFocused);

        var enabled = await ConfigurationBootstrap.ResolveAsync(
            ConfigurationArgs.Parse(["--probe-badge-when-technician-focused"]),
            environment: null,
            defaultRoot: temp.Path);
        Assert.True(enabled.Store.AppSettings.ProbeBadgeWhenTechnicianFocused);
        Assert.Equal(
            SettingSource.CommandLine,
            enabled.Store.Provenance.Single(p => p.Key == "ProbeBadgeWhenTechnicianFocused").Source);
    }

    [Fact]
    public async Task Pkcs11_library_path_supports_environment_and_command_line_precedence()
    {
        using var temp = new TempDataDirectory();
        var env = new Hashtable { ["HARDWARETEST_PKCS11_LIBRARY"] = "/from/environment.so" };
        var result = await ConfigurationBootstrap.ResolveAsync(
            ConfigurationArgs.Parse(["--pkcs11-library", "/from/command-line.so"]),
            env,
            defaultRoot: temp.Path);

        Assert.Equal("/from/command-line.so", result.Store.AppSettings.Pkcs11LibraryPath);
        Assert.Equal(
            SettingSource.CommandLine,
            result.Store.Provenance.Single(p => p.Key == "Pkcs11LibraryPath").Source);
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

    [Fact]
    public async Task Removed_environment_registry_and_hours_inputs_are_ignored()
    {
        using var temp = new TempDataDirectory();
        var env = new Hashtable
        {
            ["HARDWARETEST_INSTRUMENTS__0__RESOURCE"] = "MOCK::OLD",
            ["HARDWARETEST_STATION_BINDINGS__0__ROLE"] = "dmm",
            ["HARDWARETEST_OPERATOR_SESSION_IDLE_HOURS"] = "12",
            ["HARDWARETEST_OPENTAP_PLUGIN_DIRS"] = "old-plugins",
            ["HARDWARETEST_DEFAULT_VISA_RESOURCE"] = "MOCK::OLD",
        };
        var overlays = AppSettingsEnvironmentBinder.ReadEnvironment(env);
        Assert.Empty(overlays);
        var store = new SettingsStore(temp.Path);

        await store.LoadAsync(overlays, commandLineOverlays: null);

        Assert.Empty(store.AppSettings.PlanSlotOverrides);
        Assert.Empty(store.AppSettings.PlanParameterOverrides);
        Assert.Empty(store.AppSettings.OpenTapPluginDirectories);
        Assert.Equal(OperatorSessionIdle.DefaultMinutes, store.AppSettings.OperatorSessionIdleMinutes);
        Assert.DoesNotContain(store.Provenance, row => row.Key.Contains("Hours", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("75", "19", 19)]
    [InlineData("75", "0", OperatorSessionIdle.MinMinutes)]
    [InlineData("75", "20000", OperatorSessionIdle.MaxMinutes)]
    [InlineData("75", "invalid", 75)]
    public async Task Minute_overlays_follow_file_environment_command_line_precedence_and_normalize(
        string environmentMinutes, string commandLineMinutes, int expectedMinutes)
    {
        using var temp = new TempDataDirectory();
        var baseline = new SettingsStore(temp.Path);
        baseline.AppSettings.OperatorSessionIdleMinutes = 31;
        await baseline.SaveAppSettingsAsync();
        var env = new Hashtable
        {
            ["HARDWARETEST_OPERATOR_SESSION_IDLE_MINUTES"] = environmentMinutes,
        };
        var result = await ConfigurationBootstrap.ResolveAsync(
            ConfigurationArgs.Parse(["--session-idle-minutes", commandLineMinutes]),
            env,
            defaultRoot: temp.Path);

        Assert.Equal(expectedMinutes, result.Store.AppSettings.OperatorSessionIdleMinutes);
        var row = Assert.Single(result.Store.Provenance,
            row => row.Key == nameof(AppSettings.OperatorSessionIdleMinutes));
        Assert.Equal(commandLineMinutes == "invalid" ? SettingSource.Environment : SettingSource.CommandLine, row.Source);
        Assert.Equal(expectedMinutes.ToString(), row.EffectiveValue);
        await result.Store.SaveAppSettingsAsync();
        Assert.Equal(expectedMinutes, result.Store.AppSettings.OperatorSessionIdleMinutes);
        var reload = new SettingsStore(temp.Path);
        await reload.LoadAsync();
        Assert.Equal(31, reload.AppSettings.OperatorSessionIdleMinutes);
    }

    [Fact]
    public async Task Indexed_station_overlays_preserve_file_baseline_on_save()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        store.AppSettings.PlanSlotOverrides =
        [
            new PlanSlotOverride { PlanId = "sample", SlotName = "Meter", Resource = "MOCK::FILE" },
        ];
        store.AppSettings.PlanParameterOverrides =
        [
            new PlanParameterOverride { PlanId = "sample", MemberKey = "acq/SampleCount", Value = "16" },
        ];
        await store.SaveAppSettingsAsync();
        await store.LoadAsync(
            new Dictionary<string, string>
            {
                ["PlanSlotOverrides[0].Resource"] = "MOCK::ENV",
                ["PlanParameterOverrides[0].Value"] = "32",
            },
            new Dictionary<string, string>
            {
                ["PlanSlotOverrides[0].Resource"] = "MOCK::CLI",
            });

        Assert.Equal("MOCK::CLI", Assert.Single(store.AppSettings.PlanSlotOverrides).Resource);
        Assert.Equal("32", Assert.Single(store.AppSettings.PlanParameterOverrides).Value);
        store.AppSettings.ThemePreference = "Dark";
        await store.SaveAppSettingsAsync();
        Assert.Equal("MOCK::CLI", Assert.Single(store.AppSettings.PlanSlotOverrides).Resource);
        var reload = new SettingsStore(temp.Path);
        await reload.LoadAsync();
        Assert.Equal("MOCK::FILE", Assert.Single(reload.AppSettings.PlanSlotOverrides).Resource);
        Assert.Equal("16", Assert.Single(reload.AppSettings.PlanParameterOverrides).Value);
        Assert.Equal("Dark", reload.AppSettings.ThemePreference);
    }

    [Fact]
    public async Task Canonical_environment_minutes_and_plugin_directories_override_file_values()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        store.AppSettings.OperatorSessionIdleMinutes = 31;
        store.AppSettings.OpenTapPluginDirectories = ["file-plugins"];
        await store.SaveAppSettingsAsync();
        var env = new Hashtable
        {
            ["HARDWARETEST_OPERATOR_SESSION_IDLE_MINUTES"] = "75",
            [AppSettingsEnvironmentBinder.OpenTapPluginDirectoriesEnv] = "env-plugins",
        };

        var result = await ConfigurationBootstrap.ResolveAsync(
            ConfigurationArgs.Parse([]), env, defaultRoot: temp.Path);

        Assert.Equal(75, result.Store.AppSettings.OperatorSessionIdleMinutes);
        Assert.Equal(["env-plugins"], result.Store.AppSettings.OpenTapPluginDirectories);
        Assert.Equal(SettingSource.Environment, result.Store.Provenance.Single(
            row => row.Key == nameof(AppSettings.OperatorSessionIdleMinutes)).Source);
        Assert.Equal(SettingSource.Environment, result.Store.Provenance.Single(
            row => row.Key == nameof(AppSettings.OpenTapPluginDirectories)).Source);
    }
}
