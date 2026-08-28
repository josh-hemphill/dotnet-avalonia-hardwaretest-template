using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
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
    }

    private static void WriteSidecar(string directory, string planId, bool selectionIncludesCleanup)
    {
        File.WriteAllText(
            Path.Combine(directory, $"{planId}.program.json"),
            $$"""
            {
              "displayName": "{{planId}}",
              "dutFamily": "demo",
              "requireSerial": true,
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
