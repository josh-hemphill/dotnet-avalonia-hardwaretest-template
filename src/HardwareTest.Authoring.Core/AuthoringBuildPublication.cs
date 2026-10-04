using System.Text.Json;

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
            var incoming = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(source, path)).ToHashSet(StringComparer.Ordinal);
            foreach (var relative in ObsoleteOwnedArtifacts(output, incoming))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(output, relative);
                var saved = Path.Combine(backup, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(destination, saved);
                changed.Add((destination, saved));
                File.Delete(destination);
            }
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

    private static IReadOnlyList<string> ObsoleteOwnedArtifacts(string output, HashSet<string> incoming)
    {
        var receiptPath = Path.Combine(output, ReceiptFileName);
        if (!File.Exists(receiptPath)) return [];
        EnsureContained(output, ResolvedPath(receiptPath, false));
        if (ResolvedPath(receiptPath, false) != Path.GetFullPath(receiptPath))
            throw new AuthoringWorkspaceException("BUILD_OUTPUT_LINK: Previous receipt cannot contain links.");
        AuthoringBuildReceipt receipt;
        try
        {
            receipt = JsonSerializer.Deserialize(File.ReadAllBytes(receiptPath), AuthoringBuildJsonContext.Default.AuthoringBuildReceipt)
                ?? throw new JsonException("Empty receipt.");
            if (receipt.SchemaVersion != 1) throw new JsonException("Unsupported receipt version.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new AuthoringWorkspaceException("BUILD_OUTPUT_RECEIPT: Previous receipt is corrupt or unsupported; output ownership cannot be established.", error);
        }
        var obsolete = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var artifact in receipt.Outputs)
        {
            var relative = artifact.Path;
            var path = Path.Combine(output, relative);
            EnsureContained(output, path);
            if (Path.IsPathRooted(relative) || relative != Path.GetRelativePath(output, path) || !seen.Add(relative)
                || relative == ReceiptFileName)
                throw new AuthoringWorkspaceException("BUILD_OUTPUT_RECEIPT: Previous receipt has unsafe or duplicate artifact paths.");
            if (incoming.Contains(relative) || !File.Exists(path)) continue;
            EnsureContained(output, ResolvedPath(path, false));
            if (ResolvedPath(path, false) != Path.GetFullPath(path))
                throw new AuthoringWorkspaceException("BUILD_OUTPUT_LINK: Obsolete publication targets cannot contain links.");
            if (Hash(File.ReadAllBytes(path)) != artifact.Sha256)
                throw new AuthoringWorkspaceException($"BUILD_OUTPUT_CONFLICT: Previously owned artifact '{relative}' was modified; preserve it or reconcile before building.");
            obsolete.Add(relative);
        }
        return obsolete.AsReadOnly();
    }
}
