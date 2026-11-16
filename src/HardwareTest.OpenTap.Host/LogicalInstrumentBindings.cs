using System.Reflection;
using System.Xml.Linq;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Restores one runtime instance per authored slot after OpenTAP reads inline instrument copies.
/// Matching names never discard a conflicting type, address or serialized configuration.
internal static class LogicalInstrumentBindings
{
    public static void Canonicalize(TestPlan plan, string sourcePath)
    {
        var bindings = StepRuntimeBindings(plan).ToArray();
        if (bindings.Length < 2) return;
        var bySlot = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        Dictionary<Guid, XElement>? sourceSteps = null;
        foreach (var binding in bindings)
        {
            var slot = binding.Instrument.Name?.Trim();
            if (string.IsNullOrWhiteSpace(slot)) continue;
            if (!bySlot.TryGetValue(slot, out var first)) { bySlot.Add(slot, binding); continue; }
            if (ReferenceEquals(first.Instrument, binding.Instrument)) continue;
            if (first.Instrument.GetType() != binding.Instrument.GetType()
                || !string.Equals(InstrumentResourceAccess.GetResource(first.Instrument), InstrumentResourceAccess.GetResource(binding.Instrument), StringComparison.Ordinal))
                throw Conflict(slot);
            sourceSteps ??= ReadSourceSteps(sourcePath);
            var firstConfiguration = Configuration(first, sourceSteps, slot);
            var nextConfiguration = Configuration(binding, sourceSteps, slot);
            if (!XNode.DeepEquals(firstConfiguration, nextConfiguration)) throw Conflict(slot);
            if (!binding.Property.CanWrite)
                throw new InvalidOperationException($"Instrument slot '{slot}' has independent read-only bindings. Recompile the program with shared supported instrument bindings.");
            binding.Property.SetValue(binding.Step, first.Instrument);
        }
    }

    private sealed record Binding(ITestStep Step, PropertyInfo Property, Instrument Instrument);

    private static IEnumerable<Binding> StepRuntimeBindings(ITestStepParent parent)
    {
        foreach (var step in parent.ChildTestSteps)
        {
            foreach (var property in step.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.CanRead && property.GetIndexParameters().Length == 0
                    && typeof(Instrument).IsAssignableFrom(property.PropertyType)
                    && property.GetValue(step) is Instrument instrument)
                    yield return new Binding(step, property, instrument);
            }
            foreach (var nested in StepRuntimeBindings(step)) yield return nested;
        }
    }

    private static Dictionary<Guid, XElement> ReadSourceSteps(string path)
    {
        var document = XDocument.Load(path);
        var result = new Dictionary<Guid, XElement>();
        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "TestStep"))
        {
            if (!Guid.TryParse((string?)element.Attribute("Id"), out var id)) continue;
            if (!result.TryAdd(id, element))
                throw new InvalidOperationException("The compiled program contains ambiguous step identities. Recompile it before binding instruments.");
        }
        return result;
    }

    private static XElement Configuration(Binding binding, IReadOnlyDictionary<Guid, XElement> sourceSteps, string slot)
    {
        if (!sourceSteps.TryGetValue(binding.Step.Id, out var source)) throw Conflict(slot);
        var resources = source.Elements().Where(element => element.Name.LocalName == binding.Property.Name
            && element.Attribute("type") is not null).ToArray();
        if (resources.Length != 1) throw Conflict(slot);
        var owned = new XElement(resources[0]);
        // The binding property's name does not describe the instrument's configuration.
        owned.Name = "InstrumentConfiguration";
        foreach (var attribute in owned.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.LocalName == "type"))
            if (attribute.Value.StartsWith("emb:", StringComparison.Ordinal)) attribute.Value = attribute.Value[4..];
        return owned;
    }

    private static InvalidOperationException Conflict(string slot)
        => new($"Instrument slot '{slot}' has conflicting or unverifiable inline bindings. Use one type, resource address and configuration for that logical slot, then compile the complete program again.");
}
