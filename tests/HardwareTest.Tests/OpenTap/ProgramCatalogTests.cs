using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

public sealed class ProgramCatalogTests
{
    [Fact]
    public void Enumerate_includes_built_ins_without_duplicating_disk_sample()
    {
        var entries = ProgramCatalog.Enumerate();
        Assert.Equal("sample", entries[0].Id);
        Assert.Contains(entries, e => e.Id == "sample" && e.LoadKind == ProgramLoadKind.FactorySample);
        Assert.Contains(entries, e => e.Id == "board-demo" && e.LoadKind == ProgramLoadKind.FactoryBoardDemo);
        Assert.Contains(entries, e => e.Id == "sweep-demo" && e.LoadKind == ProgramLoadKind.FactorySweepDemo);
        Assert.Contains(entries, e => e.Id == "timing-demo" && e.LoadKind == ProgramLoadKind.FactoryTimingDemo);
        Assert.Equal(1, entries.Count(e => e.Id == "sample"));
        Assert.Equal(1, entries.Count(e => e.Id == "board-demo"));
        Assert.Equal(1, entries.Count(e => e.Id == "sweep-demo"));
        Assert.Equal(1, entries.Count(e => e.Id == "timing-demo"));
    }

    [Fact]
    public void Enumerate_reads_sidecar_metadata_for_disk_plans()
    {
        var dir = Path.Combine(Path.GetTempPath(), "program-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var planPath = Path.Combine(dir, "custom-suite.TapPlan");
            File.WriteAllText(planPath, "<TestPlan />");
            File.WriteAllText(
                Path.Combine(dir, "custom-suite.program.json"),
                """
                {
                  "displayName": "Custom Suite",
                  "dutFamily": "power",
                  "requireSerial": true,
                  "requireOperator": false,
                  "requirePartNumber": true
                }
                """);

            var entry = ProgramCatalog.Enumerate([dir]).First(e => e.Id == "custom-suite");
            Assert.Equal("Custom Suite", entry.DisplayName);
            Assert.Equal("power", entry.DutFamily);
            Assert.True(entry.Requirements.RequirePartNumber);
            Assert.False(entry.Requirements.RequireOperator);
            Assert.Equal(ProgramLoadKind.TapPlanFile, entry.LoadKind);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SelfCheck_warns_on_missing_and_invalid_sidecars()
    {
        var dir = Path.Combine(Path.GetTempPath(), "program-selfcheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "custom-suite.TapPlan"), "<TestPlan />");
            File.WriteAllText(Path.Combine(dir, "broken.TapPlan"), "<TestPlan />");
            File.WriteAllText(Path.Combine(dir, "broken.program.json"), "{ not json");
            File.WriteAllText(Path.Combine(dir, "sample.TapPlan"), "<TestPlan />");

            var warnings = ProgramCatalog.SelfCheck([dir]);

            Assert.Contains(warnings, w => w.Contains("Missing sidecar custom-suite.program.json", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("Invalid sidecar broken.program.json", StringComparison.Ordinal));
            Assert.DoesNotContain(warnings, w => w.Contains("sample.program.json", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SelfCheck_accepts_valid_sidecar()
    {
        var dir = Path.Combine(Path.GetTempPath(), "program-selfcheck-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "custom-suite.TapPlan"), "<TestPlan />");
            File.WriteAllText(
                Path.Combine(dir, "custom-suite.program.json"),
                """{ "displayName": "Custom Suite" }""");

            Assert.Empty(ProgramCatalog.SelfCheck([dir]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
