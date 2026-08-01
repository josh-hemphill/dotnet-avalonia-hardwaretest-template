namespace HardwareTest.Core.Hardware;

/// Detects demo/mock VISA resources that must not run when UseMockVisa is false.
public static class MockResourceGuard
{
    public const string MockPrefix = "MOCK::";

    public static bool LooksLikeMockResource(string? resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            return false;
        }

        return resource.Trim().StartsWith(MockPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMockInstrumentType(string? typeName)
        => string.Equals(typeName, "MockDmmInstrument", StringComparison.OrdinalIgnoreCase);
}
