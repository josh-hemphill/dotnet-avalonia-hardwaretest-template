using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class PlanContractValidatorTests
{
    [Fact]
    public void Validate_missing_file_is_error()
    {
        var report = PlanContractValidator.ValidateFile(
            Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".TapPlan"));
        Assert.True(report.HasErrors);
        Assert.Contains(report.Findings, f => f.Code == PlanContractValidator.Codes.FileNotFound);
    }

    [Fact]
    public void Validate_sample_plan_has_no_errors()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        WriteSidecar(dir.Path, "sample", selectionIncludesCleanup: true);

        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName));
        Assert.False(report.HasErrors);
        Assert.DoesNotContain(report.Findings, f => f.Code == PlanContractValidator.Codes.MissingSafeShutdown);
        Assert.DoesNotContain(report.Findings, f => f.Code == PlanContractValidator.Codes.NoRebindableSlot);
        Assert.DoesNotContain(report.Findings, f => f.Code == PlanContractValidator.Codes.DuplicateLeafPath);
        Assert.DoesNotContain(report.Findings, f => f.Code == PlanContractValidator.Codes.PresentationTimeseriesOnly);
    }

    [Fact]
    public void Validate_committed_sample_tapplan_loads()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "Programs", SampleProgramFactory.EmbeddedName);
        Assert.True(File.Exists(shipped), shipped);
        using var dir = new TempPlanDir();
        var path = Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName);
        File.Copy(shipped, path);
        WriteSidecar(dir.Path, "sample", selectionIncludesCleanup: true);
        var report = PlanContractValidator.ValidateFile(path);
        Assert.DoesNotContain(
            report.Findings,
            f => f.Code == PlanContractValidator.Codes.PlanLoadFailed);
        Assert.False(
            report.HasErrors,
            string.Join("; ", report.Findings.Select(f => $"{f.Code}: {f.Message}")));
    }

    [Fact]
    public void Validate_does_not_register_a_process_visa_broker()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        WriteSidecar(dir.Path, "sample", selectionIncludesCleanup: true);
        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName));
        Assert.False(report.HasErrors);

        var dmm = new VisaDmmInstrument { VisaAddress = "MOCK::INSTR0" };
        var ex = Assert.Throws<InvalidOperationException>(dmm.Open);
        Assert.Contains("IVisaBroker", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_no_safe_shutdown_is_error_unless_sidecar_opts_out()
    {
        using var dir = new TempPlanDir();
        PlanShapeFixtures.SaveAllBeside(dir.Path);
        var planPath = Path.Combine(dir.Path, PlanShapeFixtures.NoSafeShutdownName);

        var without = PlanContractValidator.ValidateFile(planPath);
        Assert.Contains(without.Findings, f => f.Code == PlanContractValidator.Codes.MissingSafeShutdown
            && f.Severity == PlanContractSeverity.Error);

        WriteSidecar(dir.Path, Path.GetFileNameWithoutExtension(PlanShapeFixtures.NoSafeShutdownName), selectionIncludesCleanup: false);
        var optedOut = PlanContractValidator.ValidateFile(planPath);
        Assert.DoesNotContain(optedOut.Findings, f => f.Code == PlanContractValidator.Codes.MissingSafeShutdown);
    }

    [Fact]
    public void Validate_duplicate_names_keep_unique_paths()
    {
        using var dir = new TempPlanDir();
        PlanShapeFixtures.SaveAllBeside(dir.Path);
        WriteSidecar(dir.Path, Path.GetFileNameWithoutExtension(PlanShapeFixtures.DuplicateNamesName), true);

        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, PlanShapeFixtures.DuplicateNamesName));
        Assert.DoesNotContain(report.Findings, f => f.Code == PlanContractValidator.Codes.DuplicateLeafPath);
    }

    [Fact]
    public void Validate_deep_nest_warns_chrome_depth()
    {
        using var dir = new TempPlanDir();
        PlanShapeFixtures.SaveAllBeside(dir.Path);
        WriteSidecar(dir.Path, Path.GetFileNameWithoutExtension(PlanShapeFixtures.DeepNestName), true);

        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, PlanShapeFixtures.DeepNestName));
        Assert.Contains(report.Findings, f => f.Code == PlanContractValidator.Codes.NestDepth
            && f.Severity == PlanContractSeverity.Warning);
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void Validate_invalid_sidecar_json_is_error()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "sample.program.json"), "{ not json");

        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName));
        Assert.Contains(report.Findings, f => f.Code == PlanContractValidator.Codes.SidecarInvalid
            && f.Severity == PlanContractSeverity.Error);
    }

    [Fact]
    public void Validate_shipped_sample_sidecar_has_status_and_certification()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "Programs", "sample.program.json");
        Assert.True(File.Exists(shipped), shipped);
        using var dir = new TempPlanDir();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Programs", SampleProgramFactory.EmbeddedName),
            Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName));
        File.Copy(shipped, Path.Combine(dir.Path, "sample.program.json"));
        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName));
        Assert.DoesNotContain(report.Findings, f => f.Code == PlanContractValidator.Codes.SidecarReportKinds);
        Assert.DoesNotContain(report.Findings, f => f.Code == PlanContractValidator.Codes.SidecarDefaultReportKind);
        Assert.False(report.HasErrors, string.Join("; ", report.Findings.Select(f => $"{f.Code}: {f.Message}")));
        var json = File.ReadAllText(shipped);
        Assert.Contains("\"status\"", json, StringComparison.Ordinal);
        Assert.Contains("\"certification\"", json, StringComparison.Ordinal);
        Assert.Contains("program.schema.json", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_unknown_sidecar_property_is_warning()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        File.WriteAllText(
            Path.Combine(dir.Path, "sample.program.json"),
            """
            {
              "displayName": "sample",
              "dutFamily": "demo",
              "needsDmm": true
            }
            """);
        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName));
        Assert.Contains(report.Findings, f => f.Code == PlanContractValidator.Codes.SidecarUnknownProperty
            && f.Severity == PlanContractSeverity.Warning);
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void Validate_empty_or_unknown_report_kinds_are_errors()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        File.WriteAllText(
            Path.Combine(dir.Path, "sample.program.json"),
            """{ "displayName": "sample", "reportKinds": [] }""");
        var empty = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName));
        Assert.Contains(empty.Findings, f => f.Code == PlanContractValidator.Codes.SidecarReportKinds
            && f.Severity == PlanContractSeverity.Error);

        File.WriteAllText(
            Path.Combine(dir.Path, "sample.program.json"),
            """{ "displayName": "sample", "reportKinds": ["status", "mes"] }""");
        var unknown = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName));
        Assert.Contains(unknown.Findings, f => f.Code == PlanContractValidator.Codes.SidecarReportKinds
            && f.Message.Contains("mes", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_default_report_kind_must_be_listed()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        File.WriteAllText(
            Path.Combine(dir.Path, "sample.program.json"),
            """
            {
              "reportKinds": ["status"],
              "defaultReportKind": "certification"
            }
            """);
        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName));
        Assert.Contains(report.Findings, f => f.Code == PlanContractValidator.Codes.SidecarDefaultReportKind
            && f.Severity == PlanContractSeverity.Error);
    }

    [Fact]
    public void Validate_unknown_default_report_kind_is_error()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        File.WriteAllText(
            Path.Combine(dir.Path, "sample.program.json"),
            """{ "defaultReportKind": "mes" }""");
        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName));
        Assert.Contains(report.Findings, f => f.Code == PlanContractValidator.Codes.SidecarDefaultReportKind
            && f.Severity == PlanContractSeverity.Error);
    }

    [Fact]
    public void Validate_directory_globs_tap_plans()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        PlanShapeFixtures.SaveAllBeside(dir.Path);
        WriteSidecar(dir.Path, "sample", true);

        var batch = PlanContractValidator.Validate([dir.Path]);
        Assert.True(batch.Plans.Count >= 2);
        Assert.Contains(
            batch.Plans,
            p => p.TargetPath.EndsWith(SampleProgramFactory.EmbeddedName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            batch.Plans,
            p => p.TargetPath.EndsWith(PlanShapeFixtures.NoSafeShutdownName, StringComparison.OrdinalIgnoreCase)
                 && p.Findings.Any(f => f.Code == PlanContractValidator.Codes.MissingSafeShutdown));
        Assert.True(batch.HasErrors);
    }

    [Fact]
    public void Format_and_exit_code_treat_warnings_as_success()
    {
        using var dir = new TempPlanDir();
        PlanShapeFixtures.SaveAllBeside(dir.Path);
        WriteSidecar(dir.Path, Path.GetFileNameWithoutExtension(PlanShapeFixtures.DeepNestName), true);
        var batch = PlanContractValidator.Validate([Path.Combine(dir.Path, PlanShapeFixtures.DeepNestName)]);
        Assert.False(batch.HasErrors);
        Assert.Equal(0, PlanContractValidator.ExitCode(batch));
        var text = PlanContractValidator.Format(batch);
        Assert.Contains("OK", text, StringComparison.Ordinal);
        Assert.Contains(PlanContractValidator.Codes.NestDepth, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_missing_path_is_usage_exit()
    {
        using var writer = new StringWriter();
        var code = PlanContractCli.Run([], settings: null, writer);
        Assert.Equal(PlanContractCli.UsageExitCode, code);
        Assert.Contains("--validate-plan", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("--strict", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_blank_path_is_usage_exit()
    {
        using var writer = new StringWriter();
        var code = PlanContractCli.Run([""], settings: null, writer);
        Assert.Equal(PlanContractCli.UsageExitCode, code);
    }

    [Fact]
    public void Validate_trusts_configured_plugin_dirs_when_requested()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        WriteSidecar(dir.Path, "sample", selectionIncludesCleanup: true);
        var extra = Path.Combine(dir.Path, "author-plugins");
        Directory.CreateDirectory(extra);
        var settings = new AppSettings
        {
            UseMockVisa = true,
            OpenTapPluginDirectories = [extra],
        };

        var skipped = PlanContractValidator.ValidateFile(
            Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName),
            settings,
            trustConfiguredPluginDirectories: false);
        Assert.False(skipped.HasErrors);
        Assert.DoesNotContain(
            PluginManager.DirectoriesToSearch,
            d => string.Equals(d, Path.GetFullPath(extra), StringComparison.OrdinalIgnoreCase));

        var trusted = PlanContractValidator.ValidateFile(
            Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName),
            settings,
            trustConfiguredPluginDirectories: true);
        Assert.False(trusted.HasErrors);
        Assert.Contains(
            PluginManager.DirectoriesToSearch,
            d => string.Equals(d, Path.GetFullPath(extra), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_trust_flag_does_not_admit_environment_plugin_dirs()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        WriteSidecar(dir.Path, "sample", selectionIncludesCleanup: true);
        var cliDir = Path.Combine(dir.Path, "cli-plugins");
        var envDir = Path.Combine(dir.Path, "env-plugins");
        Directory.CreateDirectory(cliDir);
        Directory.CreateDirectory(envDir);
        var previous = Environment.GetEnvironmentVariable("HARDWARETEST_OPENTAP_PLUGIN_DIRS");
        try
        {
            Environment.SetEnvironmentVariable("HARDWARETEST_OPENTAP_PLUGIN_DIRS", envDir);
            var settings = new AppSettings
            {
                UseMockVisa = true,
                OpenTapPluginDirectories = [cliDir],
            };
            var report = PlanContractValidator.ValidateFile(
                Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName),
                settings,
                trustConfiguredPluginDirectories: true);
            Assert.False(report.HasErrors);
            Assert.Contains(
                PluginManager.DirectoriesToSearch,
                d => string.Equals(d, Path.GetFullPath(cliDir), StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                PluginManager.DirectoriesToSearch,
                d => string.Equals(d, Path.GetFullPath(envDir), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HARDWARETEST_OPENTAP_PLUGIN_DIRS", previous);
        }
    }

    [Fact]
    public void Validate_strict_missing_sidecar_is_error()
    {
        using var dir = new TempPlanDir();
        SampleProgramFactory.SaveBeside(dir.Path);
        var path = Path.Combine(dir.Path, SampleProgramFactory.EmbeddedName);
        var lenient = PlanContractValidator.ValidateFile(path);
        Assert.Contains(lenient.Findings, f => f.Code == PlanContractValidator.Codes.SidecarMissing
            && f.Severity == PlanContractSeverity.Warning);

        var strict = PlanContractValidator.ValidateFile(path, new PlanContractOptions { Strict = true });
        Assert.Contains(strict.Findings, f => f.Code == PlanContractValidator.Codes.SidecarMissing
            && f.Severity == PlanContractSeverity.Error);
        Assert.True(strict.HasErrors);
    }

    [Fact]
    public void Validate_require_serial_without_identity_is_error()
    {
        using var dir = new TempPlanDir();
        PlanShapeFixtures.SaveAllBeside(dir.Path);
        var planId = Path.GetFileNameWithoutExtension(PlanShapeFixtures.DeepNestName);
        File.WriteAllText(
            Path.Combine(dir.Path, $"{planId}.program.json"),
            """{ "requireSerial": true, "selectionIncludesCleanup": true }""");
        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, PlanShapeFixtures.DeepNestName));
        Assert.Contains(report.Findings, f => f.Code == PlanContractValidator.Codes.MissingIdentity
            && f.Severity == PlanContractSeverity.Error);
    }

    [Fact]
    public void Validate_duplicate_channel_key_is_error()
    {
        using var dir = new TempPlanDir();
        PlanShapeFixtures.SaveAllBeside(dir.Path);
        var path = Path.Combine(dir.Path, "dup.TapPlan");
        PlanShapeFixtures.CreateDuplicateChannelKey().Save(path);
        WriteSidecar(dir.Path, "dup", selectionIncludesCleanup: true);
        var report = PlanContractValidator.ValidateFile(path);
        Assert.Contains(report.Findings, f => f.Code == PlanContractValidator.Codes.DuplicateChannelKey
            && f.Severity == PlanContractSeverity.Error);
    }

    [Fact]
    public void Validate_measure_leaf_without_presentation_warns()
    {
        using var dir = new TempPlanDir();
        PlanShapeFixtures.SaveAllBeside(dir.Path);
        WriteSidecar(dir.Path, Path.GetFileNameWithoutExtension(PlanShapeFixtures.FlatLeavesName), true);
        var report = PlanContractValidator.ValidateFile(Path.Combine(dir.Path, PlanShapeFixtures.FlatLeavesName));
        Assert.Contains(report.Findings, f => f.Code == PlanContractValidator.Codes.MissingPresentation
            && f.Severity == PlanContractSeverity.Warning);
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void Validate_passband_without_limits_warns()
    {
        using var dir = new TempPlanDir();
        PlanShapeFixtures.SaveAllBeside(dir.Path);
        var path = Path.Combine(dir.Path, "band.TapPlan");
        PlanShapeFixtures.CreatePassbandWithoutLimits().Save(path);
        WriteSidecar(dir.Path, "band", true);
        var report = PlanContractValidator.ValidateFile(path);
        Assert.Contains(report.Findings, f => f.Code == PlanContractValidator.Codes.MissingLimits
            && f.Severity == PlanContractSeverity.Warning);
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void Cli_json_and_sarif_include_finding_codes()
    {
        using var dir = new TempPlanDir();
        PlanShapeFixtures.SaveAllBeside(dir.Path);
        var path = Path.Combine(dir.Path, PlanShapeFixtures.NoSafeShutdownName);
        using var jsonWriter = new StringWriter();
        var jsonCode = PlanContractCli.Run(
            [path],
            jsonWriter,
            new PlanContractOptions { Format = PlanContractFormat.Json });
        Assert.Equal(1, jsonCode);
        var json = jsonWriter.ToString();
        Assert.Contains("MISSING_SAFE_SHUTDOWN", json, StringComparison.Ordinal);
        Assert.Contains("\"hasErrors\": true", json, StringComparison.Ordinal);

        using var sarifWriter = new StringWriter();
        PlanContractCli.Run([path], sarifWriter, new PlanContractOptions { Format = PlanContractFormat.Sarif });
        var sarif = sarifWriter.ToString();
        Assert.Contains("\"ruleId\": \"MISSING_SAFE_SHUTDOWN\"", sarif, StringComparison.Ordinal);
        Assert.Contains("sarif-2.1.0", sarif, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_committed_product_plans_have_no_errors_when_strict()
    {
        var programs = Path.Combine(AppContext.BaseDirectory, "Programs");
        Assert.True(Directory.Exists(programs), programs);
        var plans = Directory.EnumerateFiles(programs, "*.TapPlan", SearchOption.TopDirectoryOnly).ToList();
        Assert.Contains(plans, p => p.EndsWith("sample.TapPlan", StringComparison.OrdinalIgnoreCase));
        var batch = PlanContractValidator.Validate(plans, new PlanContractOptions { Strict = true });
        Assert.DoesNotContain(
            batch.Plans,
            p => p.TargetPath.Contains($"{Path.DirectorySeparatorChar}fixtures{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        Assert.False(
            batch.HasErrors,
            string.Join("; ", batch.Plans.SelectMany(p => p.Findings).Select(f => $"{f.Code}: {f.Message}")));
    }

    private static void WriteSidecar(string directory, string planId, bool selectionIncludesCleanup)
    {
        File.WriteAllText(
            Path.Combine(directory, $"{planId}.program.json"),
            $$"""
            {
              "displayName": "{{planId}}",
              "dutFamily": "demo",
              "requireSerial": false,
              "selectionIncludesCleanup": {{(selectionIncludesCleanup ? "true" : "false")}}
            }
            """);
    }

    private sealed class TempPlanDir : IDisposable
    {
        public TempPlanDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "plan-contract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
