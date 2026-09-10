using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Serialization;
using Serilog;

namespace HardwareTest.Core.StationHealth;

/// JSON files under `{dataDirectory}/station-health/{profileId}.json`.
public sealed class FileStationHealthStore : IStationHealthStore
{
    public const string DirectoryName = "station-health";
    public const string DefaultProfileId = "default";

    private readonly string _rootDirectory;

    public FileStationHealthStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _rootDirectory = PathContainment.CombineUnderRoot(dataDirectory, DirectoryName);
        Directory.CreateDirectory(_rootDirectory);
    }

    public StationHealthRecord? TryRead(string profileId)
    {
        var path = PathFor(profileId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.StationHealthRecord);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warning(ex, "Skipping unreadable station health record at {Path}", path);
            return null;
        }
    }

    public async Task WriteAsync(StationHealthRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.ProfileId))
        {
            record.ProfileId = DefaultProfileId;
        }

        record.SchemaVersion = SchemaVersions.StationHealthRecord;
        var path = PathFor(record.ProfileId);
        await AtomicFile.WriteJsonAsync(
                path,
                record,
                AppJsonContext.Default.StationHealthRecord,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public string PathFor(string profileId)
    {
        var key = string.IsNullOrWhiteSpace(profileId) ? DefaultProfileId : profileId.Trim();
        return PathContainment.CombineUnderRoot(_rootDirectory, PortableFileNames.Sanitize(key) + ".json");
    }
}
