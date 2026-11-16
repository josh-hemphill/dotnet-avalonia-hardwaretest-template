namespace HardwareTest.Features.RunTest;

/// Maps step/stage status strings to chip labels.
public static class StatusChip
{
    public static string FromStatus(string? statusText, string? verdict = null)
    {
        var raw = string.IsNullOrWhiteSpace(statusText) ? verdict : statusText;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Pending";
        }

        if (raw.Contains("Await", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("operator", StringComparison.OrdinalIgnoreCase))
        {
            return "Awaiting";
        }

        if (raw.Contains("Run", StringComparison.OrdinalIgnoreCase))
        {
            return "Running";
        }

        if (raw.Contains("Cancel", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Abort", StringComparison.OrdinalIgnoreCase))
        {
            return "Cancelled";
        }

        if (raw.Contains("Error", StringComparison.OrdinalIgnoreCase)) return "Error";

        if (raw.Contains("Fail", StringComparison.OrdinalIgnoreCase))
        {
            return "Fail";
        }

        if (raw.Contains("Pass", StringComparison.OrdinalIgnoreCase))
        {
            return "Pass";
        }

        if (string.Equals(raw, "NotSet", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Pending", StringComparison.OrdinalIgnoreCase))
        {
            return "Pending";
        }

        return raw.Length <= 12 ? raw : raw[..12];
    }

    public static bool NeedsAttention(string chip) => chip is "Fail" or "Error" or "Cancelled";
    public static bool IsTerminal(string chip) => chip is "Pass" or "Fail" or "Error" or "Cancelled";
}
