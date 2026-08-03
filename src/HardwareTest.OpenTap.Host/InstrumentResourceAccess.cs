using System.Reflection;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Reads/writes common VISA resource properties on OpenTAP instruments.
public static class InstrumentResourceAccess
{
    // Prefer VisaAddress (SCPI convention), then ResourceName / Address fallbacks.
    private static readonly string[] ResourcePropertyNames = ["VisaAddress", "ResourceName", "Address"];

    public static string GetResource(Instrument instrument)
    {
        foreach (var name in ResourcePropertyNames)
        {
            var prop = instrument.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (prop?.PropertyType == typeof(string) && prop.CanRead)
            {
                return prop.GetValue(instrument) as string ?? string.Empty;
            }
        }

        return string.Empty;
    }

    public static bool TrySetResource(Instrument instrument, string resource)
    {
        foreach (var name in ResourcePropertyNames)
        {
            var prop = instrument.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (prop?.PropertyType == typeof(string) && prop.CanWrite)
            {
                prop.SetValue(instrument, resource);
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<Instrument> CollectFromPlan(TestPlan plan)
    {
        var seen = new HashSet<Instrument>(ReferenceEqualityComparer.Instance);
        foreach (var step in FlattenSteps(plan))
        {
            foreach (var instrument in CollectFromStep(step))
            {
                if (seen.Add(instrument))
                {
                    yield return instrument;
                }
            }
        }
    }

    private static IEnumerable<Instrument> CollectFromStep(ITestStep step)
    {
        foreach (var prop in step.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!typeof(Instrument).IsAssignableFrom(prop.PropertyType) || !prop.CanRead)
            {
                continue;
            }

            if (prop.GetValue(step) is Instrument instrument)
            {
                yield return instrument;
            }
        }
    }

    private static IEnumerable<ITestStep> FlattenSteps(ITestStepParent parent)
    {
        foreach (var child in parent.ChildTestSteps)
        {
            yield return child;
            foreach (var nested in FlattenSteps(child))
            {
                yield return nested;
            }
        }
    }
}
