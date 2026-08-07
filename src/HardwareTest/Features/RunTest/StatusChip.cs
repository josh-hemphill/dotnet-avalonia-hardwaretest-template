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

        if (raw.Contains("Fail", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Error", StringComparison.OrdinalIgnoreCase))
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

        if (raw.Contains("Cancel", StringComparison.OrdinalIgnoreCase))
        {
            return "Fail";
        }

        return raw.Length <= 12 ? raw : raw[..12];
    }
}
