using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Serialization;
using Serilog;

namespace HardwareTest.Core.Time;

/// Persists last-known-good UTC under the data directory. Missing/corrupt files are not errors.
public sealed class LastKnownGoodClockStore(string dataDirectory, ILogger? log = null)
{
    private readonly ILogger? _log = log;

    public string FilePath { get; } = Path.Combine(dataDirectory, ClockSkew.LastGoodFileName);

    public ClockLastGoodRecord? Load()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
            {
                return null;
            }

            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize(stream, AppJsonContext.Default.ClockLastGoodRecord);
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "Could not read last-known-good clock file {Path}", FilePath);
            return null;
        }
    }

    public void Save(ClockLastGoodRecord record)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                return;
            }

            AtomicFile.WriteJsonAsync(FilePath, record, AppJsonContext.Default.ClockLastGoodRecord)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "Could not write last-known-good clock file {Path}", FilePath);
        }
    }
}
