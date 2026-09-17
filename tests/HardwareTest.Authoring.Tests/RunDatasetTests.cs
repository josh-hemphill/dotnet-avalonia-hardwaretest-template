using HardwareTest.Authoring;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class RunDatasetTests
{
    [Fact]
    public void Load_v1_fixture_exposes_vdc_series()
    {
        var dataset = RunDatasetCatalog.Load(Path.Combine(FindRepoRoot(), "tests", "fixtures", "schema", "run-v1.json"));
        var series = RunDatasetBinder.SeriesByMetric(dataset.Run);
        Assert.True(series.ContainsKey("VDC"));
        Assert.Equal(10, Assert.Single(series["VDC"]).Value);
        Assert.False(dataset.Run.IsSchemaReadOnly);
    }

    [Fact]
    public void Mean_vdc_golden_evals_to_two()
    {
        var dataset = RunDatasetCatalog.Load(
            Path.Combine(FindRepoRoot(), "tests", "fixtures", "authoring", "recordings", "sample", "mean-vdc", "run.json"));
        Assert.True(string.IsNullOrWhiteSpace(dataset.Run.DutSerial));
        var draft = FormulaDraft("sample");
        var result = Assert.Single(FormulaDatasetEval.EvaluateProgram(draft, dataset.Run));
        Assert.Equal(2, result.Value);
        Assert.Equal("VDC.mean", result.EffectiveMetricKey);
    }

    [Fact]
    public void Missing_channel_fails_eval()
    {
        var run = new TestRunRecord
        {
            PlanId = "sample",
            Samples =
            [
                new StoredSample { Channel = "other", MetricKey = "other", Value = 1 },
            ],
        };
        var ex = Assert.Throws<AuthoringWorkspaceException>(
            () => FormulaDatasetEval.EvaluateProgram(FormulaDraft("sample"), run));
        Assert.Contains(AuthoringCompileCodes.FormulaEval, ex.Message, StringComparison.Ordinal);
        Assert.Contains("VDC", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mean_without_threshold_fails_missing_limits()
    {
        var run = new TestRunRecord
        {
            PlanId = "sample",
            Samples =
            [
                new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 1 },
                new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 3 },
            ],
        };
        var draft = FormulaDraft("sample") with
        {
            Measure =
            [
                new MetricNode(new MetricDraft(
                    "Formula",
                    "VDC.mean",
                    PresentationRoles.Scalar,
                    "V",
                    null,
                    null,
                    new ExpressionAlgorithm(["VDC"], "mean(VDC)"))),
            ],
        };
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => FormulaDatasetEval.EvaluateProgram(draft, run));
        Assert.Contains(AuthoringCompileCodes.MissingLimits, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Transfer_function_is_skipped_not_identity()
    {
        var run = new TestRunRecord
        {
            PlanId = "tf",
            Samples =
            [
                new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 1.5 },
            ],
        };
        var draft = new ProgramDraft(
            "tf",
            new ProgramSidecar { DisplayName = "tf" },
            [],
            [],
            [
                new MetricNode(new MetricDraft(
                    "Filter",
                    "VDC.filt",
                    PresentationRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new TransferFunctionAlgorithm("VDC", [0.5, 0.5], [1, 0], 0.005, "filter"))),
            ],
            new CleanupPolicy(false, "DMM"));
        Assert.True(FormulaDatasetEval.HasPendingTransferFunction(draft));
        Assert.Empty(FormulaDatasetEval.EvaluateProgram(draft, run));
        var preview = MetricPreviewBuilder.From(
            ((MetricNode)draft.Measure[0]).Metric,
            null,
            RunDatasetBinder.SeriesByMetric(run));
        Assert.Equal(FormulaDatasetEval.TransferFunctionPendingNote, preview.Note);
        Assert.Empty(preview.CannedSamples);
        Assert.Equal(0, preview.CannedValue);
    }

    [Fact]
    public void Future_schema_loads_read_only_without_overwrite()
    {
        var src = Path.Combine(FindRepoRoot(), "tests", "fixtures", "schema", "run-v999-future.json");
        var dest = Path.Combine(NewTempDir(), "run.json");
        File.Copy(src, dest);
        var before = File.ReadAllText(dest);
        var dataset = RunDatasetCatalog.Load(dest);
        Assert.True(dataset.Run.IsSchemaReadOnly);
        Assert.Equal(999, dataset.Run.StoredSchemaVersion);
        Assert.Equal(before, File.ReadAllText(dest));
    }

    [Fact]
    public void Template_workspace_lists_no_recordings()
    {
        var workspace = AuthoringWorkspaceLoader.Load(Path.Combine(FindRepoRoot(), "plans", "opentap"));
        Assert.Empty(RunDatasetCatalog.List(workspace));
    }

    [Fact]
    public void List_includes_run_without_dut_serial()
    {
        var root = CopyTemplateWorkspace();
        var dest = Path.Combine(root, "recordings", "sample", "mean-vdc");
        Directory.CreateDirectory(dest);
        File.Copy(
            Path.Combine(FindRepoRoot(), "tests", "fixtures", "authoring", "recordings", "sample", "mean-vdc", "run.json"),
            Path.Combine(dest, "run.json"));
        var workspace = AuthoringWorkspaceLoader.Load(root);
        var dataset = Assert.Single(RunDatasetCatalog.List(workspace));
        Assert.True(string.IsNullOrWhiteSpace(dataset.Run.DutSerial));
        Assert.Equal("sample", dataset.Run.PlanId);
    }

    [Fact]
    public void Extra_metric_keys_are_kept()
    {
        var run = new TestRunRecord
        {
            Samples =
            [
                new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 1 },
                new StoredSample { Channel = "extra", MetricKey = "extra", Value = 9 },
            ],
        };
        var series = RunDatasetBinder.SeriesByMetric(run);
        Assert.Contains("VDC", series.Keys);
        Assert.Contains("extra", series.Keys);
    }

    private static ProgramDraft FormulaDraft(string planId)
        => new(
            planId,
            new ProgramSidecar { DisplayName = planId },
            [],
            [],
            [
                new MetricNode(new MetricDraft(
                    "Formula",
                    "VDC.mean",
                    PresentationRoles.Scalar,
                    "V",
                    new LimitSpec(null, null, 1.2),
                    null,
                    new ExpressionAlgorithm(["VDC"], "mean(VDC)"))),
            ],
            new CleanupPolicy(false, "DMM"));

    private static string CopyTemplateWorkspace()
    {
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        var dest = NewTempDir();
        File.Copy(Path.Combine(src, "authoring.json"), Path.Combine(dest, "authoring.json"));
        var schema = Path.Combine(src, "authoring.schema.json");
        if (File.Exists(schema))
        {
            File.Copy(schema, Path.Combine(dest, "authoring.schema.json"));
        }

        return dest;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-recordings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("HardwareTest.slnx").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate HardwareTest.slnx above '{AppContext.BaseDirectory}'.");
    }
}
