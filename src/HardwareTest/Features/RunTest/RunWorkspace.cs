namespace HardwareTest.Features.RunTest;

/// Mutually exclusive Run-page workspace. Preparation and interaction temporarily overlay this.
public sealed record RunWorkspace(string Key)
{
    public static RunWorkspace Steps { get; } = new("steps");
    public static RunWorkspace Details { get; } = new("details");
    public static RunWorkspace Chart { get; } = new("chart");
}
