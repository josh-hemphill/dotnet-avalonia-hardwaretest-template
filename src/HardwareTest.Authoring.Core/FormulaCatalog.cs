namespace HardwareTest.Authoring;

/// Allowlist and chip/completion items for the MATLAB-flavored formula subset. Not a type checker.
public static class FormulaCatalog
{
    public static IReadOnlyList<string> AllowedFunctions { get; } =
    [
        "abs", "sqrt", "min", "max", "mean", "sum", "std", "diff", "length", "median",
        "rise_time", "inband_pct", "filter", "filtfilt",
    ];

    public static IReadOnlyList<string> ReservedUnknown { get; } =
        ["fft", "tf", "plot", "eval"];

    public sealed record Item(
        string InsertText,
        string Name,
        string Kind,
        string Description,
        bool Packs);

    public readonly record struct IdentSpan(int Start, int Length, string Text);

    public static IReadOnlyList<Item> Completions(IReadOnlyList<string> channelKeys)
    {
        var items = new List<Item>(AllowedFunctions.Count + channelKeys.Count);
        foreach (var name in AllowedFunctions)
        {
            items.Add(new Item(
                InsertText: InsertFor(name),
                Name: name,
                Kind: "function",
                Description: Describe(name),
                Packs: Packs(name)));
        }

        foreach (var key in channelKeys
                     .Where(key => !string.IsNullOrWhiteSpace(key))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ReservedUnknown.Contains(key))
            {
                continue;
            }

            items.Add(new Item(
                InsertText: key,
                Name: key,
                Kind: "channel",
                Description: "Existing channel key in this program.",
                Packs: false));
        }

        return items;
    }

    public static IReadOnlyList<Item> CompletionsFor(string prefix, IReadOnlyList<string> channelKeys)
    {
        var all = Completions(channelKeys);
        if (string.IsNullOrEmpty(prefix))
        {
            return all;
        }

        return all
            .Where(item => item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// Ident under caret using the parser rule (letters, digits, `_`, `.` before a letter).
    public static IdentSpan IdentAt(string? source, int caret)
    {
        var text = source ?? string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (!(char.IsLetter(text[i]) || text[i] == '_'))
            {
                i++;
                continue;
            }

            var start = i;
            i++;
            while (i < text.Length)
            {
                var ch = text[i];
                if (char.IsLetterOrDigit(ch) || ch == '_')
                {
                    i++;
                    continue;
                }

                if (ch == '.' && i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] == '_'))
                {
                    i++;
                    continue;
                }

                break;
            }

            if (caret >= start && caret <= i)
            {
                return new IdentSpan(start, i - start, text[start..i]);
            }
        }

        return new IdentSpan(caret, 0, string.Empty);
    }

    public static bool Packs(string name)
        => name is "mean" or "filter" or "filtfilt";

    private static string InsertFor(string name) => $"{name}(";

    private static string Describe(string name)
        => name switch
        {
            "mean" => "mean(x) — last-sample scalar. Save lowers mean(ident)+threshold → Mean GTE.",
            "filter" => "filter(b, a, channel) — causal IIR. Save lowers top-level filter → Apply Transfer Function.",
            "filtfilt" => "filtfilt(b, a, channel) — zero-phase IIR. Save lowers top-level filtfilt → Apply Transfer Function.",
            "abs" => "abs(x) — preview/eval only (does not pack).",
            "sqrt" => "sqrt(x) — preview/eval only (does not pack).",
            "min" => "min(x) — preview/eval only (does not pack).",
            "max" => "max(x) — preview/eval only (does not pack).",
            "sum" => "sum(x) — preview/eval only (does not pack).",
            "std" => "std(x) — preview/eval only (does not pack).",
            "diff" => "diff(x) — preview/eval only (does not pack).",
            "length" => "length(x) — preview/eval only (does not pack).",
            "median" => "median(x) — preview/eval only (does not pack).",
            "rise_time" => "rise_time(x, lo, hi) — preview/eval only (does not pack).",
            "inband_pct" => "inband_pct(x, lo, hi) — preview/eval only (does not pack).",
            _ => name,
        };
}
