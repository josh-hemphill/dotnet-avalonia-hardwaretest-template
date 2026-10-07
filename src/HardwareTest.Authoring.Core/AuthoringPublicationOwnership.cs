using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace HardwareTest.Authoring;

/// Cross-process ownership of a case-insensitive publication identity.
/// Keep this synchronous lease on its acquiring thread through validation and publication.
public sealed class AuthoringPublicationOwnership : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> Owned = new(StringComparer.Ordinal);
    private readonly Mutex _mutex;
    private readonly string _name;
    private bool _disposed;
    private AuthoringPublicationOwnership(Mutex mutex, string name) { _mutex = mutex; _name = name; }

    public static AuthoringPublicationOwnership Acquire(string destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = Path.GetFullPath(destination).ToUpperInvariant();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var name = "HardwareTest.Authoring.Publication." + digest;
        var mutex = new Mutex(false, name);
        // Native mutexes are reentrant: reject a second creator on the owning thread too.
        if (!Owned.TryAdd(name, 0)) { mutex.Dispose(); throw Busy(); }
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw Busy();
            cancellationToken.ThrowIfCancellationRequested();
            return new AuthoringPublicationOwnership(mutex, name);
        }
        catch
        {
            if (acquired) mutex.ReleaseMutex();
            mutex.Dispose();
            Owned.TryRemove(name, out _);
            throw;
        }
    }

    private static IOException Busy() => new("A case-equivalent destination is being created; retry or choose a different ID.");

    public void Dispose()
    {
        if (_disposed) return;
        Owned.TryRemove(_name, out _);
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _disposed = true;
    }
}
