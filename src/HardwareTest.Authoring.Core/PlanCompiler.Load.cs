using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    private static readonly string[] SettingPropertyNames =
    [
        "SampleCount",
        "IntervalMs",
        "DwellLimitMs",
        "ScriptedValues",
        "PublishSummaries",
        "OffsetVolts",
        "AgeHours",
        "MaxAgeHours",
        "ResultSource",
        "MockCalPath",
        "Channel",
        "Threshold",
        "Value",
        "Unit",
        "MetricName",
        "BitCount",
        "SeriesCompliance",
        "FailWhenOutOfBand",
        "Values",
        "ElapsedMs",
        "EventName",
        "EventLabel",
        "EventValue",
        "Index",
        "OffsetLimitLow",
        "OffsetLimitHigh",
        "InputChannel",
        "ProducerStepId",
        "Numerator",
        "Denominator",
        "TsSeconds",
        "Method",
    ];

    private static ProgramDraft Decompile(
        string planId,
        TestPlan plan,
        ProgramSidecar sidecar,
        IReadOnlyDictionary<string, XElement> xmlById)
    {
        var instruments = new Dictionary<string, InstrumentRef>(StringComparer.OrdinalIgnoreCase);
        var setup = new List<SetupAction>();
        var measure = new List<MeasureNode>();
        var cleanup = new CleanupPolicy(false, string.Empty);

        foreach (var step in plan.ChildTestSteps)
        {
            Walk(step, instruments, setup, measure, ref cleanup, xmlById);
        }

        // Resources which OpenTAP cannot materialize still belong to the source document.
        foreach (var pair in xmlById.Where(pair => pair.Key.StartsWith("resource:", StringComparison.Ordinal)))
        {
            var resource = pair.Value;
            var typeId = (string?)resource.Attribute("type");
            if (string.IsNullOrWhiteSpace(typeId) || AuthoringInstrumentCatalog.TryGet(typeId, out _)) continue;
            var slot = resource.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value;
            if (string.IsNullOrWhiteSpace(slot)) continue;
            var address = new[] { "VisaAddress", "ResourceName", "Address" }
                .Select(name => resource.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            instruments[slot] = new InstrumentRef(slot, typeId, address)
            { OpaqueResourceXml = resource.ToString(SaveOptions.DisableFormatting) };
        }

        cleanup = AuthoringCleanup.FromPlan(cleanup, sidecar);

        return new ProgramDraft(
            planId,
            sidecar,
            instruments.Values.ToArray(),
            setup,
            measure,
            cleanup);
    }

    private static void Walk(
        ITestStep step,
        Dictionary<string, InstrumentRef> instruments,
        List<SetupAction> setup,
        List<MeasureNode> measure,
        ref CleanupPolicy cleanup,
        IReadOnlyDictionary<string, XElement> xmlById)
    {
        CollectInstrument(step, instruments);

        if (step is TestGroupStep)
        {
            foreach (var child in step.ChildTestSteps)
            {
                Walk(child, instruments, setup, measure, ref cleanup, xmlById);
            }

            return;
        }

        if (step is RepeatLoopStep repeat)
        {
            var children = new List<MeasureNode>();
            foreach (var child in repeat.ChildTestSteps)
            {
                children.Add(DecompileMeasure(child, instruments, xmlById));
            }

            measure.Add(new RepeatNode(repeat.Count, children) { NodeId = step.Id });
            return;
        }

        if (OpenTapStepKinds.IsIdentity(step))
        {
            setup.Add(new IdentitySetup(ImportedInstrumentSlot(step, xmlById)) { NodeId = step.Id });
            return;
        }

        if (step is OperatorPromptStep prompt)
        {
            setup.Add(new OperatorPromptSetup(prompt.Name, prompt.Message) { NodeId = step.Id });
            return;
        }

        if (step is OperatorInputStep input)
        {
            setup.Add(new OperatorInputSetup(
                input.Name,
                input.Title,
                input.Message,
                string.IsNullOrWhiteSpace(input.StringFieldId) ? null : input.StringFieldId,
                string.IsNullOrWhiteSpace(input.NumberFieldId) ? null : input.NumberFieldId)
            { NodeId = step.Id });
            return;
        }

        if (OpenTapStepKinds.IsSafeShutdown(step))
        {
            var slots = cleanup.InstrumentSlots.ToList();
            var slot = ImportedInstrumentSlot(step, xmlById);
            if (!string.IsNullOrWhiteSpace(slot)
                && !slots.Any(existing => string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase)))
            {
                slots.Add(slot);
            }

            cleanup = cleanup with { IncludeSafeShutdown = true, InstrumentSlots = slots };
            return;
        }

        measure.Add(DecompileMeasure(step, instruments, xmlById));
    }

    private static MeasureNode DecompileMeasure(
        ITestStep step,
        Dictionary<string, InstrumentRef> instruments,
        IReadOnlyDictionary<string, XElement> xmlById)
    {
        CollectInstrument(step, instruments);

        if (OpenTapStepKinds.IsApplyTransferFunction(step) && step is ApplyTransferFunctionStep tfStep)
        {
            return new MetricNode(ToTransferFunctionMetric(tfStep)) { NodeId = step.Id };
        }

        if (step is RepeatLoopStep repeat)
        {
            var children = new List<MeasureNode>();
            foreach (var child in repeat.ChildTestSteps)
            {
                children.Add(DecompileMeasure(child, instruments, xmlById));
            }

            return new RepeatNode(repeat.Count, children) { NodeId = step.Id };
        }

        if (step is TestGroupStep)
        {
            // Nested groups inside Repeat become a raw wrapper so children are not stripped.
            return ToRaw(step, xmlById);
        }

        if (AuthoringFunctionCatalog.TryGetByStep(step, out var spec))
        {
            return new MetricNode(ToMetricDraft(step, spec, xmlById)) { NodeId = step.Id };
        }

        return ToRaw(step, xmlById);
    }

    private static MetricDraft ToMetricDraft(ITestStep step, AuthoringFunctionSpec spec, IReadOnlyDictionary<string, XElement> xmlById)
    {
        var hints = OpenTapPresentation.TryReadMixin(step);
        var settings = ReadSettings(step);
        var slot = ImportedInstrumentSlot(step, xmlById);
        if (string.IsNullOrWhiteSpace(slot) && xmlById.TryGetValue(step.Id.ToString(), out var stepXml))
        {
            slot = stepXml.Elements().FirstOrDefault(e => e.Name.LocalName == "Instrument")?.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? string.Empty;
        }
        MetricSource source;
        if (spec.IsAlgorithm)
        {
            var inputs = step is ChannelAverageStep average ? new[] { average.InputChannel } : Array.Empty<string>();
            source = new AlgorithmSource(spec.Id, inputs, settings)
            {
                InstrumentSlot = spec.NeedsInstrument ? slot : null,
            };
        }
        else
        {
            source = new MeasureSource(slot, spec.Id, settings);
        }

        return new MetricDraft(
            step.Name,
            hints?.ChannelKey ?? settings.GetValueOrDefault("Channel", string.Empty),
            hints?.DisplayRole ?? string.Empty,
            hints?.YUnit ?? settings.GetValueOrDefault("Unit", string.Empty),
            ReadLimits(step),
            hints is null
                ? null
                : new HistorySpec(hints.HistoryEnabled, hints.HistoryWatchPercent, hints.HistoryAlertPercent),
            source);
    }

    private static RawStepNode ToRaw(ITestStep step, IReadOnlyDictionary<string, XElement> xmlById)
    {
        var typeName = step.GetType().FullName ?? step.GetType().Name;
        if (!xmlById.TryGetValue(step.Id.ToString(), out var element))
        {
            return new RawStepNode(typeName, string.Empty) { NodeId = step.Id };
        }

        return new RawStepNode(typeName, element.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
    }

    private static IReadOnlyDictionary<string, string> ReadSettings(ITestStep step, bool includeEmpty = false)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var type = step.GetType();
        foreach (var name in SettingPropertyNames)
        {
            var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (prop is null || !prop.CanRead)
            {
                continue;
            }

            var raw = prop.GetValue(step);
            if (raw is null && !includeEmpty)
            {
                continue;
            }

            if (raw is double[] vector)
            {
                map[name] = string.Join(",", vector.Select(v => v.ToString(CultureInfo.InvariantCulture)));
                continue;
            }

            if (AuthoringCriteria.IsRuntimeLimit(name)) continue;
            map[name] = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return map;
    }

    private static MetricDraft ToTransferFunctionMetric(ApplyTransferFunctionStep step)
    {
        var hints = OpenTapPresentation.TryReadMixin(step);
        return new MetricDraft(
            step.Name,
            hints?.ChannelKey ?? step.Channel,
            hints?.DisplayRole ?? string.Empty,
            hints?.YUnit ?? string.Empty,
            null,
            hints is null
                ? null
                : new HistorySpec(hints.HistoryEnabled, hints.HistoryWatchPercent, hints.HistoryAlertPercent),
            new TransferFunctionAlgorithm(
                step.InputChannel,
                step.Numerator ?? [1],
                step.Denominator ?? [1],
                step.TsSeconds,
                string.IsNullOrWhiteSpace(step.Method) ? "filter" : step.Method));
    }

    private static LimitSpec? ReadLimits(ITestStep step)
    {
        var low = ReadNullableDouble(step, "LimitLow") ?? ReadNullableDouble(step, "OffsetLimitLow");
        var high = ReadNullableDouble(step, "LimitHigh") ?? ReadNullableDouble(step, "OffsetLimitHigh");
        var threshold = ReadNullableDouble(step, "Threshold");
        if (low is null && high is null && threshold is null)
        {
            return null;
        }

        return new LimitSpec(low, high, threshold);
    }

    private static double? ReadNullableDouble(ITestStep step, string name)
    {
        var prop = step.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (prop is null || !prop.CanRead)
        {
            return null;
        }

        var raw = prop.GetValue(step);
        if (raw is null)
        {
            return null;
        }

        if (raw is double d)
        {
            return d;
        }

        if (double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static void CollectInstrument(ITestStep step, Dictionary<string, InstrumentRef> instruments)
    {
        var prop = step.GetType().GetProperty("Instrument", BindingFlags.Instance | BindingFlags.Public);
        if (prop is null || !prop.CanRead)
        {
            return;
        }

        if (prop.GetValue(step) is not Instrument instrument)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(instrument.Name) ? instrument.GetType().Name : instrument.Name;
        if (instruments.ContainsKey(name))
        {
            return;
        }

        var visa = ReadInstrumentAddress(instrument);
        var typeId = instrument.GetType().FullName ?? instrument.GetType().Name;
        instruments[name] = AuthoringInstrumentCatalog.TryGet(typeId, out var adapter)
            ? adapter.Serialize(instrument) : new InstrumentRef(name, typeId, visa);
    }

    private static string InstrumentSlotName(ITestStep step)
    {
        var prop = step.GetType().GetProperty("Instrument", BindingFlags.Instance | BindingFlags.Public);
        if (prop?.GetValue(step) is Instrument instrument && !string.IsNullOrWhiteSpace(instrument.Name))
        {
            return instrument.Name;
        }

        return string.Empty;
    }

    private static string ReadInstrumentAddress(Instrument instrument)
    {
        foreach (var name in new[] { "VisaAddress", "ResourceName", "Address" })
        {
            var prop = instrument.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var value = prop?.GetValue(instrument) as string;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static Dictionary<string, XElement> IndexStepXml(string tapPlanPath)
    {
        var map = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        XDocument document;
        try
        {
            document = XDocument.Load(tapPlanPath);
        }
        catch
        {
            return map;
        }

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "TestStep"))
        {
            var id = (string?)element.Attribute("Id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            map[id] = element;
        }

        var resourceIndex = 0;
        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "Instrument" && e.Attribute("type") is not null))
        {
            map[$"resource:{resourceIndex++}"] = element;
        }

        return map;
    }
}
