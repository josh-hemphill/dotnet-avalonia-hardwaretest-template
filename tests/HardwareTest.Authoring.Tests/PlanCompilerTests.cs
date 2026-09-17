using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[CollectionDefinition("AuthoringOpenTap", DisableParallelization = true)]
public sealed class AuthoringOpenTapCollection;

[Collection("AuthoringOpenTap")]
public sealed class PlanCompilerTests
{
    [Fact]
    public void Plugin_search_does_not_add_the_visa_project_directory()
    {
        AuthoringPluginSearch.Search();

        Assert.DoesNotContain(
            PluginManager.DirectoriesToSearch,
            dir => dir.Contains(
                       $"{Path.DirectorySeparatorChar}HardwareTest.OpenTap.Plugins.Visa{Path.DirectorySeparatorChar}",
                       StringComparison.OrdinalIgnoreCase)
                   || dir.EndsWith(
                       $"{Path.DirectorySeparatorChar}HardwareTest.OpenTap.Plugins.Visa",
                       StringComparison.OrdinalIgnoreCase));
        Assert.False(AuthoringFunctionCatalog.TryGet("Visa.Dmm", out _));
    }

    [Fact]
    public void Compile_sample_equivalent_passes_contract_without_missing_limits()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "sample.TapPlan");
        new PlanCompiler().Save(SampleEquivalentDraft("sample"), path);

        var report = PlanContractValidator.ValidateFile(
            path,
            new PlanContractOptions { ExcludeVisaAdapter = true });
        Assert.False(report.HasErrors, string.Join("; ", report.Findings.Select(f => $"{f.Code}: {f.Message}")));
        Assert.DoesNotContain(report.Findings, f => f.Code == PlanContractValidator.Codes.MissingLimits);
        Assert.DoesNotContain(File.ReadAllText(path), "DialogStep", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(File.ReadAllText(path), "VisaDmmInstrument", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decompile_sample_tap_plan_exposes_vdc_channel_keys()
    {
        var path = Path.Combine(FindRepoRoot(), "plans", "opentap", "sample.TapPlan");
        var draft = new PlanCompiler().Load(path);

        var keys = EnumerateChannelKeys(draft.Measure).ToArray();
        Assert.Contains("VDC", keys);
        Assert.Contains("VDC.mean", keys);
        Assert.Contains(draft.Setup, s => s is IdentitySetup);
        Assert.Contains(draft.Setup, s => s is OperatorPromptSetup);
        Assert.Contains(draft.Setup, s => s is OperatorInputSetup);
        Assert.True(draft.Cleanup.IncludeSafeShutdown);
        Assert.Contains(
            draft.Instruments,
            i => i.TypeId.Contains("MockDmm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compile_decompile_preserves_channel_role_limits_and_history()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "roundtrip.TapPlan");
        var original = SampleEquivalentDraft("roundtrip");
        new PlanCompiler().Save(original, path);
        var loaded = new PlanCompiler().Load(path);

        var mean = loaded.Measure.OfType<MetricNode>()
            .Select(n => n.Metric)
            .Single(m => m.ChannelKey == "VDC.mean");
        Assert.Equal(PresentationDisplayRoles.Scalar, mean.DisplayRole);
        Assert.Equal("V", mean.YUnit);
        Assert.NotNull(mean.Limits);
        Assert.Equal(1.2, mean.Limits!.Threshold);
        Assert.NotNull(mean.History);
        Assert.True(mean.History!.Enabled);
        Assert.Equal(5, mean.History.WatchPercent);
        Assert.Equal(10, mean.History.AlertPercent);

        var acquire = loaded.Measure.OfType<MetricNode>()
            .Select(n => n.Metric)
            .Single(m => m.ChannelKey == "VDC");
        Assert.Equal(PresentationDisplayRoles.Timeseries, acquire.DisplayRole);
    }

    [Fact]
    public void Compile_passband_with_limits_has_no_missing_limits_warning()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "passband.TapPlan");
        var draft = MinimalDraft(
            "passband",
            [
                new MetricNode(new MetricDraft(
                    "Band",
                    "rail.mean",
                    PresentationDisplayRoles.Passband,
                    "V",
                    new LimitSpec(1.1, 1.4, null),
                    null,
                    new AlgorithmSource(
                        AuthoringFunctionIds.BasicPublishBandScalar,
                        [],
                        new Dictionary<string, string>
                        {
                            ["MetricName"] = "rail.mean",
                            ["Value"] = "1.25",
                            ["Unit"] = "V",
                        }))),
            ]);
        new PlanCompiler().Save(draft, path);

        var report = PlanContractValidator.ValidateFile(
            path,
            new PlanContractOptions { ExcludeVisaAdapter = true });
        Assert.DoesNotContain(report.Findings, f => f.Code == PlanContractValidator.Codes.MissingLimits);
        Assert.False(report.HasErrors, string.Join("; ", report.Findings.Select(f => $"{f.Code}: {f.Message}")));
    }

    [Fact]
    public void Compile_repeat_wrapping_metric_round_trips_count_and_channel()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "repeat.TapPlan");
        var draft = MinimalDraft(
            "repeat",
            [
                new RepeatNode(
                    3,
                    [
                        new MetricNode(new MetricDraft(
                            "Acquire VDC",
                            "VDC",
                            PresentationDisplayRoles.Timeseries,
                            "V",
                            null,
                            null,
                            new MeasureSource(
                                "DMM",
                                AuthoringFunctionIds.BasicAcquireVoltage,
                                new Dictionary<string, string>
                                {
                                    ["SampleCount"] = "4",
                                    ["Channel"] = "VDC",
                                }))),
                    ]),
            ]);
        new PlanCompiler().Save(draft, path);
        var loaded = new PlanCompiler().Load(path);
        var repeat = Assert.Single(loaded.Measure.OfType<RepeatNode>());
        Assert.Equal(3, repeat.Count);
        var metric = Assert.Single(repeat.Children.OfType<MetricNode>());
        Assert.Equal("VDC", metric.Metric.ChannelKey);
    }

    [Fact]
    public void Duplicate_channel_key_fails_before_save()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "dup.TapPlan");
        var metric = new MetricDraft(
            "A",
            "dup",
            PresentationDisplayRoles.Timeseries,
            "V",
            null,
            null,
            new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>()));
        var draft = MinimalDraft("dup", [new MetricNode(metric), new MetricNode(metric with { Name = "B" })]);

        var ex = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Contains(AuthoringCompileCodes.DuplicateChannelKey, ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Raw_hang_forever_round_trips_as_raw_step()
    {
        AuthoringPluginSearch.Search();
        var hang = new HangForeverStep { Name = "Hang Forever" };
        var tmp = Path.Combine(NewTempDir(), "hang-src.TapPlan");
        var source = new TestPlan();
        source.ChildTestSteps.Add(hang);
        source.Save(tmp);
        var xml = System.Xml.Linq.XDocument.Load(tmp)
            .Descendants()
            .First(e => e.Name.LocalName == "TestStep")
            .ToString(System.Xml.Linq.SaveOptions.DisableFormatting);

        var dir = NewTempDir();
        var path = Path.Combine(dir, "raw.TapPlan");
        var draft = MinimalDraft(
            "raw",
            [new RawStepNode(typeof(HangForeverStep).FullName!, xml)]);
        new PlanCompiler().Save(draft, path);
        var loaded = new PlanCompiler().Load(path);
        var raw = Assert.Single(loaded.Measure.OfType<RawStepNode>());
        Assert.Contains("HangForeverStep", raw.TypeName, StringComparison.Ordinal);
        Assert.Contains("HangForeverStep", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.DoesNotContain("DialogStep", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadAll_template_workspace_includes_sample()
    {
        var workspace = AuthoringWorkspaceLoader.Load(Path.Combine(FindRepoRoot(), "plans", "opentap"));
        var drafts = new PlanCompiler().LoadAll(workspace);
        Assert.Contains(drafts.Programs, p => p.PlanId == "sample");
        var sample = drafts.Programs.Single(p => p.PlanId == "sample");
        Assert.Contains("VDC", EnumerateChannelKeys(sample.Measure));
    }

    [Fact]
    public void Mean_formula_lowers_to_mean_gte()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "formula.TapPlan");
        var draft = MinimalDraft(
            "formula",
            [
                new MetricNode(new MetricDraft(
                    "Acquire VDC",
                    "VDC",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new MeasureSource(
                        "DMM",
                        AuthoringFunctionIds.BasicAcquireVoltage,
                        new Dictionary<string, string> { ["Channel"] = "VDC" }))),
                new MetricNode(new MetricDraft(
                    "Formula",
                    "VDC.mean",
                    PresentationDisplayRoles.Scalar,
                    "V",
                    new LimitSpec(null, null, 1.2),
                    null,
                    new ExpressionAlgorithm(["VDC"], "mean(VDC)"))),
            ]);
        new PlanCompiler().Save(draft, path);
        var loaded = new PlanCompiler().Load(path);
        var mean = loaded.Measure.OfType<MetricNode>().Select(n => n.Metric)
            .Single(m => m.ChannelKey == "VDC.mean");
        var algorithm = Assert.IsType<AlgorithmSource>(mean.Source);
        Assert.Equal(AuthoringFunctionIds.BasicMeanGte, algorithm.AlgorithmId);
        var xml = File.ReadAllText(path);
        Assert.Contains("MeanGteStep", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogStep", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("abs(VDC)")]
    [InlineData("mean(VDC)+1")]
    public void Unlowerable_formula_save_fails_closed(string source)
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "formula.TapPlan");
        var draft = MinimalDraft(
            "formula",
            [
                new MetricNode(new MetricDraft(
                    "Formula",
                    "VDC.mean",
                    PresentationDisplayRoles.Scalar,
                    "V",
                    new LimitSpec(null, null, 1.2),
                    null,
                    new ExpressionAlgorithm(["VDC"], source))),
            ]);
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Contains(AuthoringCompileCodes.FormulaNoLower, ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Transfer_function_save_emits_apply_step_and_round_trips()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "tf.TapPlan");
        var draft = MinimalDraft(
            "tf",
            [
                new MetricNode(new MetricDraft(
                    "Acquire VDC",
                    "VDC",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new MeasureSource(
                        "DMM",
                        AuthoringFunctionIds.BasicAcquireVoltage,
                        new Dictionary<string, string>
                        {
                            ["SampleCount"] = "8",
                            ["IntervalMs"] = "5",
                            ["Channel"] = "VDC",
                        }))),
                new MetricNode(new MetricDraft(
                    "Filter",
                    "VDC.filt",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new TransferFunctionAlgorithm("VDC", [0.5, 0.5], [1], 0.005, "filter"))),
            ]);
        new PlanCompiler().Save(draft, path);
        var xml = File.ReadAllText(path);
        Assert.Contains("ApplyTransferFunctionStep", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogStep", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Expressions", xml, StringComparison.OrdinalIgnoreCase);
        AssertFiltfiltIsSibling(xml);

        var loaded = new PlanCompiler().Load(path);
        var tf = Assert.IsType<TransferFunctionAlgorithm>(
            loaded.Measure.OfType<MetricNode>().Select(n => n.Metric)
                .Single(m => m.ChannelKey == "VDC.filt").Source);
        Assert.Equal("VDC", tf.InputChannelKey);
        Assert.Equal([0.5, 0.5], tf.Numerator);
        Assert.Equal([1d], tf.Denominator);
        Assert.Equal(0.005, tf.TsSeconds);
        Assert.Equal("filter", tf.Method);
        Assert.Null(loaded.Measure.OfType<MetricNode>().Select(n => n.Metric)
            .Single(m => m.ChannelKey == "VDC.filt").Limits);
    }

    [Fact]
    public void Filter_formula_save_emits_apply_step()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "formula-tf.TapPlan");
        var draft = MinimalDraft(
            "formula-tf",
            [
                new MetricNode(new MetricDraft(
                    "Acquire VDC",
                    "VDC",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new MeasureSource(
                        "DMM",
                        AuthoringFunctionIds.BasicAcquireVoltage,
                        new Dictionary<string, string> { ["Channel"] = "VDC" }))),
                new MetricNode(new MetricDraft(
                    "Filter",
                    "VDC.filt",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new ExpressionAlgorithm(["VDC"], "filter([0.5 0.5],[1],VDC)"))),
            ]);
        new PlanCompiler().Save(draft, path);
        Assert.Contains("ApplyTransferFunctionStep", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Filtfilt_save_is_sibling_analyze_not_nested_in_acquire()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "filtfilt.TapPlan");
        var draft = MinimalDraft(
            "filtfilt",
            [
                new MetricNode(new MetricDraft(
                    "Acquire VDC",
                    "VDC",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new MeasureSource(
                        "DMM",
                        AuthoringFunctionIds.BasicAcquireVoltage,
                        new Dictionary<string, string> { ["Channel"] = "VDC" }))),
                new MetricNode(new MetricDraft(
                    "FiltFilt",
                    "VDC.filt",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new TransferFunctionAlgorithm("VDC", [0.5, 0.5], [1], 0.005, "filtfilt"))),
            ]);
        new PlanCompiler().Save(draft, path);
        var xml = File.ReadAllText(path);
        Assert.Contains("ApplyTransferFunctionStep", xml, StringComparison.Ordinal);
        AssertFiltfiltIsSibling(xml);
        var loaded = new PlanCompiler().Load(path);
        var tf = Assert.IsType<TransferFunctionAlgorithm>(
            loaded.Measure.OfType<MetricNode>().Select(n => n.Metric)
                .Single(m => m.ChannelKey == "VDC.filt").Source);
        Assert.Equal("filtfilt", tf.Method);
    }

    [Fact]
    public void Publish_timed_sample_without_elapsed_fails_tf_save()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "timed.TapPlan");
        var draft = MinimalDraft(
            "timed",
            [
                new MetricNode(new MetricDraft(
                    "Timed",
                    "VDC",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new MeasureSource(
                        "DMM",
                        AuthoringFunctionIds.BasicPublishTimedSample,
                        new Dictionary<string, string> { ["Channel"] = "VDC", ["Value"] = "1" }))),
                new MetricNode(new MetricDraft(
                    "Filter",
                    "VDC.filt",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new TransferFunctionAlgorithm("VDC", [1], [1], 0.005, "filter"))),
            ]);
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Contains(AuthoringCompileCodes.TfMissingElapsed, ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Chained_filter_formula_save_treats_lowered_sibling_as_elapsed_source()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "chain-tf.TapPlan");
        var draft = MinimalDraft(
            "chain-tf",
            [
                new MetricNode(new MetricDraft(
                    "Acquire VDC",
                    "VDC",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new MeasureSource(
                        "DMM",
                        AuthoringFunctionIds.BasicAcquireVoltage,
                        new Dictionary<string, string> { ["Channel"] = "VDC" }))),
                new MetricNode(new MetricDraft(
                    "Filter",
                    "VDC.filt",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new ExpressionAlgorithm(["VDC"], "filter([0.5 0.5],[1],VDC)"))),
                new MetricNode(new MetricDraft(
                    "Filter again",
                    "VDC.filt2",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new ExpressionAlgorithm(["VDC.filt"], "filtfilt([1],[1],VDC.filt)"))),
            ]);
        new PlanCompiler().Save(draft, path);
        var xml = File.ReadAllText(path);
        Assert.Equal(2, xml.Split("ApplyTransferFunctionStep", StringSplitOptions.None).Length - 1);
        var loaded = new PlanCompiler().Load(path);
        var second = Assert.IsType<TransferFunctionAlgorithm>(
            loaded.Measure.OfType<MetricNode>().Select(n => n.Metric)
                .Single(m => m.ChannelKey == "VDC.filt2").Source);
        Assert.Equal("VDC.filt", second.InputChannelKey);
        Assert.Equal("filtfilt", second.Method);
    }

    [Fact]
    public void Mean_formula_sibling_does_not_satisfy_tf_elapsed()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "mean-tf.TapPlan");
        var draft = MinimalDraft(
            "mean-tf",
            [
                new MetricNode(new MetricDraft(
                    "Acquire VDC",
                    "VDC",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new MeasureSource(
                        "DMM",
                        AuthoringFunctionIds.BasicAcquireVoltage,
                        new Dictionary<string, string> { ["Channel"] = "VDC" }))),
                new MetricNode(new MetricDraft(
                    "Mean",
                    "VDC.mean",
                    PresentationDisplayRoles.Scalar,
                    "V",
                    new LimitSpec(null, null, 1.2),
                    null,
                    new ExpressionAlgorithm(["VDC"], "mean(VDC)"))),
                new MetricNode(new MetricDraft(
                    "Filter",
                    "VDC.filt",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new TransferFunctionAlgorithm("VDC.mean", [1], [1], 0.005, "filter"))),
            ]);
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Contains(AuthoringCompileCodes.TfMissingElapsed, ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Transfer_function_save_does_not_add_limit_properties()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "tf-nolo.TapPlan");
        var draft = MinimalDraft(
            "tf-nolo",
            [
                new MetricNode(new MetricDraft(
                    "Filter",
                    "VDC.filt",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new TransferFunctionAlgorithm("VDC", [0.5, 0.5], [1], 0.005, "filter"))),
            ]);
        new PlanCompiler().Save(draft, path);
        var xml = File.ReadAllText(path);
        Assert.Contains("ApplyTransferFunctionStep", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<LimitLow", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<LimitHigh", xml, StringComparison.Ordinal);
    }

    private static void AssertFiltfiltIsSibling(string xml)
    {
        var document = System.Xml.Linq.XDocument.Parse(xml);
        var tf = document.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "TestStep"
                && ((string?)e.Attribute("type") ?? string.Empty)
                    .Contains("ApplyTransferFunctionStep", StringComparison.Ordinal));
        Assert.NotNull(tf);
        foreach (var ancestor in tf!.Ancestors())
        {
            if (ancestor.Name.LocalName != "TestStep")
            {
                continue;
            }

            var type = (string?)ancestor.Attribute("type") ?? string.Empty;
            Assert.DoesNotContain("AcquireVoltageStep", type, StringComparison.Ordinal);
            Assert.DoesNotContain("BitSweepAcquireStep", type, StringComparison.Ordinal);
        }
    }

    private static ProgramDraft SampleEquivalentDraft(string planId)
    {
        var settingsAcquire = new Dictionary<string, string>
        {
            ["SampleCount"] = "32",
            ["IntervalMs"] = "5",
            ["Channel"] = "VDC",
        };
        var settingsMean = new Dictionary<string, string>
        {
            ["SampleCount"] = "8",
            ["Threshold"] = "1.2",
        };

        return new ProgramDraft(
            planId,
            new ProgramSidecar
            {
                DisplayName = "Sample Hardware Suite (Demo)",
                DutFamily = "demo",
                RequireSerial = true,
                RequireOperator = true,
                RequirePartNumber = false,
                RequireRevision = false,
                ReportKinds = ["status", "certification"],
                DefaultReportKind = "status",
                SelectionIncludesCleanup = true,
            },
            [new InstrumentRef("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::INSTR0")],
            [
                new IdentitySetup("DMM"),
                new OperatorPromptSetup(
                    "Confirm Sweep Area Clear",
                    "Confirm the sweep area is clear of tools, then Continue."),
                new OperatorInputSetup(
                    "Install Sweep Fixture",
                    "Install Sweep Fixture",
                    "Install the voltage-sweep fixture, enter the fixture id and optional torque, then Continue.",
                    "fixtureId",
                    "fixtureTorqueNm"),
            ],
            [
                new MetricNode(new MetricDraft(
                    "Acquire VDC",
                    "VDC",
                    PresentationDisplayRoles.Timeseries,
                    "V",
                    null,
                    null,
                    new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, settingsAcquire))),
                new MetricNode(new MetricDraft(
                    "Mean GTE",
                    "VDC.mean",
                    PresentationDisplayRoles.Scalar,
                    "V",
                    new LimitSpec(null, null, 1.2),
                    new HistorySpec(true, 5, 10),
                    new AlgorithmSource(AuthoringFunctionIds.BasicMeanGte, ["VDC"], settingsMean))),
            ],
            new CleanupPolicy(true, "DMM"));
    }

    private static ProgramDraft MinimalDraft(string planId, IReadOnlyList<MeasureNode> measure)
        => new(
            planId,
            new ProgramSidecar
            {
                DisplayName = planId,
                DutFamily = "demo",
                RequireSerial = true,
                ReportKinds = ["status"],
                DefaultReportKind = "status",
                SelectionIncludesCleanup = true,
            },
            [new InstrumentRef("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::INSTR0")],
            [new IdentitySetup("DMM")],
            measure,
            new CleanupPolicy(true, "DMM"));

    private static IEnumerable<string> EnumerateChannelKeys(IReadOnlyList<MeasureNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case MetricNode metric:
                    yield return metric.Metric.ChannelKey;
                    break;
                case RepeatNode repeat:
                    foreach (var nested in EnumerateChannelKeys(repeat.Children))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-plancompiler-" + Guid.NewGuid().ToString("N"));
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
