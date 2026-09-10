namespace HardwareTest.Core.StationHealth;

/// Persists station-scoped health records outside run history.
public interface IStationHealthStore
{
    StationHealthRecord? TryRead(string profileId);
    Task WriteAsync(StationHealthRecord record, CancellationToken cancellationToken = default);
}
