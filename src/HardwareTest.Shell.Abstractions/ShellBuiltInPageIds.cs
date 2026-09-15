namespace HardwareTest.Shell;

/// Reserved page ids owned by the test-automation pack. Guest apps must not reuse these.
public static class ShellBuiltInPageIds
{
    public const string Home = "Home";
    public const string RunTest = "RunTest";
    public const string Inspect = "Inspect";
    public const string Results = "Results";
    public const string ReportPreview = "ReportPreview";
    public const string Instruments = "Instruments";
    public const string Settings = "Settings";

    public static readonly IReadOnlyList<string> All =
    [
        Home,
        RunTest,
        Inspect,
        Results,
        ReportPreview,
        Instruments,
        Settings,
    ];

    /// True when <paramref name="pageId"/> is a built-in test-pack page.
    public static bool IsReserved(string? pageId)
        => pageId is not null
           && All.Contains(pageId, StringComparer.Ordinal);
}
