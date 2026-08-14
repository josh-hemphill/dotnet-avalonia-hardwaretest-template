using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HardwareTest.Core.Diagnostics;

/// Immutable snapshot of what is running on this bench (build + host environment).
public sealed class BuildInfo
{
    public required string Version { get; init; }
    public required string InformationalVersion { get; init; }
    public required string CommitSha { get; init; }
    public required DateTimeOffset? BuildTimestampUtc { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string RuntimeIdentifier { get; init; }
    public required string OsDescription { get; init; }
    public required string ProcessArchitecture { get; init; }
    public required bool IsSelfContained { get; init; }
    public required DateTimeOffset ProcessStartUtc { get; init; }
    /// Filled by OpenTAP Host — Core stays OpenTAP-free.
    public string? OpenTapEngineVersion { get; init; }

    public BuildInfo WithOpenTapEngineVersion(string? version)
        => new()
        {
            Version = Version,
            InformationalVersion = InformationalVersion,
            CommitSha = CommitSha,
            BuildTimestampUtc = BuildTimestampUtc,
            RuntimeVersion = RuntimeVersion,
            RuntimeIdentifier = RuntimeIdentifier,
            OsDescription = OsDescription,
            ProcessArchitecture = ProcessArchitecture,
            IsSelfContained = IsSelfContained,
            ProcessStartUtc = ProcessStartUtc,
            OpenTapEngineVersion = version,
        };

    /// Reads assembly informational attributes (AoT-safe) plus RuntimeInformation.
    public static BuildInfo FromAssembly(Assembly assembly, DateTimeOffset? processStartUtc = null)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Trim();
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0";
        if (string.IsNullOrWhiteSpace(informational))
        {
            informational = version;
        }

        ParseInformational(informational, out var commit, out var stampUtc);
        var buildUtc = ReadCommitDate(assembly) ?? stampUtc;

        return new BuildInfo
        {
            Version = version,
            InformationalVersion = informational,
            CommitSha = commit,
            BuildTimestampUtc = buildUtc,
            RuntimeVersion = Environment.Version.ToString(),
            RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            OsDescription = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            IsSelfContained = string.IsNullOrEmpty(typeof(object).Assembly.Location),
            ProcessStartUtc = processStartUtc ?? ProcessStartTimeUtc(),
        };
    }

    public static BuildInfo FromEntryAssembly()
        => FromAssembly(Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly);

    public string FormatSupportBlock(string? dataDirectory = null)
    {
        var lines = new List<string>
        {
            "HardwareTest diagnostics",
            $"Version: {Version}",
            $"InformationalVersion: {InformationalVersion}",
            $"Commit: {CommitSha}",
            $"BuildTimestampUtc: {BuildTimestampUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "unknown"}",
            $"Runtime: {RuntimeVersion}",
            $"RID: {RuntimeIdentifier}",
            $"OS: {OsDescription}",
            $"Arch: {ProcessArchitecture}",
            $"SelfContained: {IsSelfContained}",
            $"ProcessStartUtc: {ProcessStartUtc.ToString("u", CultureInfo.InvariantCulture)}",
            $"OpenTAP: {OpenTapEngineVersion ?? "n/a"}",
        };
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            lines.Add($"DataDirectory: {dataDirectory}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static DateTimeOffset ProcessStartTimeUtc()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch (InvalidOperationException)
        {
            return DateTimeOffset.UtcNow;
        }
        catch (PlatformNotSupportedException)
        {
            return DateTimeOffset.UtcNow;
        }
    }

    internal static DateTimeOffset? ReadCommitDate(Assembly assembly)
    {
        foreach (var meta in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (!string.Equals(meta.Key, "CommitDate", StringComparison.Ordinal))
            {
                continue;
            }

            var value = meta.Value?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            return null;
        }

        return null;
    }

    internal static void ParseInformational(string informational, out string commit, out DateTimeOffset? buildUtc)
    {
        commit = "local";
        buildUtc = null;

        // Current: 0.1.0+abc1234  or  0.1.0+local
        // Legacy:  0.1.0+abc1234.20260728220000 (wall-clock suffix, no longer stamped)
        var plus = informational.IndexOf('+');
        if (plus < 0 || plus >= informational.Length - 1)
        {
            return;
        }

        var meta = informational[(plus + 1)..];
        var dot = meta.IndexOf('.');
        if (dot <= 0)
        {
            commit = meta;
            return;
        }

        commit = meta[..dot];
        var stamp = meta[(dot + 1)..];
        if (DateTime.TryParseExact(
                stamp,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            buildUtc = new DateTimeOffset(dt, TimeSpan.Zero);
        }
    }
}
