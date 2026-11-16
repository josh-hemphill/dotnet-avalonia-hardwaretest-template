namespace HardwareTest.OpenTap.Host;

internal sealed class OfflineBenchReadBudget(long maximumBytes = 512L * 1024 * 1024, int maximumEntries = 10_000)
{
    private long _remainingBytes = maximumBytes;
    private int _remainingEntries = maximumEntries;
    public void Reserve(long length)
    {
        if (length < 0 || length > _remainingBytes || _remainingEntries == 0)
            throw new InvalidDataException("Offline bundle exceeds the shared expanded-payload or archive-entry budget.");
        _remainingBytes -= length;
        _remainingEntries--;
    }

    internal static byte[] Read(Stream source, long maximum, CancellationToken cancellationToken)
    {
        using var owned = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owned.Length + count > maximum) throw new InvalidDataException("Offline payload exceeds its declared or supported size.");
            owned.Write(buffer, 0, count);
        }
        return owned.ToArray();
    }
}
