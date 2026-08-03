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
        Assert.Equal(1, entries.Count(e => e.Id == "sample"));
        Assert.Equal(1, entries.Count(e => e.Id == "board-demo"));
        Assert.Equal(1, entries.Count(e => e.Id == "sweep-demo"));
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
}
