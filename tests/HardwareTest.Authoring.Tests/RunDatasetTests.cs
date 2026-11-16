using System.Text.Json.Nodes;
using HardwareTest.Authoring;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class RunDatasetTests
{
    [Fact]
    public void Loaded_recording_identity_stays_with_the_ingested_snapshot_when_compatibility_model_changes()
    {
        var dataset = RunDatasetCatalog.Load(Path.Combine(FindRepoRoot(), "tests", "fixtures", "schema", "run-v4.json"));
        var identity = Assert.IsType<RecordingIdentity>(dataset.Identity);
        Assert.Equal(dataset.Run.RunId, identity.RunId);
        Assert.Equal(dataset.Run.DutSerial, identity.DutSerial);
        Assert.Equal(dataset.Run.StartedAt, identity.StartedAt);
        Assert.Equal(dataset.Run.Samples.Count, identity.SampleCount);
        var originalDetails = identity.DisplayDetails;
        dataset.Run.RunId = "mutated compatibility run";
        dataset.Run.DutSerial = "mutated device";
        dataset.Run.StartedAt = DateTimeOffset.UtcNow;
        dataset.Run.Samples.Clear();
        Assert.Equal(originalDetails, dataset.Identity!.DisplayDetails);
        Assert.DoesNotContain("mutated", dataset.Identity.DisplayDetails);
    }

    [Theory]
    [InlineData("run-v4.json", 4, true)]
    [InlineData("run-v5.json", 5, false)]
    public void Load_supported_fixture_exposes_vdc_series_and_preserves_schema_read_only_status(string fixture, int version, bool readOnly)
    {
        var dataset = RunDatasetCatalog.Load(Path.Combine(FindRepoRoot(), "tests", "fixtures", "schema", fixture));
        var series = RunDatasetBinder.SeriesByMetric(dataset.Run);
        Assert.True(series.ContainsKey("VDC"));
        Assert.Equal(10, Assert.Single(series["VDC"]).Value);
        Assert.Equal(readOnly, dataset.Run.IsSchemaReadOnly);
        Assert.Equal(version, dataset.Run.StoredSchemaVersion);
    }

    [Fact]
    public void Historical_read_only_recording_previews_samples_and_formula_while_future_schema_stays_blocked()
    {
        var source = Path.Combine(FindRepoRoot(), "tests", "fixtures", "authoring", "recordings", "sample", "mean-vdc", "run.json");
        var bytes = File.ReadAllBytes(source);
        var recording = RunDatasetCatalog.Load(source).Run;
        Assert.True(recording.IsSchemaReadOnly);
        Assert.Equal(4, recording.StoredSchemaVersion);
        var draft = AuthoringRecipeCatalog.Apply(MockDmmDraftFixture.Create("sample"), AuthoringRecipeIds.Acquire);
        draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Formula);
        var tiles = BoardPreviewBuilder.Build(draft, recording);
        Assert.Equal(2, tiles.Count);
        Assert.All(tiles, tile => Assert.NotEmpty(tile.Preview.CannedSamples));
        Assert.Equal(new[] { 1d, 3d }, tiles[0].Preview.CannedSamples);
        Assert.Equal(2, tiles[1].Preview.CannedValue);
        Assert.True(recording.IsSchemaReadOnly);
        Assert.Equal(bytes, File.ReadAllBytes(source));

        var temporary = NewTempDir();
        try
        {
            var future = JsonNode.Parse(bytes)!;
            future["schemaVersion"] = 999;
            var path = Path.Combine(temporary, "run.json");
            File.WriteAllText(path, future.ToJsonString());
            var futureBytes = File.ReadAllBytes(path);
            var unsupported = RunDatasetCatalog.Load(path).Run;
            Assert.All(BoardPreviewBuilder.Build(draft, unsupported), tile =>
            {
                Assert.Empty(tile.Preview.CannedSamples);
                Assert.Contains("Unsupported recording schema 999", tile.Preview.Note);
            });
            Assert.Equal(futureBytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(temporary, recursive: true); }
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
    public void Transfer_function_evals_on_elapsed_recording()
    {
        var dataset = RunDatasetCatalog.Load(
            Path.Combine(FindRepoRoot(), "tests", "fixtures", "authoring", "tf", "vdc-elapsed", "run.json"));
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
                    new TransferFunctionAlgorithm("VDC", [0.5, 0.5], [1], 0.005, "filter"))),
            ],
            new CleanupPolicy(false, "DMM"));
        var results = FormulaDatasetEval.EvaluateProgram(draft, dataset.Run);
        Assert.Equal(8, results.Count);
        Assert.Equal(0.5, results[0].Value);
        Assert.Equal(0.5, results[1].Value);
        var preview = MetricPreviewBuilder.From(
            ((MetricNode)draft.Measure[0]).Metric,
            null,
            RunDatasetBinder.SeriesByMetric(dataset.Run));
        Assert.Equal(0.5, preview.CannedSamples[0]);
        Assert.DoesNotContain("needs Area", preview.Note ?? string.Empty, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("run-v0-legacy.json")]
    [InlineData("run-v1.json")]
    public void Unsupported_recording_is_rejected_without_changing_bytes(string fixture)
    {
        var path = Path.Combine(NewTempDir(), "run.json");
        File.Copy(Path.Combine(FindRepoRoot(), "tests", "fixtures", "schema", fixture), path);
        var before = File.ReadAllBytes(path);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => RunDatasetCatalog.Load(path));
        Assert.Contains("Unsupported", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
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
            if (dir.EnumerateFiles("dirs.proj").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate dirs.proj above '{AppContext.BaseDirectory}'.");
    }
}
