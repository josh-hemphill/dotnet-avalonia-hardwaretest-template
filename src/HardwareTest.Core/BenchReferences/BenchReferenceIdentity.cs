using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HardwareTest.Core.Serialization;

namespace HardwareTest.Core.BenchReferences;

public static class BenchReferenceIdentity
{
    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string HashText(string text) => Sha256(Encoding.UTF8.GetBytes(text));

    /// Hashes explicit effective configuration inputs. Callers supply runtime settings, not UI or DUT identity.
    public static string ConfigurationHash(IEnumerable<KeyValuePair<string, string>> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var ordered = inputs.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        if (ordered.Any(item => string.IsNullOrWhiteSpace(item.Key))
            || ordered.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidOperationException("Reference configuration needs unique nonempty input keys.");
        // Length-prefix every field so embedded delimiters cannot alias another configuration.
        var text = new StringBuilder();
        foreach (var item in ordered)
        {
            var value = item.Value ?? string.Empty;
            text.Append(item.Key.Length).Append(':').Append(item.Key)
                .Append(value.Length).Append(':').Append(value);
        }
        return HashText(text.ToString());
    }

    public static string ComputeProfileHash(BenchReferenceProfile profile)
    {
        var canonical = profile with
        {
            ContentSha256 = string.Empty,
            Context = profile.Context with { },
            Values = profile.Values.OrderBy(value => value.ReferenceKey, StringComparer.Ordinal)
                .ThenBy(value => value.MetricKey, StringComparer.Ordinal)
                .ThenBy(value => value.ProducerStepId)
                .ThenBy(value => value.Unit, StringComparer.Ordinal)
                .Select(value => value with { }).ToList(),
        };
        return Sha256(JsonSerializer.SerializeToUtf8Bytes(canonical, AppJsonContext.Default.BenchReferenceProfile));
    }

    internal static bool IsHash(string? hash)
        => hash is { Length: 64 } && hash.All(character => char.IsAsciiHexDigit(character));
}
