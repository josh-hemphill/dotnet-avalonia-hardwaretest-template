using OpenTap;

namespace HardwareTest.Authoring;

/// Bind to the library's ScpiInstrument contract, never Basic's IDmmInstrument.
internal static class AuthoringLibraryLifecycle
{
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
