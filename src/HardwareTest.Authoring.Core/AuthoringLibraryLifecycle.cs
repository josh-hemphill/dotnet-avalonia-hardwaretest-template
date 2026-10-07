using System.Xml;
using System.Xml.Linq;
using OpenTap;

namespace HardwareTest.Authoring;

/// Bind to the library's ScpiInstrument contract, never Basic's IDmmInstrument.
internal static class AuthoringLibraryLifecycle
{
    internal const string ReimportMessage = "LIBRARY_LIFECYCLE_REIMPORT: Library identity and cleanup source is preserved, but its phases cannot be compiled from raw rows. Prepare the library in Environment, then reimport the original compiled plan to restore setup and cleanup order.";

    internal static IEnumerable<RawStepNode> OpaqueLifecycleNodes(IEnumerable<MeasureNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is RawStepNode raw && ContainsLifecycle(raw)) yield return raw;
            if (node is RepeatNode repeat)
                foreach (var child in OpaqueLifecycleNodes(repeat.Children)) yield return child;
        }
    }

    private static bool ContainsLifecycle(RawStepNode raw)
    {
        if (IsLifecycleType(raw.TypeName)) return true;
        try
        {
            // Opaque disabled/groups/repeats retain their children only in original XML.
            // Detect lifecycle descendants before any raw payload can be emitted into Measure.
            return XElement.Parse(raw.XmlFragment).DescendantsAndSelf().Any(element =>
                element.Name.LocalName == "TestStep" && IsLifecycleType((string?)element.Attribute("type")));
        }
        catch (XmlException) { return false; } // Malformed raw XML is rejected by compilation itself.
    }

    private static bool IsLifecycleType(string? type)
    {
        if (type?.StartsWith("emb:", StringComparison.Ordinal) == true) type = type[4..];
        return type is "InstrumentComponents.OpenTap.IdentityQueryStep" or "InstrumentComponents.OpenTap.SafeShutdownStep";
    }

    public static Guid CleanupId(Guid policyId, string slot, string firstSlot)
        => slot == firstSlot ? policyId : new Guid(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(policyId.ToString("N") + "|" + slot.ToUpperInvariant()))[..16]);

    public static ITestStep Create(Instrument resource, bool identity)
    {
        var name = "InstrumentComponents.OpenTap." + (identity ? "IdentityQueryStep" : "SafeShutdownStep");
        var type = resource.GetType().Assembly.GetType(name)
            ?? throw new AuthoringWorkspaceException($"INSTRUMENT_INCOMPATIBLE: library package does not provide '{name}'.");
        var step = (ITestStep)Activator.CreateInstance(type)!;
        var property = type.GetProperty("Instrument");
        if (property is null || !property.PropertyType.IsInstanceOfType(resource))
            throw new AuthoringWorkspaceException($"INSTRUMENT_INCOMPATIBLE: '{name}' cannot bind '{resource.GetType().FullName}'.");
        property.SetValue(step, resource);
        return step;
    }
}
