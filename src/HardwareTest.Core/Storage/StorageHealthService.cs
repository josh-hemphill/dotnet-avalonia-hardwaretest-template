using HardwareTest.Core.Settings;

namespace HardwareTest.Core.Storage;

public enum StorageHealthLevel
{
    Ok,
    Warn,
    Critical,
}

public sealed class StorageHealthSnapshot
{
    public required StorageHealthLevel Level { get; init; }
    public required long AvailableBytes { get; init; }
    public required long WarnThresholdBytes { get; init; }
    public required long CriticalThresholdBytes { get; init; }
    public string? VolumeRoot { get; init; }
    public string Message { get; init; } = string.Empty;
}

public interface IStorageHealthService
{
    StorageHealthSnapshot GetDataVolumeHealth();
}

/// Free-space levels for the DataDirectory volume.
public sealed class StorageHealthService : IStorageHealthService
{
    private readonly AppSettings _settings;
    private readonly string _dataDirectory;
    private readonly Func<string, long?> _availableBytes;

    public StorageHealthService(
        AppSettings settings,
        string dataDirectory,
        Func<string, long?>? availableBytes = null)
    {
        _settings = settings;
        _dataDirectory = dataDirectory;
        _availableBytes = availableBytes ?? TryGetAvailableBytes;
    }

    public StorageHealthSnapshot GetDataVolumeHealth()
    {
        var warn = Math.Max(0, _settings.DataFreeSpaceWarnBytes);
        var critical = Math.Max(0, _settings.DataFreeSpaceCriticalBytes);

        var root = Path.GetPathRoot(Path.GetFullPath(_dataDirectory));
        var available = _availableBytes(_dataDirectory) ?? _availableBytes(root ?? _dataDirectory);
        if (available is null)
        {
            return new StorageHealthSnapshot
            {
                Level = StorageHealthLevel.Ok,
                AvailableBytes = -1,
                WarnThresholdBytes = warn,
                CriticalThresholdBytes = critical,
                VolumeRoot = root,
                Message = "Free space unavailable; continuing.",
            };
        }

        var bytes = available.Value;
        StorageHealthLevel level;
        string message;
        if (critical > 0 && bytes <= critical)
        {
            level = StorageHealthLevel.Critical;
            message =
                $"Disk space critically low ({FormatBytes(bytes)} free). Clear space or open Settings before starting a run.";
        }
        else if (warn > 0 && bytes <= warn)
        {
            level = StorageHealthLevel.Warn;
            message =
                $"Disk space low ({FormatBytes(bytes)} free). Export or prune old runs soon.";
        }
        else
        {
            level = StorageHealthLevel.Ok;
            message = $"{FormatBytes(bytes)} free on data volume.";
        }

        return new StorageHealthSnapshot
        {
            Level = level,
            AvailableBytes = bytes,
            WarnThresholdBytes = warn,
            CriticalThresholdBytes = critical,
            VolumeRoot = root,
            Message = message,
        };
    }

    private static long? TryGetAvailableBytes(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var info = new DriveInfo(root);
            return info.IsReady ? info.AvailableFreeSpace : null;
        }
        catch
        {
            return null;
        }
    }

    internal static string FormatBytes(long bytes)
    {
        const double kib = 1024d;
        const double mib = kib * 1024;
        const double gib = mib * 1024;
        if (bytes >= gib)
        {
            return $"{bytes / gib:0.##} GiB";
        }

        if (bytes >= mib)
        {
            return $"{bytes / mib:0.##} MiB";
        }

        if (bytes >= kib)
        {
            return $"{bytes / kib:0.##} KiB";
        }

        return $"{bytes} B";
    }
}
