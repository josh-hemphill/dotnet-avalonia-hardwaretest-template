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
            items.Add(new Item(
                InsertText: key,
                Name: key,
                Kind: "channel",
                Description: "Existing channel key in this program.",
                Packs: false));
        }

        return items;
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
