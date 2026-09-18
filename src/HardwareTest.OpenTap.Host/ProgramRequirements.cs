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

/// Sidecar requiredFields tokens. Known ids map to ProgramRequirements bools.
public static class RequiredFieldIds
{
    public const string Serial = "serial";
    public const string PartNumber = "partNumber";
    public const string Revision = "revision";
    public const string Operator = "operator";

    public static IReadOnlyList<string> Known { get; } =
        [Serial, PartNumber, Revision, Operator];

    public static bool IsKnown(string? id)
    {
        var token = id?.Trim();
        return !string.IsNullOrWhiteSpace(token)
               && Known.Any(known => string.Equals(known, token, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> FromSidecar(ProgramSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        if (sidecar.RequiredFields is not null)
        {
            return NormalizeAll(sidecar.RequiredFields);
        }

        var derived = new List<string>();
        AddIf(derived, sidecar.RequireSerial == true, Serial);
        AddIf(derived, sidecar.RequirePartNumber == true, PartNumber);
        AddIf(derived, sidecar.RequireRevision == true, Revision);
        AddIf(derived, sidecar.RequireOperator == true, Operator);
        return derived;
    }

    public static void Apply(ProgramSidecar sidecar, IReadOnlyList<string> fields)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        var normalized = NormalizeAll(fields);
        sidecar.RequiredFields = [.. normalized];
        sidecar.RequireSerial = Contains(normalized, Serial);
        sidecar.RequirePartNumber = Contains(normalized, PartNumber);
        sidecar.RequireRevision = Contains(normalized, Revision);
        sidecar.RequireOperator = Contains(normalized, Operator);
    }

    public static bool Contains(IReadOnlyList<string> fields, string id)
        => fields.Any(field => string.Equals(field, id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> NormalizeAll(IEnumerable<string?> fields)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in fields)
        {
            var token = raw?.Trim();
            if (string.IsNullOrWhiteSpace(token) || !seen.Add(token))
            {
                continue;
            }

            var known = Known.FirstOrDefault(id => string.Equals(id, token, StringComparison.OrdinalIgnoreCase));
            result.Add(known ?? token);
        }

        return result;
    }

    private static void AddIf(List<string> fields, bool include, string id)
    {
        if (include)
        {
            fields.Add(id);
        }
    }
}
