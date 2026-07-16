namespace HardwareTest.OpenTap.Host;

/// Declares which Operator Session fields a program requires before Run.
public sealed class ProgramRequirements
{
    public bool RequireSerial { get; init; } = true;
    public bool RequirePartNumber { get; init; }
    public bool RequireRevision { get; init; }
    public bool RequireOperator { get; init; }

    public static ProgramRequirements Sample { get; } = new();

    public static ProgramRequirements FromFamily(string? family)
        => string.Equals(family, "demo", StringComparison.OrdinalIgnoreCase)
            ? Sample
            : new ProgramRequirements { RequireSerial = true };
}
