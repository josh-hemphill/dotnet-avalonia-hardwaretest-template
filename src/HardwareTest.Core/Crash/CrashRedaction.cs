using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HardwareTest.Core.Crash;

/// Redacts identifying fields for crash dossiers and diagnostics dumps.
public static class CrashRedaction
{
    private static readonly object SaltGate = new();
    private static byte[]? _salt;

    /// Stable per-process salt so the same serial hashes the same within a session.
    public static void EnsureSalt(string? dataDirectory = null)
    {
        lock (SaltGate)
        {
            if (_salt is not null)
            {
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(dataDirectory))
                {
                    Directory.CreateDirectory(dataDirectory);
                    var path = Path.Combine(dataDirectory, ".crash-redaction-salt");
                    if (File.Exists(path))
                    {
                        var existing = File.ReadAllBytes(path);
                        if (existing.Length >= 16)
                        {
                            _salt = existing;
                            return;
                        }
                    }

                    _salt = RandomNumberGenerator.GetBytes(32);
                    File.WriteAllBytes(path, _salt);
                    return;
                }
            }
            catch
            {
                // Fall through to ephemeral salt.
            }

            _salt = RandomNumberGenerator.GetBytes(32);
        }
    }

    public static string HashIdentifier(string? value, bool redact)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!redact)
        {
            return value;
        }

        EnsureSalt();
        var bytes = Encoding.UTF8.GetBytes(value.Trim());
        var hash = HMACSHA256.HashData(_salt!, bytes);
        return "h:" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    public static string RedactPath(string? path, bool redact)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (!redact)
        {
            return path;
        }

        try
        {
            var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(leaf))
            {
                leaf = "path";
            }

            return Path.Combine("…", leaf);
        }
        catch
        {
            return "…";
        }
    }

    public static string RedactText(string? text, IEnumerable<string?> identifiers, bool redact)
    {
        if (string.IsNullOrEmpty(text) || !redact)
        {
            return text ?? string.Empty;
        }

        var result = text;
        foreach (var id in identifiers)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length < 2)
            {
                continue;
            }

            var replacement = HashIdentifier(id, redact: true);
            result = result.Replace(id, replacement, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    public static string FormatUptime(TimeSpan uptime)
        => uptime.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
