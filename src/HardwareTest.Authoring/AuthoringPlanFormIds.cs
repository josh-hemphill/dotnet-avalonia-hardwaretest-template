using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// New human-authored plans need IDs that the operator can discover as custom programs.
/// Imported examples and programmatic factory construction keep their original identities.
internal static class AuthoringPlanFormIds
{
    internal static string Automatic(string proposed)
    {
        try { BenchProgramHandoff.RequireCustomPlanId(proposed); return proposed; }
        catch (InvalidOperationException) { return "custom-" + proposed.TrimStart('.'); }
    }

    internal static void RequireCustom(string proposed) => BenchProgramHandoff.RequireCustomPlanId(proposed.Trim());
}
