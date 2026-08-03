namespace HardwareTest.Core.Hardware;

public sealed class VisaResourceParseResult
{
    public required string Interface { get; init; }
    public string Detail { get; init; } = string.Empty;
    public bool LooksLikeAlias { get; init; }
    public bool SupportsMessageQuery { get; init; }
}

/// Pure VISA resource-string heuristics (no I/O).
public static class VisaResourceParser
{
    private static readonly HashSet<string> MessageQueryInterfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "USB", "TCPIP", "GPIB", "ASRL", "MOCK",
    };

    private static readonly string[] KnownPrefixes =
    [
        "TCPIP", "USB", "GPIB", "ASRL", "PXI", "VXI", "FIREWIRE", "MOCK",
    ];

    /// Parse interface family, a short detail snippet, alias guess, and *IDN? safety heuristic.
    public static VisaResourceParseResult Parse(string? resource)
    {
        var trimmed = resource?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new VisaResourceParseResult
            {
                Interface = "Other",
                LooksLikeAlias = false,
                SupportsMessageQuery = false,
            };
        }

        var iface = DetectInterface(trimmed);
        var looksLikeAlias = iface == "Other" && !trimmed.Contains("::", StringComparison.Ordinal);
        var detail = BuildDetail(trimmed, iface, looksLikeAlias);

        return new VisaResourceParseResult
        {
            Interface = iface,
            Detail = detail,
            LooksLikeAlias = looksLikeAlias,
            SupportsMessageQuery = MessageQueryInterfaces.Contains(iface),
        };
    }

    /// Split a standard *IDN? CSV into manufacturer / model / serial / firmware.
    public static (string? Manufacturer, string? Model, string? Serial, string? Firmware, string Summary) FormatIdn(
        string? idnRaw)
    {
        var raw = idnRaw?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null, null, null, string.Empty);
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        var manufacturer = parts.Length > 0 ? parts[0] : null;
        var model = parts.Length > 1 ? parts[1] : null;
        var serial = parts.Length > 2 ? parts[2] : null;
        var firmware = parts.Length > 3 ? parts[3] : null;

        var summaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(manufacturer))
        {
            summaryParts.Add(manufacturer);
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            summaryParts.Add(model);
        }

        if (!string.IsNullOrWhiteSpace(serial))
        {
            summaryParts.Add($"S/N {serial}");
        }

        if (!string.IsNullOrWhiteSpace(firmware))
        {
            summaryParts.Add($"FW {firmware}");
        }

        var summary = summaryParts.Count == 0 ? raw : string.Join(", ", summaryParts);
        return (manufacturer, model, serial, firmware, summary);
    }

    private static string DetectInterface(string resource)
    {
        foreach (var prefix in KnownPrefixes)
        {
            if (resource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (resource.Length == prefix.Length
                    || !char.IsLetter(resource[prefix.Length])))
            {
                return prefix.ToUpperInvariant() switch
                {
                    "MOCK" => "MOCK",
                    "TCPIP" => "TCPIP",
                    "USB" => "USB",
                    "GPIB" => "GPIB",
                    "ASRL" => "ASRL",
                    "PXI" => "PXI",
                    "VXI" => "VXI",
                    "FIREWIRE" => "FIREWIRE",
                    _ => prefix.ToUpperInvariant(),
                };
            }
        }

        return "Other";
    }

    private static string BuildDetail(string resource, string iface, bool looksLikeAlias)
    {
        if (looksLikeAlias)
        {
            return "possible alias";
        }

        var parts = resource.Split("::", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return iface switch
        {
            "TCPIP" when parts.Length >= 2 => parts[1],
            "USB" when parts.Length >= 2 => string.Join("::", parts.Skip(1).Take(2)),
            "GPIB" when parts.Length >= 2 => parts[^1],
            "ASRL" => parts[0],
            "MOCK" when parts.Length >= 2 => parts[1],
            "PXI" when parts.Length >= 2 => parts[1],
            _ => parts.Length > 1 ? parts[1] : string.Empty,
        };
    }
}
