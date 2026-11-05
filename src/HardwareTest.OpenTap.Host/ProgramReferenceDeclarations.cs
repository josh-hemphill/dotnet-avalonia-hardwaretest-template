using System.Text.Json;

namespace HardwareTest.OpenTap.Host;

/// Unknown reference semantics must not be dropped by tolerant sidecar deserialization.
public static class ProgramReferenceDeclarations
{
    private static readonly HashSet<string> RuleFields = new(StringComparer.Ordinal)
    {
        "ruleId", "metricKey", "producerStepId", "referenceKey", "unit", "comparison", "tolerance"
    };

    public static void RequireSupportedJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The program sidecar must be an object.");
        var found = false;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.Name.Equals("referenceRules", StringComparison.OrdinalIgnoreCase)) continue;
            if (found || property.Name != "referenceRules")
                throw new InvalidDataException("The referenceRules declaration is duplicated or uses an unsupported name.");
            found = true;
            if (property.Value.ValueKind == JsonValueKind.Null) continue;
            if (property.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Reference criteria must be an array.");
            foreach (var rule in property.Value.EnumerateArray())
            {
                if (rule.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("A reference criterion must be an object.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in rule.EnumerateObject())
                    if (!RuleFields.Contains(field.Name) || !names.Add(field.Name))
                        throw new InvalidDataException($"Unsupported or duplicate reference field '{field.Name}'. Restore a supported sidecar before Run.");
            }
        }
    }
}
