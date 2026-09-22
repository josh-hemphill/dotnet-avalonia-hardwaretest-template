namespace HardwareTest.Core.Settings;

/// <summary>Preserves the historical case-insensitive option contract before library parsing.</summary>
public static class CliArgumentNormalizer
{
    public static string[] NormalizeOptionAliases(
        IReadOnlyList<string> args,
        IEnumerable<string> aliases)
    {
        var canonical = aliases
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(alias => alias, StringComparer.OrdinalIgnoreCase);
        var normalized = new string[args.Count];
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            var equals = token.IndexOf('=');
            var optionName = equals >= 0 ? token[..equals] : token;
            normalized[i] = canonical.TryGetValue(optionName, out var alias)
                ? alias + (equals >= 0 ? token[equals..] : string.Empty)
                : token;
        }

        return normalized;
    }
}
