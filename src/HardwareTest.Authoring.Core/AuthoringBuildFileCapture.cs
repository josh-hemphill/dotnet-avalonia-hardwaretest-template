using System.Buffers;
using System.Security.Cryptography;

namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    private static string CapturedFileTarget(string path, string resolvedDirectory)
    {
        // Walk already resolves and validates this directory and rejects child directory
        // links. Only a file link needs another complete physical target resolution.
        return new FileInfo(path).LinkTarget is null
            ? Path.Combine(resolvedDirectory, Path.GetFileName(path)) : ResolvedPath(path, false);
    }

    private static (string Hash, byte[] Bytes) ReadCapturedFile(string path, bool materialize)
    {
        if (materialize)
        {
            var bytes = File.ReadAllBytes(path);
            return (Hash(bytes), bytes);
        }
        // Identity capture must hash actual bytes on every recheck, even when size and
        // timestamps are unchanged. Streaming avoids allocating the entire SDK payload.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            0, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) != 0) hash.AppendData(buffer, 0, count);
            return (Convert.ToHexString(hash.GetHashAndReset()), []);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
