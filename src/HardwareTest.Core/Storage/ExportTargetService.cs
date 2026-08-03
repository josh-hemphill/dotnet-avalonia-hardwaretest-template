using HardwareTest.Core.IO;
using HardwareTest.Core.Settings;
using Serilog;

namespace HardwareTest.Core.Storage;

public sealed class ExportTarget
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string RootPath { get; init; }
    public bool IsRemovable { get; init; }
    public long? AvailableBytes { get; init; }
}

public interface IExportTargetService
{
    IReadOnlyList<ExportTarget> ListTargets();
    /// Atomically write bytes under the target (temp → flush → rename).
    string WriteAtomic(ExportTarget target, string relativePath, byte[] content, long? minFreeBytes = null);
    /// Copy files into a package folder under the target; returns the package directory.
    string ExportPackage(ExportTarget target, string packageFolderName, IEnumerable<(string SourcePath, string RelativeName)> files);
}

/// Removable media + configured ExportDirectory targets with atomic writes.
public sealed class ExportTargetService : IExportTargetService
{
    private readonly AppSettings _settings;
    private readonly string _dataDirectory;
    private readonly ILogger? _log;
    private readonly Func<IEnumerable<ExportTarget>>? _removableRoots;

    public ExportTargetService(
        AppSettings settings,
        string dataDirectory,
        ILogger? log = null,
        Func<IEnumerable<ExportTarget>>? removableRoots = null)
    {
        _settings = settings;
        _dataDirectory = dataDirectory;
        _log = log;
        _removableRoots = removableRoots;
    }

    public IReadOnlyList<ExportTarget> ListTargets()
    {
        var list = new List<ExportTarget>();
        var removable = (_removableRoots?.Invoke() ?? DetectRemovableRoots()).ToList();
        var configured = string.IsNullOrWhiteSpace(_settings.ExportDirectory)
            ? null
            : new ExportTarget
            {
                Id = "configured",
                DisplayName = "Export directory",
                RootPath = Path.GetFullPath(_settings.ExportDirectory.Trim()),
                IsRemovable = false,
                AvailableBytes = TryAvailable(_settings.ExportDirectory),
            };

        if (_settings.PreferRemovableExport)
        {
            list.AddRange(removable);
            if (configured is not null)
            {
                list.Add(configured);
            }
        }
        else
        {
            if (configured is not null)
            {
                list.Add(configured);
            }

            list.AddRange(removable);
        }

        // Local fallback under data/exports when nothing else is listed.
        if (list.Count == 0)
        {
            var local = Path.Combine(_dataDirectory, "exports");
            list.Add(new ExportTarget
            {
                Id = "local-exports",
                DisplayName = "Local exports",
                RootPath = local,
                IsRemovable = false,
                AvailableBytes = TryAvailable(_dataDirectory),
            });
        }

        return list;
    }

    public string WriteAtomic(ExportTarget target, string relativePath, byte[] content, long? minFreeBytes = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        EnsureSpace(target, content.LongLength, minFreeBytes);
        var relative = SanitizeRelative(relativePath);
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var dest = PathContainment.CombineUnderRoot(target.RootPath, segments);
        var dir = Path.GetDirectoryName(dest) ?? NormalizeRootPath(target.RootPath);
        Directory.CreateDirectory(dir);
        var temp = dest + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temp, content);
            using (var fs = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(dest))
            {
                File.Delete(dest);
            }

            File.Move(temp, dest);
            return dest;
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // best effort
                }
            }
        }
    }

    public string ExportPackage(
        ExportTarget target,
        string packageFolderName,
        IEnumerable<(string SourcePath, string RelativeName)> files)
    {
        ArgumentNullException.ThrowIfNull(target);
        var safeName = Sanitize(packageFolderName);
        var packageRoot = PathContainment.CombineUnderRoot(target.RootPath, safeName);
        Directory.CreateDirectory(NormalizeRootPath(target.RootPath));

        long total = 0;
        var fileList = files.ToList();
        foreach (var (source, _) in fileList)
        {
            if (File.Exists(source))
            {
                total += new FileInfo(source).Length;
            }
        }

        EnsureSpace(target, total, minFreeBytes: null);

        var staging = packageRoot + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (source, relative) in fileList)
            {
                if (!File.Exists(source))
                {
                    _log?.Warning("Export skip missing file {Path}", source);
                    continue;
                }

                var dest = Path.Combine(staging, SanitizeRelative(relative));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(source, dest, overwrite: true);
            }

            if (Directory.Exists(packageRoot))
            {
                Directory.Delete(packageRoot, recursive: true);
            }

            Directory.Move(staging, packageRoot);
            return packageRoot;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch
                {
                    // best effort
                }
            }
        }
    }

    private void EnsureSpace(ExportTarget target, long neededBytes, long? minFreeBytes)
    {
        var available = target.AvailableBytes ?? TryAvailable(target.RootPath);
        if (available is null)
        {
            return;
        }

        var required = Math.Max(neededBytes, minFreeBytes ?? 0);
        if (available.Value < required)
        {
            throw new IOException(
                $"Not enough free space on {target.DisplayName} ({StorageHealthService.FormatBytes(available.Value)} free, need {StorageHealthService.FormatBytes(required)}).");
        }
    }

    private static IEnumerable<ExportTarget> DetectRemovableRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Removable)
                {
                    continue;
                }

                yield return new ExportTarget
                {
                    Id = "removable:" + drive.Name.TrimEnd('\\', '/'),
                    DisplayName = $"Removable ({drive.Name.TrimEnd('\\', '/')})",
                    RootPath = drive.RootDirectory.FullName,
                    IsRemovable = true,
                    AvailableBytes = drive.AvailableFreeSpace,
                };
            }

            yield break;
        }

        foreach (var mediaRoot in new[] { "/media", "/run/media" })
        {
            if (!Directory.Exists(mediaRoot))
            {
                continue;
            }

            foreach (var userDir in Directory.EnumerateDirectories(mediaRoot))
            {
                IEnumerable<string> mounts;
                try
                {
                    mounts = Directory.EnumerateDirectories(userDir);
                }
                catch
                {
                    continue;
                }

                foreach (var mount in mounts)
                {
                    yield return new ExportTarget
                    {
                        Id = "removable:" + mount,
                        DisplayName = $"Removable ({Path.GetFileName(mount)})",
                        RootPath = mount,
                        IsRemovable = true,
                        AvailableBytes = TryAvailable(mount),
                    };
                }
            }
        }
    }

    private static long? TryAvailable(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
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

    private static string NormalizeRootPath(string root)
        => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string Sanitize(string name)
    {
        var cleaned = PortableFileNames.Sanitize(name);
        return string.IsNullOrWhiteSpace(cleaned) || cleaned is "_" ? "export" : cleaned.Trim();
    }

    private static string SanitizeRelative(string relative)
    {
        var parts = relative.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part is not "." and not "..")
            .Select(Sanitize)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        if (parts.Length == 0)
        {
            throw new ArgumentException("Relative path is required.", nameof(relative));
        }

        return Path.Combine(parts);
    }
}
