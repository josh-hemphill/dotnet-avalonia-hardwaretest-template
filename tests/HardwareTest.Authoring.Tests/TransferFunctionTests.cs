using System.Globalization;
using System.Text.Json;
using HardwareTest.Authoring;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class TransferFunctionTests
{
    private const double AbsEpsilon = 1e-9;
    private const double RelEpsilon = 1e-6;

    [Fact]
    public void Filter_impulse_matches_checked_in_biquad_golden()
    {
        var golden = ReadVectorGolden("biquad.impulse.json");
        var actual = TransferFunctionFilter.Filter(golden.Numerator, golden.Denominator, golden.Input);
        AssertVectors(golden.Output, actual);
    }

    [Fact]
    public void FiltFilt_matches_checked_in_odd_pad_vector()
    {
        var golden = ReadVectorGolden("filtfilt.odd-pad.json");
        var actual = TransferFunctionFilter.FiltFilt(golden.Numerator, golden.Denominator, golden.Input);
        AssertVectors(golden.Output, actual);
    }

    [Fact]
    public void TimeBase_maps_null_elapsed_to_nan_without_dropping_rows()
    {
        StoredSample[] series =
        [
            new() { Channel = "VDC", Value = 1, ElapsedMs = 0 },
            new() { Channel = "VDC", Value = 2, ElapsedMs = null },
            new() { Channel = "VDC", Value = 3, ElapsedMs = 10 },
        ];
        var elapsed = TransferFunctionTimeBase.ElapsedMs(series);
        var values = TransferFunctionTimeBase.Values(series);
        Assert.Equal(3, elapsed.Count);
        Assert.Equal(3, values.Count);
        Assert.Equal(0, elapsed[0]);
        Assert.True(double.IsNaN(elapsed[1]));
        Assert.Equal(10, elapsed[2]);
        Assert.Equal([1d, 2d, 3d], values);
    }

    [Fact]
    public void RequireUniform_fails_empty_and_single_sample()
    {
        var empty = Assert.Throws<InvalidOperationException>(
            () => TransferFunctionGrid.RequireUniform([], 0.005));
        Assert.Contains("TF_GRID", empty.Message, StringComparison.Ordinal);
        var single = Assert.Throws<InvalidOperationException>(
            () => TransferFunctionGrid.RequireUniform([0], 0.005));
        Assert.Contains("TF_GRID", single.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireUniform_fails_irregular_dt_distinct_from_ts_mismatch()
    {
        var irregular = Assert.Throws<InvalidOperationException>(
            () => TransferFunctionGrid.RequireUniform([0, 5, 20], 0.01));
        Assert.Contains("irregular", irregular.Message, StringComparison.OrdinalIgnoreCase);
        var mismatch = Assert.Throws<InvalidOperationException>(
            () => TransferFunctionGrid.RequireUniform([0, 10, 20], 0.005));
        Assert.Contains("TsSeconds", mismatch.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireUniform_fails_nan_elapsed()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => TransferFunctionGrid.RequireUniform([0, double.NaN, 10], 0.005));
        Assert.Contains("NaN", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToSeries_missing_elapsed_keeps_length_and_fails_uniform()
    {
        var step = new ApplyTransferFunctionStep
        {
            Numerator = [0.5, 0.5],
            Denominator = [1],
            TsSeconds = 0.005,
            Method = "filter",
        };
        var ex = Assert.Throws<InvalidOperationException>(
            () => step.ApplyToSeries([1, 2, 3], [double.NaN, double.NaN, double.NaN]));
        Assert.Contains("TF_GRID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyToSeries_mixed_null_elapsed_fails_uniform()
    {
        var step = new ApplyTransferFunctionStep
        {
            Numerator = [1],
            Denominator = [1],
            TsSeconds = 0.005,
        };
        var elapsed = TransferFunctionTimeBase.ElapsedMs(
        [
            new StoredSample { Value = 1, ElapsedMs = 0 },
            new StoredSample { Value = 2, ElapsedMs = null },
            new StoredSample { Value = 3, ElapsedMs = 10 },
        ]);
        Assert.Equal(3, elapsed.Count);
        var ex = Assert.Throws<InvalidOperationException>(
            () => step.ApplyToSeries([1, 2, 3], elapsed));
        Assert.Contains("TF_GRID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_valid_model_maps_output_channel_key()
    {
        var imported = TfModelImporter.Load(Path.Combine(FixtureRoot(), "model.valid.json"));
        Assert.Equal("VDC.filt", imported.OutputChannelKey);
        Assert.Equal("VDC", imported.Algorithm.InputChannelKey);
        Assert.Equal([0.5, 0.5], imported.Algorithm.Numerator);
        Assert.Equal([1d], imported.Algorithm.Denominator);
        Assert.Equal(0.005, imported.Algorithm.TsSeconds);
        Assert.Equal("filter", imported.Algorithm.Method);
        var metric = new MetricDraft(
            "Transfer Function",
            imported.OutputChannelKey,
            PresentationRoles.Timeseries,
            "V",
            null,
            null,
            imported.Algorithm);
        Assert.Equal("VDC.filt", metric.ChannelKey);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0,\"numerator\":[1],\"denominator\":[1],\"method\":\"filter\",\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"schemaVersion\":1,\"numerator\":[1],\"denominator\":[1],\"method\":\"filter\",\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0.005,\"numerator\":[],\"denominator\":[1],\"method\":\"filter\",\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[],\"method\":\"filter\",\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[1],\"method\":\"filter\",\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\",\"extra\":1}")]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[1],\"method\":\"filter\",\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zi\"}")]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[1],\"method\":\"filter\",\"timeBase\":\"index\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[1],\"method\":\"filter\",\"timeBase\":\"timestamp\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[1],\"method\":\"filter\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[1],\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[1],\"method\":\"filter\",\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"schemaVersion\":2,\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[1],\"method\":\"filter\",\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[0],\"method\":\"filter\",\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    [InlineData("{\"schemaVersion\":1,\"tsSeconds\":0.005,\"numerator\":[1],\"denominator\":[1],\"method\":\"Filter\",\"timeBase\":\"elapsedMs\",\"inputChannelKey\":\"VDC\",\"outputChannelKey\":\"VDC.filt\",\"initialConditions\":\"zero\"}")]
    public void Import_fail_closed_on_illegal_documents(string json)
    {
        var path = Path.Combine(NewTempDir(), "bad.tf.json");
        File.WriteAllText(path, json);
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => TfModelImporter.Load(path));
        Assert.True(
            ex.Message.Contains(AuthoringCompileCodes.TfImport, StringComparison.Ordinal)
            || ex.Message.Contains(AuthoringCompileCodes.TfDenLeadingZero, StringComparison.Ordinal),
            ex.Message);
    }

    [Fact]
    public void Lower_filter_with_leading_zero_denominator_fails()
    {
        var ex = Assert.Throws<AuthoringWorkspaceException>(
            () => FormulaLowerer.Lower(new ExpressionAlgorithm(["VDC"], "filter([1],[0],VDC)"), null));
        Assert.Contains(AuthoringCompileCodes.TfDenLeadingZero, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Lower_filter_and_filtfilt_become_transfer_function_algorithms()
    {
        var filter = Assert.IsType<TransferFunctionAlgorithm>(
            FormulaLowerer.Lower(new ExpressionAlgorithm(["VDC"], "filter([0.5 0.5],[1],VDC)"), null));
        Assert.Equal("VDC", filter.InputChannelKey);
        Assert.Equal([0.5, 0.5], filter.Numerator);
        Assert.Equal([1d], filter.Denominator);
        Assert.Equal("filter", filter.Method);
        Assert.Equal(FormulaLowerer.DefaultTsSeconds, filter.TsSeconds);

        var filtfilt = Assert.IsType<TransferFunctionAlgorithm>(
            FormulaLowerer.Lower(new ExpressionAlgorithm(["VDC"], "filtfilt([0.5 0.5],[1],VDC)"), null));
        Assert.Equal("filtfilt", filtfilt.Method);
    }

    [Theory]
    [InlineData("mean(filter([1],[1],VDC))")]
    [InlineData("filter([1],[1],VDC)+1")]
    public void Nested_filter_fails_lower_not_expressions(string source)
    {
        var ex = Assert.Throws<AuthoringWorkspaceException>(
            () => FormulaLowerer.Lower(new ExpressionAlgorithm(["VDC"], source), new LimitSpec(null, null, 1.2)));
        Assert.Contains(AuthoringCompileCodes.FormulaNoLower, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Expressions", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preview_without_dataset_synthesizes_elapsed_clock()
    {
        var metric = new MetricDraft(
            "Filter",
            "VDC.filt",
            PresentationRoles.Timeseries,
            "V",
            null,
            null,
            new TransferFunctionAlgorithm("VDC", [1], [1], 0.005, "filter"));
        var preview = MetricPreviewBuilder.From(metric);
        Assert.NotEmpty(preview.CannedSamples);
        Assert.False(string.Equals(preview.Note, "TF_GRID", StringComparison.Ordinal));
        Assert.DoesNotContain("TF_GRID", preview.Note ?? string.Empty, StringComparison.Ordinal);
        Assert.All(preview.CannedSamples, value => Assert.False(double.IsNaN(value)));
    }

    [Fact]
    public void Preview_and_eval_fail_when_elapsed_is_missing()
    {
        var tf = new TransferFunctionAlgorithm("VDC", [0.5, 0.5], [1], 0.005, "filter");
        var metric = new MetricDraft("Filter", "VDC.filt", PresentationRoles.Timeseries, "V", null, null, tf);
        var recorded = new Dictionary<string, IReadOnlyList<StoredSample>>(StringComparer.OrdinalIgnoreCase)
        {
            ["VDC"] =
            [
                new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 1 },
                new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 2 },
            ],
        };
        var preview = MetricPreviewBuilder.From(metric, null, recorded);
        Assert.Empty(preview.CannedSamples);
        Assert.Contains(AuthoringCompileCodes.TfGrid, preview.Note, StringComparison.Ordinal);

        var mixed = new Dictionary<string, IReadOnlyList<StoredSample>>(StringComparer.OrdinalIgnoreCase)
        {
            ["VDC"] =
            [
                new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 1, ElapsedMs = 0 },
                new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 2, ElapsedMs = null },
            ],
        };
        Assert.Equal(2, TransferFunctionTimeBase.ElapsedMs(mixed["VDC"]).Count);
        var mixedPreview = MetricPreviewBuilder.From(metric, null, mixed);
        Assert.Empty(mixedPreview.CannedSamples);
        Assert.Contains(AuthoringCompileCodes.TfGrid, mixedPreview.Note, StringComparison.Ordinal);

        var draft = TfDraft(tf);
        var run = new TestRunRecord
        {
            PlanId = "tf",
            Samples = recorded["VDC"].ToList(),
        };
        var eval = Assert.Throws<AuthoringWorkspaceException>(() => FormulaDatasetEval.EvaluateProgram(draft, run));
        Assert.Contains(AuthoringCompileCodes.TfGrid, eval.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_program_applies_leaky_integrator_on_elapsed_recording()
    {
        var dataset = RunDatasetCatalog.Load(Path.Combine(FixtureRoot(), "vdc-elapsed", "run.json"));
        var tf = new TransferFunctionAlgorithm("VDC", [1], [1, -0.5], 0.005, "filter");
        var results = FormulaDatasetEval.EvaluateProgram(TfDraft(tf), dataset.Run);
        Assert.Equal(8, results.Count);
        Assert.Equal(1, results[0].Value);
        Assert.Equal(0.5, results[1].Value);
        Assert.Equal(0.25, results[2].Value);
        Assert.Equal(0, results[0].ElapsedMs);
        Assert.Equal(5, results[1].ElapsedMs);
        Assert.Equal("VDC.filt", results[0].EffectiveMetricKey);
    }

    private static ProgramDraft TfDraft(TransferFunctionAlgorithm tf)
        => new(
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
                    tf)),
            ],
            new CleanupPolicy(false, "DMM"));

    private static (double[] Numerator, double[] Denominator, double[] Input, double[] Output) ReadVectorGolden(
        string fileName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureRoot(), fileName)));
        return (
            ReadArray(document.RootElement.GetProperty("numerator")),
            ReadArray(document.RootElement.GetProperty("denominator")),
            ReadArray(document.RootElement.GetProperty("input")),
            ReadArray(document.RootElement.GetProperty("output")));
    }

    private static double[] ReadArray(JsonElement element)
        => element.EnumerateArray().Select(item => item.GetDouble()).ToArray();

    private static void AssertVectors(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var scale = Math.Max(1, Math.Abs(expected[i]));
            Assert.True(
                Math.Abs(expected[i] - actual[i]) <= Math.Max(AbsEpsilon, RelEpsilon * scale),
                $"index {i}: expected {expected[i].ToString(CultureInfo.InvariantCulture)} actual {actual[i].ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static string FixtureRoot()
        => Path.Combine(FindRepoRoot(), "tests", "fixtures", "authoring", "tf");

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-tf-" + Guid.NewGuid().ToString("N"));
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
