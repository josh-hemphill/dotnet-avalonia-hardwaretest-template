namespace HardwareTest.Core.Text;

/// Shortens opaque ids (run/session/commit) for operator-facing chrome.
public static class ShortId
{
    public const int DefaultLength = 8;

    /// Returns up to <paramref name="length"/> characters of <paramref name="id"/> (trimmed).
    public static string Display(string? id, int length = DefaultLength)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        var trimmed = id.Trim();
        if (length <= 0 || trimmed.Length <= length)
        {
            return trimmed;
        }

        return trimmed[..length];
    }
}
