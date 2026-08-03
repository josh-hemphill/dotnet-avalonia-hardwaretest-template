namespace HardwareTest.OpenTap.Host;

/// Declares which Operator Session fields a program requires before Run.
public sealed class ProgramRequirements
{
    public bool RequireSerial { get; init; } = true;
    public bool RequirePartNumber { get; init; }
    public bool RequireRevision { get; init; }
    public bool RequireOperator { get; init; }

    /// Sample / demo programs require serial + technician.
    public static ProgramRequirements Sample { get; } = new()
    {
        RequireSerial = true,
        RequireOperator = true,
    };

    public static ProgramRequirements FromFamily(string? family)
        => string.Equals(family, "demo", StringComparison.OrdinalIgnoreCase)
            ? Sample
            : new ProgramRequirements { RequireSerial = true, RequireOperator = true };

    /// Merges DUT metadata hints from a loaded plan into requirements.
    public ProgramRequirements WithDutHints(bool hasPartNumberField, bool hasRevisionField)
        => new()
        {
            RequireSerial = RequireSerial,
            RequirePartNumber = RequirePartNumber || hasPartNumberField,
            RequireRevision = RequireRevision || hasRevisionField,
            RequireOperator = RequireOperator,
        };
}
