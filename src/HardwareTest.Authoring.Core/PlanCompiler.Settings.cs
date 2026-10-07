namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    // Reuse the compiler's closed setting projection instead of introducing a property grid.
    internal static IReadOnlyDictionary<string, string> FunctionSettings(string functionId)
        => ReadSettings(AuthoringFunctionCatalog.CreateStep(functionId), includeEmpty: true);
}
