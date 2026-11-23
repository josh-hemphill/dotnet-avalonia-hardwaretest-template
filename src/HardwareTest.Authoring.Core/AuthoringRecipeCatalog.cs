using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using InstrumentComponents.Drivers.Catalog;

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
        new(AuthoringRecipeIds.Identity, "Identity Check", "Check", "Confirm DUT serial against the instrument."),
        new(AuthoringRecipeIds.Prompt, "Operator Prompt", "Operator action", "Pause for the operator to confirm a message."),
        new(AuthoringRecipeIds.Input, "Operator Input", "Operator action", "Typed fixture / operator fields."),
        new(AuthoringRecipeIds.Acquire, "Acquire Voltage", "Measure", "Waveform timeseries (ChannelKey VDC)."),
        new(AuthoringRecipeIds.MeanGte, "Mean GTE", "Check", "Scalar threshold on VDC.mean. Requires LimitSpec."),
        new(AuthoringRecipeIds.BandScalar, "Publish Band Scalar", "Check", "Passband with Limit low / Limit high."),
        new(AuthoringRecipeIds.SeriesCompliance, "Publish Series Compliance", "Check", "In-band percent passband."),
        new(AuthoringRecipeIds.Repeat, "Repeat Loop", "Flow", "Wraps the selected supported measurement or loop."),
        new(AuthoringRecipeIds.Formula, "Formula…", "Check", "Deployment checks: mean(x) with threshold and top-level filter/filtfilt. Other expressions are saved for exploration and excluded from deployment."),
        new(AuthoringRecipeIds.TransferFunction, "Transfer function…", "Check", "Discrete SISO IIR from numerator/denominator/Ts."),
        new(AuthoringRecipeIds.StationHealth, "Report Station Health", "Check", "cal.dc.offset scalar with limits."),
        new(AuthoringRecipeIds.Shutdown, "Safe Shutdown", "Flow", "Cleanup Safe Shutdown on selected instrument slots."),
        .. global::HardwareTest.OpenTap.Plugins.Instruments.DeviceDriverCatalog.OperationNames
            .Select(operation => new AuthoringRecipe("device:" + operation, FriendlyOperation(operation), "Device operation", "Use an advertised " + operation + " capability on the selected vendor device."))
    ];

    private static string FriendlyOperation(string operation) => System.Text.RegularExpressions.Regex.Replace(operation, "(?<=[a-z0-9])(?=[A-Z])", " ");

    public static bool PaletteContainsDialog()
        => Palette.Any(LooksLikeDialog);

    public static ProgramDraft Apply(ProgramDraft draft, string recipeId, string? instrumentSlot = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeId);
        if (LooksLikeDialogId(recipeId))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.DialogStep}: '{recipeId}' is not an authoring recipe.");
        }

        if (recipeId.StartsWith("device:", StringComparison.Ordinal))
        {
            var operation = recipeId[7..];
            if (!AuthoringFunctionCatalog.TryGet("Device." + operation, out _))
                throw new AuthoringWorkspaceException("Choose a supported device operation.");
            var selected = draft.Instruments.FirstOrDefault(instrument => instrument.SlotName == (instrumentSlot ?? DefaultSlot(draft)));
            var arguments = selected?.TypeId == global::HardwareTest.OpenTap.Plugins.Instruments.DeviceDriverCatalog.RegisteredTypeId
                ? global::HardwareTest.OpenTap.Plugins.Instruments.DeviceDriverCatalog.ArgumentTemplate(AuthoringInstrumentCatalog.DeviceBindingFor(selected), operation)
                : global::HardwareTest.OpenTap.Plugins.Instruments.DeviceOperationRequests.Template(operation);
            var role = Enum.Parse<CatalogOperation>(operation).Describe().Result is ReplyKind.Trace or ReplyKind.Samples
                ? PresentationDisplayRoles.Timeseries : PresentationDisplayRoles.Scalar;
            var metric = new MetricDraft(FriendlyOperation(operation), operation, role, "", null, null,
                new MeasureSource(instrumentSlot ?? DefaultSlot(draft), "Device." + operation,
                    new Dictionary<string, string> { ["Arguments"] = arguments }));
            return WithMeasure(draft, metric);
        }
        return recipeId.Trim().ToLowerInvariant() switch
        {
            AuthoringRecipeIds.TestGroup => draft,
            AuthoringRecipeIds.Identity => WithSetup(draft, new IdentitySetup(instrumentSlot ?? DefaultSlot(draft))),
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
            AuthoringRecipeIds.Acquire => WithMeasure(draft, AcquireMetric(instrumentSlot ?? DefaultSlot(draft))),
            AuthoringRecipeIds.MeanGte => WithMeasure(draft, MeanGteMetric(instrumentSlot ?? DefaultSlot(draft))),
            AuthoringRecipeIds.BandScalar => WithMeasure(draft, BandScalarMetric()),
            AuthoringRecipeIds.SeriesCompliance => WithMeasure(draft, SeriesComplianceMetric()),
            AuthoringRecipeIds.Repeat => WrapLastInRepeat(draft),
            AuthoringRecipeIds.Formula => WithMeasure(draft, FormulaMetric(draft)),
            AuthoringRecipeIds.TransferFunction => WithMeasure(draft, TransferFunctionMetric(draft)),
            AuthoringRecipeIds.StationHealth => WithMeasure(draft, StationHealthMetric()),
            AuthoringRecipeIds.Shutdown => EnableShutdown(draft),
            _ => throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.UnknownFunction}: recipe '{recipeId}' is not in the authoring palette."),
        };
    }

    public static void EnsureScalarLimits(ProgramDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        foreach (var metric in EnumerateMetrics(draft.Measure))
        {
            AuthoringCriteria.Validate(metric);
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
                case SectionNode section:
                    foreach (var nested in EnumerateMetrics(section.Children)) yield return nested;
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
    {
        var used = PlanCompiler.AuthoringOutputChannels(draft.Measure).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var channel = AuthoringSequenceOperations.UniqueChannel(metric.ChannelKey, used);
        metric = AuthoringSequenceOperations.WithOutputChannel(metric, channel);
        var updated = draft with { Measure = [.. draft.Measure, new MetricNode(metric)] };
        PlanCompiler.RequireUniqueNewOutputs(draft, updated);
        return updated;
    }

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

    private static ProgramDraft EnableShutdown(ProgramDraft draft)
    {
        var slots = draft.Cleanup.InstrumentSlots
            .Where(slot => !string.IsNullOrWhiteSpace(slot))
            .Select(slot => slot.Trim())
            .ToList();
        if (slots.Count == 0)
        {
            slots.Add(DefaultSlot(draft));
        }

        return draft with
        {
            Cleanup = draft.Cleanup with { IncludeSafeShutdown = true, InstrumentSlots = slots },
        };
    }

    private static string DefaultSlot(ProgramDraft draft)
        => draft.Instruments.FirstOrDefault()?.SlotName ?? "DMM";

    private static MetricDraft AcquireMetric(string instrumentSlot)
        => new(
            "Acquire VDC",
            "VDC",
            PresentationDisplayRoles.Timeseries,
            "V",
            null,
            null,
            new MeasureSource(
                instrumentSlot,
                AuthoringFunctionIds.BasicAcquireVoltage,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SampleCount"] = "32",
                    ["IntervalMs"] = "5",
                    ["Channel"] = "VDC",
                }));

    private static MetricDraft MeanGteMetric(string instrumentSlot)
        => new(
            "Mean GTE",
            "VDC.mean",
            PresentationDisplayRoles.Scalar,
            "V",
            new LimitSpec(null, null, 1.2),
            null,
            new AlgorithmSource(
                AuthoringFunctionIds.BasicMeanGte,
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SampleCount"] = "8",
                })
            { InstrumentSlot = instrumentSlot });

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
                }));

    private static MetricDraft FormulaMetric(ProgramDraft draft)
    {
        var keys = PlanCompiler.AuthoringOutputChannels(draft.Measure)
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
        var keys = PlanCompiler.AuthoringOutputChannels(draft.Measure)
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
                string.Empty,
                AuthoringFunctionIds.BasicReportStationHealth,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                }));
}
