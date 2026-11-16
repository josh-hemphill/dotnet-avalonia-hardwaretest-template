using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringRecoveryPresentationTests
{
    [Fact]
    public void Dynamic_numeric_recovery_targets_actual_visible_settings_and_rejects_unverified_targets()
    {
        var metric = new MetricNode(new MetricDraft("Acquire", "VDC", "timeseries", "V", null, null,
            new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>())));
        var draft = new ProgramDraft("sample", new ProgramSidecar(), [], [], [metric], new CleanupPolicy(false, "DMM"));
        var target = new PlanContractTarget(NodeId: metric.NodeId, Section: "Configure", Field: "SampleCount");
        var destination = AuthoringFindingNavigation.Resolve(draft, metric.NodeId, target);
        Assert.Equal("Go to field", destination.Label);
        Assert.Equal(metric.NodeId, destination.Target.NodeId);
        Assert.Equal("SampleCount", destination.Target.Field);
        Assert.Equal("Configure", destination.Target.Section);
        foreach (var invalid in new[] { target with { Field = "samplecount" }, target with { Field = "MissingSetting" },
                     target with { Field = "Channel" }, target with { Section = "Unsupported" } })
        {
            var fallback = AuthoringFindingNavigation.Resolve(draft, metric.NodeId, invalid);
            Assert.Equal("ProgramSettings", fallback.Target.Section);
            Assert.Null(fallback.Target.NodeId);
        }
        Assert.Null(AuthoringFindingNavigation.Resolve(draft with { Measure = [metric, metric] }, metric.NodeId, target).Target.NodeId);
        Assert.Null(AuthoringFindingNavigation.Resolve(draft, Guid.NewGuid(), target).Target.NodeId);
        var unknown = metric with { Metric = metric.Metric with { Source = new MeasureSource("DMM", "Unknown.Plugin", new Dictionary<string, string> { ["SampleCount"] = "2" }) } };
        Assert.Null(AuthoringFindingNavigation.Resolve(draft with { Measure = [unknown] }, unknown.NodeId,
            target with { NodeId = unknown.NodeId }).Target.NodeId);
    }

    [Fact]
    public void Action_copy_supplements_complete_diagnostics_and_unknown_messages_are_preserved()
    {
        const string diagnostic = "Instrument package unavailable at /private/workspace/Packages/InstrumentComponents.OpenTap. Full diagnostic remains available.";
        var package = new AuthoringEditingIssue("INSTRUMENT_UNAVAILABLE", diagnostic, "sample", Guid.Empty)
        { RecoveryArea = AuthoringRecoveryArea.Environment };
        Assert.Equal("The instrument package is not ready", package.Headline);
        Assert.Equal("Open Environment", package.RecoveryLabel);
        Assert.DoesNotContain("/private", package.RecoverySummary);
        Assert.Equal(diagnostic, package.Message);
        var adapter = package with { RecoveryArea = AuthoringRecoveryArea.Instruments };
        Assert.Equal("Configure instruments", adapter.RecoveryLabel);
        Assert.Contains("authoring adapter", adapter.Headline);
        var unknown = new AuthoringEditingIssue("NEW_PLUGIN_FINDING", diagnostic, "sample", Guid.Empty);
        Assert.Equal(diagnostic, unknown.RecoverySummary);
        Assert.Equal(diagnostic, unknown.Message);
    }
}
