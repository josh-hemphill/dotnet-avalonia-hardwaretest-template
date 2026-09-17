using System.Globalization;
using System.Reflection;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    private static TestPlan BuildPlan(ProgramDraft draft)
    {
        var instruments = CreateInstruments(draft.Instruments);
        var dut = new HardwareDut
        {
            Name = "DUT",
            Family = string.IsNullOrWhiteSpace(draft.Sidecar.DutFamily) ? "generic" : draft.Sidecar.DutFamily,
        };

        var setupGroup = new TestGroupStep { Name = SetupGroupName };
        foreach (var action in draft.Setup)
        {
            setupGroup.ChildTestSteps.Add(CreateSetupStep(action, instruments, dut));
        }

        var suiteName = string.IsNullOrWhiteSpace(draft.Sidecar.DisplayName)
            ? draft.PlanId
            : draft.Sidecar.DisplayName!;
        var measureGroup = new TestGroupStep { Name = suiteName };
        foreach (var node in draft.Measure)
        {
            measureGroup.ChildTestSteps.Add(CreateMeasureStep(node, instruments));
        }

        var cleanupGroup = new TestGroupStep { Name = CleanupGroupName };
        if (draft.Cleanup.IncludeSafeShutdown)
        {
            var shutdown = new SafeShutdownStep { Name = "Safe Shutdown" };
            AssignInstrument(shutdown, ResolveInstrument(instruments, draft.Cleanup.InstrumentSlot));
            cleanupGroup.ChildTestSteps.Add(shutdown);
        }

        var plan = new TestPlan();
        if (setupGroup.ChildTestSteps.Count > 0)
        {
            plan.ChildTestSteps.Add(setupGroup);
        }

        if (measureGroup.ChildTestSteps.Count > 0)
        {
            plan.ChildTestSteps.Add(measureGroup);
        }

        if (cleanupGroup.ChildTestSteps.Count > 0)
        {
            plan.ChildTestSteps.Add(cleanupGroup);
        }

        return plan;
    }

    private static Dictionary<string, HardwareDmm> CreateInstruments(IReadOnlyList<InstrumentRef> refs)
    {
        var map = new Dictionary<string, HardwareDmm>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in refs)
        {
            map[slot.SlotName] = CreateInstrument(slot);
        }

        return map;
    }

    private static HardwareDmm CreateInstrument(InstrumentRef slot)
    {
        if (IsMockDmm(slot.TypeId))
        {
            return new MockDmmInstrument
            {
                Name = slot.SlotName,
                VisaAddress = slot.VisaAddress,
                ResourceName = slot.VisaAddress,
            };
        }

        throw new AuthoringWorkspaceException(
            $"{AuthoringCompileCodes.UnknownFunction}: instrument type '{slot.TypeId}' is not supported in this compile.");
    }

    private static bool IsMockDmm(string typeId)
        => typeId.Contains("MockDmm", StringComparison.OrdinalIgnoreCase)
           || string.Equals(typeId, typeof(MockDmmInstrument).FullName, StringComparison.Ordinal);

    private static ITestStep CreateSetupStep(
        SetupAction action,
        IReadOnlyDictionary<string, HardwareDmm> instruments,
        HardwareDut dut)
    {
        switch (action)
        {
            case IdentitySetup identity:
                {
                    var step = new IdentityCheckStep { Name = "Identity Check", Dut = dut };
                    AssignInstrument(step, ResolveInstrument(instruments, identity.InstrumentSlot));
                    OpenTapMixinAttach.AttachAnnotation(step);
                    return step;
                }
            case OperatorPromptSetup prompt:
                return new OperatorPromptStep { Name = prompt.Name, Message = prompt.Message };
            case OperatorInputSetup input:
                return new OperatorInputStep
                {
                    Name = input.Name,
                    Title = input.Title,
                    Message = input.Message,
                    StringFieldId = input.StringFieldId ?? string.Empty,
                    NumberFieldId = input.NumberFieldId ?? string.Empty,
                };
            default:
                throw new AuthoringWorkspaceException($"Unsupported setup action '{action.GetType().Name}'.");
        }
    }

    private static ITestStep CreateMeasureStep(
        MeasureNode node,
        IReadOnlyDictionary<string, HardwareDmm> instruments)
    {
        switch (node)
        {
            case MetricNode metric:
                return CreateMetricStep(metric.Metric, instruments);
            case RepeatNode repeat:
                {
                    var loop = new RepeatLoopStep { Name = "Repeat", Count = Math.Max(1, repeat.Count) };
                    foreach (var child in repeat.Children)
                    {
                        loop.ChildTestSteps.Add(CreateMeasureStep(child, instruments));
                    }

                    return loop;
                }
            case RawStepNode raw:
                return LoadRawStep(raw);
            default:
                throw new AuthoringWorkspaceException($"Unsupported measure node '{node.GetType().Name}'.");
        }
    }

    private static ITestStep CreateMetricStep(
        MetricDraft metric,
        IReadOnlyDictionary<string, HardwareDmm> instruments)
    {
        var functionId = metric.Source switch
        {
            MeasureSource measure => measure.FunctionId,
            AlgorithmSource algorithm => algorithm.AlgorithmId,
            _ => throw new AuthoringWorkspaceException($"Unsupported metric source '{metric.Source.GetType().Name}'."),
        };

        if (LooksLikeDialogName(functionId))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.DialogStep}: '{functionId}' is not an authoring function.");
        }

        var step = AuthoringFunctionCatalog.CreateStep(functionId);
        step.Name = metric.Name;
        ApplySource(step, metric.Source, instruments);
        PresentationAttach.Apply(step, metric);
        return step;
    }

    private static void ApplySource(
        ITestStep step,
        MetricSource source,
        IReadOnlyDictionary<string, HardwareDmm> instruments)
    {
        switch (source)
        {
            case MeasureSource measure:
                ApplySettings(step, measure.Settings);
                if (!string.IsNullOrWhiteSpace(measure.InstrumentSlot))
                {
                    AssignInstrument(step, ResolveInstrument(instruments, measure.InstrumentSlot));
                }

                break;
            case AlgorithmSource algorithm:
                ApplySettings(step, algorithm.Settings);
                if (AuthoringFunctionCatalog.TryGet(algorithm.AlgorithmId, out var spec) && spec.NeedsInstrument)
                {
                    var slot = instruments.Keys.FirstOrDefault()
                               ?? throw new AuthoringWorkspaceException(
                                   $"Algorithm '{algorithm.AlgorithmId}' needs an instrument slot.");
                    AssignInstrument(step, ResolveInstrument(instruments, slot));
                }

                break;
        }
    }

    private static HardwareDmm ResolveInstrument(
        IReadOnlyDictionary<string, HardwareDmm> instruments,
        string slotName)
    {
        if (instruments.TryGetValue(slotName, out var found))
        {
            return found;
        }

        throw new AuthoringWorkspaceException($"Unknown instrument slot '{slotName}'.");
    }

    private static void AssignInstrument(ITestStep step, HardwareDmm instrument)
    {
        var prop = step.GetType().GetProperty("Instrument", BindingFlags.Instance | BindingFlags.Public);
        if (prop is null || !prop.CanWrite)
        {
            return;
        }

        if (prop.PropertyType.IsInstanceOfType(instrument))
        {
            prop.SetValue(step, instrument);
        }
    }

    private static void ApplySettings(ITestStep step, IReadOnlyDictionary<string, string> settings)
    {
        foreach (var (key, value) in settings)
        {
            var prop = step.GetType().GetProperty(key, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (prop is null || !prop.CanWrite)
            {
                continue;
            }

            prop.SetValue(step, ConvertSetting(prop.PropertyType, value));
        }
    }

    private static object? ConvertSetting(Type type, string value)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string))
        {
            return value;
        }

        if (string.IsNullOrWhiteSpace(value) && Nullable.GetUnderlyingType(type) is not null)
        {
            return null;
        }

        if (target == typeof(bool))
        {
            return bool.Parse(value);
        }

        if (target == typeof(int))
        {
            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        if (target == typeof(double))
        {
            return double.Parse(value, CultureInfo.InvariantCulture);
        }

        return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    private static bool LooksLikeDialogName(string name)
        => name.Contains("Dialog", StringComparison.OrdinalIgnoreCase)
           || name.Contains("MessageBox", StringComparison.OrdinalIgnoreCase);

    private static ITestStep LoadRawStep(RawStepNode raw)
    {
        if (LooksLikeDialogName(raw.TypeName))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.DialogStep}: raw step '{raw.TypeName}' is not allowed.");
        }

        if (string.IsNullOrWhiteSpace(raw.XmlFragment))
        {
            throw new AuthoringWorkspaceException($"Raw step '{raw.TypeName}' has no XML fragment.");
        }

        var tmp = Path.Combine(Path.GetTempPath(), "ht-raw-" + Guid.NewGuid().ToString("N") + ".TapPlan");
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestPlan type="OpenTap.TestPlan">
              <Steps>
                {raw.XmlFragment}
              </Steps>
            </TestPlan>
            """;
        File.WriteAllText(tmp, xml);
        try
        {
            var plan = TestPlan.Load(tmp);
            if (plan.ChildTestSteps.Count != 1)
            {
                throw new AuthoringWorkspaceException(
                    $"Raw step '{raw.TypeName}' XML must deserialize to one root step.");
            }

            return plan.ChildTestSteps[0];
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
