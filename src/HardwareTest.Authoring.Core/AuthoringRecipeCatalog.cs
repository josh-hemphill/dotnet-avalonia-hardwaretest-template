using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;

namespace HardwareTest.Authoring;

/// Getting-started recipe ids (no Dialog / Hang Forever).
public static class AuthoringRecipeIds
{
    public const string TestGroup = "group";
    public const string Identity = "identity";
    public const string Prompt = "prompt";
    public const string Input = "input";
    public const string Acquire = "acquire";
    public const string MeanGte = "mean";
    public const string BandScalar = "band";
    public const string SeriesCompliance = "series";
    public const string Repeat = "repeat";
    public const string Formula = "formula";
    public const string TransferFunction = "tf";
    public const string StationHealth = "station-health";
    public const string Shutdown = "shutdown";
}

/// One palette entry matching a getting-started test type.
public sealed record AuthoringRecipe(string Id, string Title, string Category, string Summary)
{
    public string ListLabel => $"{Category} — {Title}";
}

/// Closed recipe picker for metric-first authoring. Compiler writes Presentation on apply.
public static class AuthoringRecipeCatalog
{
    public static IReadOnlyList<AuthoringRecipe> Palette { get; } =
    [
        new(AuthoringRecipeIds.TestGroup, "Test Group", "Structure", "Setup / measure / Cleanup groups are written on Save."),
        new(AuthoringRecipeIds.Identity, "Identity Check", "Identity", "Confirm DUT serial against the instrument."),
        new(AuthoringRecipeIds.Prompt, "Operator Prompt", "Operator", "In-panel confirm. Never OpenTAP Dialog."),
        new(AuthoringRecipeIds.Input, "Operator Input", "Operator", "Typed fixture / operator fields."),
        new(AuthoringRecipeIds.Acquire, "Acquire Voltage", "Measure", "Waveform timeseries (ChannelKey VDC)."),
        new(AuthoringRecipeIds.MeanGte, "Mean GTE", "Analyze", "Scalar threshold on VDC.mean. Requires LimitSpec."),
        new(AuthoringRecipeIds.BandScalar, "Publish Band Scalar", "Analyze", "Passband with Limit low / Limit high."),
        new(AuthoringRecipeIds.SeriesCompliance, "Publish Series Compliance", "Analyze", "In-band percent passband."),
        new(AuthoringRecipeIds.Repeat, "Repeat Loop", "Flow", "Wraps the last measure node in RepeatNode."),
        new(AuthoringRecipeIds.Formula, "Formula…", "Analyze", "MATLAB-flavored subset. mean(x) lowers to Mean GTE. filter(b,a,x) lowers to IIR."),
        new(AuthoringRecipeIds.TransferFunction, "Transfer function…", "Analyze", "Discrete SISO IIR from numerator/denominator/Ts."),
        new(AuthoringRecipeIds.StationHealth, "Report Station Health", "Station", "cal.dc.offset scalar with limits."),
        new(AuthoringRecipeIds.Shutdown, "Safe Shutdown", "Safety", "Cleanup Safe Shutdown on the DMM slot."),
    ];

    public static bool PaletteContainsDialog()
        => Palette.Any(LooksLikeDialog);

    public static ProgramDraft CreateProgram(string planId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        return new ProgramDraft(
            planId.Trim(),
            new ProgramSidecar
            {
                DisplayName = planId.Trim(),
                DutFamily = "generic",
                RequireSerial = true,
                ReportKinds = ["status"],
                DefaultReportKind = "status",
                SelectionIncludesCleanup = true,
            },
            [new InstrumentRef("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::INSTR0")],
            [new IdentitySetup("DMM")],
            [],
            new CleanupPolicy(true, "DMM"));
    }

    public static ProgramDraft Apply(ProgramDraft draft, string recipeId)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeId);
        if (LooksLikeDialogId(recipeId))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.DialogStep}: '{recipeId}' is not an authoring recipe.");
        }

        return recipeId.Trim().ToLowerInvariant() switch
        {
            AuthoringRecipeIds.TestGroup => draft,
            AuthoringRecipeIds.Identity => WithSetup(draft, new IdentitySetup(DefaultSlot(draft))),
            AuthoringRecipeIds.Prompt => WithSetup(
                draft,
                new OperatorPromptSetup("Operator Prompt", "Confirm the fixture is seated, then Continue.")),
            AuthoringRecipeIds.Input => WithSetup(
                draft,
                new OperatorInputSetup(
                    "Operator Input",
                    "Operator Input",
                    "Enter the fixture id, then Continue.",
                    "fixtureId",
                    null)),
            AuthoringRecipeIds.Acquire => WithMeasure(draft, AcquireMetric()),
            AuthoringRecipeIds.MeanGte => WithMeasure(draft, MeanGteMetric()),
            AuthoringRecipeIds.BandScalar => WithMeasure(draft, BandScalarMetric()),
            AuthoringRecipeIds.SeriesCompliance => WithMeasure(draft, SeriesComplianceMetric()),
            AuthoringRecipeIds.Repeat => WrapLastInRepeat(draft),
            AuthoringRecipeIds.Formula => WithMeasure(draft, FormulaMetric(draft)),
            AuthoringRecipeIds.TransferFunction => WithMeasure(draft, TransferFunctionMetric(draft)),
            AuthoringRecipeIds.StationHealth => WithMeasure(draft, StationHealthMetric()),
            AuthoringRecipeIds.Shutdown => draft with { Cleanup = new CleanupPolicy(true, DefaultSlot(draft)) },
            _ => throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.UnknownFunction}: recipe '{recipeId}' is not in the authoring palette."),
        };
    }

    public static void EnsureScalarLimits(ProgramDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        foreach (var metric in EnumerateMetrics(draft.Measure))
        {
            if (!IsBandRole(metric.DisplayRole))
            {
                continue;
            }

            if (!HasLimit(metric.Limits))
            {
                throw new AuthoringWorkspaceException(
                    $"{AuthoringCompileCodes.MissingLimits}: '{metric.ChannelKey}' ({metric.DisplayRole}) requires LimitSpec.");
            }
        }
    }

    public static IEnumerable<MetricDraft> EnumerateMetrics(IReadOnlyList<MeasureNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case MetricNode metric:
                    yield return metric.Metric;
                    break;
                case RepeatNode repeat:
                    foreach (var nested in EnumerateMetrics(repeat.Children))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    internal static bool LooksLikeDialog(AuthoringRecipe recipe)
        => LooksLikeDialogId(recipe.Id) || LooksLikeDialogId(recipe.Title);

    private static bool LooksLikeDialogId(string value)
        => value.Contains("Dialog", StringComparison.OrdinalIgnoreCase)
           || value.Contains("MessageBox", StringComparison.OrdinalIgnoreCase)
           || value.Contains("HangForever", StringComparison.OrdinalIgnoreCase);

    private static ProgramDraft WithSetup(ProgramDraft draft, SetupAction action)
    {
        if (action is IdentitySetup identity
            && draft.Setup.OfType<IdentitySetup>().Any(existing =>
                string.Equals(existing.InstrumentSlot, identity.InstrumentSlot, StringComparison.OrdinalIgnoreCase)))
        {
            return draft;
        }

        return draft with { Setup = [.. draft.Setup, action] };
    }

    private static ProgramDraft WithMeasure(ProgramDraft draft, MetricDraft metric)
        => draft with { Measure = [.. draft.Measure, new MetricNode(metric)] };

    private static ProgramDraft WrapLastInRepeat(ProgramDraft draft)
    {
        if (draft.Measure.Count == 0)
        {
            return draft with { Measure = [new RepeatNode(2, [])] };
        }

        var last = draft.Measure[^1];
        if (last is RepeatNode repeat)
        {
            return draft with
            {
                Measure = [.. draft.Measure.SkipLast(1), repeat with { Count = Math.Max(1, repeat.Count) }],
            };
        }

        return draft with
        {
            Measure = [.. draft.Measure.SkipLast(1), new RepeatNode(2, [last])],
        };
    }

    private static string DefaultSlot(ProgramDraft draft)
        => draft.Instruments.FirstOrDefault()?.SlotName ?? "DMM";

    private static MetricDraft AcquireMetric()
        => new(
            "Acquire VDC",
            "VDC",
            PresentationDisplayRoles.Timeseries,
            "V",
            null,
            null,
            new MeasureSource(
                "DMM",
                AuthoringFunctionIds.BasicAcquireVoltage,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SampleCount"] = "32",
                    ["IntervalMs"] = "5",
                    ["Channel"] = "VDC",
                }));

    private static MetricDraft MeanGteMetric()
        => new(
            "Mean GTE",
            "VDC.mean",
            PresentationDisplayRoles.Scalar,
            "V",
            new LimitSpec(null, null, 1.2),
            null,
            new AlgorithmSource(
                AuthoringFunctionIds.BasicMeanGte,
                ["VDC"],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SampleCount"] = "8",
                    ["Threshold"] = "1.2",
                }));

    private static MetricDraft BandScalarMetric()
        => new(
            "Band Scalar",
            "rail.mean",
            PresentationDisplayRoles.Passband,
            "V",
            new LimitSpec(1.1, 1.4, null),
            null,
            new AlgorithmSource(
                AuthoringFunctionIds.BasicPublishBandScalar,
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MetricName"] = "rail.mean",
                    ["Value"] = "1.25",
                    ["Unit"] = "V",
                }));

    private static MetricDraft SeriesComplianceMetric()
        => new(
            "Series Compliance",
            "series.inband.pct",
            PresentationDisplayRoles.Passband,
            "%",
            new LimitSpec(1.1, 1.4, null),
            null,
            new AlgorithmSource(
                AuthoringFunctionIds.BasicPublishSeriesCompliance,
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Values"] = "1.20,1.22,1.21",
                    ["LimitLow"] = "1.1",
                    ["LimitHigh"] = "1.4",
                }));

    private static MetricDraft FormulaMetric(ProgramDraft draft)
    {
        var keys = EnumerateMetrics(draft.Measure)
            .Select(m => m.ChannelKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var input = keys.FirstOrDefault(key => string.Equals(key, "VDC", StringComparison.OrdinalIgnoreCase))
                    ?? keys.FirstOrDefault()
                    ?? "VDC";
        return new(
            "Formula",
            $"{input}.mean",
            PresentationDisplayRoles.Scalar,
            "V",
            new LimitSpec(null, null, 1.2),
            null,
            new ExpressionAlgorithm([input], $"mean({input})"));
    }

    private static MetricDraft TransferFunctionMetric(ProgramDraft draft)
    {
        var keys = EnumerateMetrics(draft.Measure)
            .Select(m => m.ChannelKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var input = keys.FirstOrDefault(key => string.Equals(key, "VDC", StringComparison.OrdinalIgnoreCase))
                    ?? keys.FirstOrDefault()
                    ?? "VDC";
        return new(
            "Transfer Function",
            $"{input}.filt",
            PresentationDisplayRoles.Timeseries,
            "V",
            null,
            null,
            new TransferFunctionAlgorithm(
                input,
                [0.5, 0.5],
                [1],
                FormulaLowerer.DefaultTsSeconds,
                "filter"));
    }

    private static MetricDraft StationHealthMetric()
        => new(
            "Station Health",
            "cal.dc.offset",
            PresentationDisplayRoles.Scalar,
            "V",
            new LimitSpec(-0.01, 0.01, null),
            null,
            new MeasureSource(
                "DMM",
                AuthoringFunctionIds.BasicReportStationHealth,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OffsetLimitLow"] = "-0.01",
                    ["OffsetLimitHigh"] = "0.01",
                }));

    private static bool IsBandRole(string displayRole)
        => string.Equals(displayRole, PresentationDisplayRoles.Scalar, StringComparison.OrdinalIgnoreCase)
           || string.Equals(displayRole, PresentationDisplayRoles.Passband, StringComparison.OrdinalIgnoreCase)
           || string.Equals(displayRole, PresentationRoles.Scalar, StringComparison.OrdinalIgnoreCase)
           || string.Equals(displayRole, PresentationRoles.Passband, StringComparison.OrdinalIgnoreCase);

    private static bool HasLimit(LimitSpec? limits)
        => limits is not null
           && (limits.Low is not null || limits.High is not null || limits.Threshold is not null);
}
