using System.Collections;
using System.Reflection;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Detects OpenTAP Repeat/Sweep flow steps and reads iteration totals via reflection.
public static class OpenTapLoopProgress
{
    private static readonly HashSet<string> LoopTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RepeatStep",
        "RepeatLoopStep",
        "SweepLoop",
        "SweepLoopRange",
        "SweepParameterStep",
        "SweepParameterRangeStep",
    };

    public static bool IsLoopStep(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        return LoopTypeNames.Contains(type.Name);
    }

    public static bool IsLoopStep(ITestStep? step) => IsLoopStep(step?.GetType());

    /// Best-effort total iterations (Count, SweepPoints, or enabled sweep rows).
    public static int? TryGetLoopTotal(ITestStep step)
    {
        foreach (var name in new[] { "Count", "SweepPoints", "Iterations" })
        {
            if (TryReadInt(step, name, out var value) && value > 0)
            {
                return value;
            }
        }

        if (TryReadEnabledRowCount(step, out var rows) && rows > 0)
        {
            return rows;
        }

        return null;
    }

    public static string? FormatIteration(int index, int? total)
    {
        if (index <= 0)
        {
            return null;
        }

        return total is > 0 ? $"{index}/{total}" : $"#{index}";
    }

    public static int CountEnabledLeaves(TestPlan plan)
    {
        var count = 0;
        foreach (var step in Flatten(plan))
        {
            if (!step.Enabled)
            {
                continue;
            }

            if (step.ChildTestSteps.Count == 0)
            {
                count++;
            }
        }

        return Math.Max(1, count);
    }

    public static ITestStep? FindStepById(TestPlan plan, Guid id)
        => Flatten(plan).FirstOrDefault(s => s.Id == id);

    private static bool TryReadInt(object target, string propertyName, out int value)
    {
        value = 0;
        var prop = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (prop is null || !prop.CanRead)
        {
            return false;
        }

        try
        {
            var raw = prop.GetValue(target);
            switch (raw)
            {
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = (int)l;
                    return true;
                case uint u:
                    value = (int)u;
                    return true;
                default:
                    return int.TryParse(Convert.ToString(raw), out value);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadEnabledRowCount(object target, out int count)
    {
        count = 0;
        foreach (var name in new[] { "SweepValues", "SweepRows", "Rows", "SweepParameters" })
        {
            var prop = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (prop?.GetValue(target) is not IEnumerable enumerable)
            {
                continue;
            }

            var enabled = 0;
            var any = 0;
            foreach (var row in enumerable)
            {
                any++;
                if (row is null)
                {
                    continue;
                }

                var enabledProp = row.GetType().GetProperty("Enabled", BindingFlags.Instance | BindingFlags.Public);
                if (enabledProp?.PropertyType == typeof(bool) && enabledProp.CanRead)
                {
                    if (enabledProp.GetValue(row) is true)
                    {
                        enabled++;
                    }
                }
                else
                {
                    enabled++;
                }
            }

            if (enabled > 0 || any > 0)
            {
                count = enabled > 0 ? enabled : any;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<ITestStep> Flatten(ITestStepParent parent)
    {
        foreach (var child in parent.ChildTestSteps)
        {
            yield return child;
            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }
}
