using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Serialization;
using Serilog;

namespace HardwareTest.Core.Time;

/// Persists last-known-good UTC under the data directory. Missing/corrupt files are not errors.
public sealed class LastKnownGoodClockStore
{
    private readonly string _path;
    private readonly ILogger? _log;

    public LastKnownGoodClockStore(string dataDirectory, ILogger? log = null)
    {
        _path = Path.Combine(dataDirectory, ClockSkew.LastGoodFileName);
        _log = log;
    }

    public string FilePath => _path;

    public ClockLastGoodRecord? Load()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
            {
                return null;
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, AppJsonContext.Default.ClockLastGoodRecord);
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "Could not read last-known-good clock file {Path}", _path);
            return null;
        }
    }

    public void Save(ClockLastGoodRecord record)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_path))
            {
                return;
            }

            AtomicFile.WriteJsonAsync(_path, record, AppJsonContext.Default.ClockLastGoodRecord)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "Could not write last-known-good clock file {Path}", _path);
        }
    }
}
