namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    // Each move is rollback protected. The receipt is committed last; consumers must use it as the completion marker.
    internal static void Publish(string source, string output, CancellationToken cancellationToken,
        Action<string, string>? move = null, Action? validate = null)
    {
        move ??= (from, to) => File.Move(from, to, overwrite: true);
        Directory.CreateDirectory(output);
        var guardRoot = ResolvedPath(output, true);
        if (guardRoot != Path.GetFullPath(output))
            throw new AuthoringWorkspaceException("BUILD_OUTPUT_LINK: Publication directory cannot contain links.");
        var backup = Path.Combine(Path.GetDirectoryName(output)!, ".authoring-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var retainBackup = false;
        var changed = new List<(string Destination, string? Backup)>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                .OrderBy(p => Path.GetFileName(p) == ReceiptFileName ? 1 : 0).ThenBy(p => p, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFileName(path) == ReceiptFileName) validate?.Invoke();
                var relative = Path.GetRelativePath(source, path);
                var destination = Path.Combine(output, relative);
                EnsureContained(output, ResolvedPath(destination, false));
                if (ResolvedPath(destination, false) != Path.GetFullPath(destination))
                    throw new AuthoringWorkspaceException("BUILD_OUTPUT_LINK: Publication targets cannot contain links.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string? saved = null;
                if (File.Exists(destination))
                {
                    saved = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(destination, saved);
                }
                changed.Add((destination, saved));
                move(path, destination);
            }
            validate?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception original)
        {
            var failures = new List<Exception>();
            foreach (var item in changed.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.Backup is null) File.Delete(item.Destination);
                    else File.Copy(item.Backup, item.Destination, overwrite: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failures.Add(error); }
            }
            if (failures.Count != 0)
            {
                retainBackup = true;
                throw new AuthoringWorkspaceException($"BUILD_ROLLBACK: Publication failed and restoration was incomplete. Retained preimages: {backup}", new AggregateException(new[] { original }.Concat(failures)));
            }
            throw;
        }
        finally { if (!retainBackup) Directory.Delete(backup, recursive: true); }
    }
}
