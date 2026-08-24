using System.Text.Json;
using HardwareTest.Core.Serialization;
using Serilog;

namespace HardwareTest.Core.Hardware;

/// Last successful *IDN? for a plan slot on this station (not AppSettings).
public sealed class StationIdnRecord
{
    public string PlanId { get; set; } = string.Empty;
    public string SlotName { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string IdnRaw { get; set; } = string.Empty;
    public string IdnSummary { get; set; } = string.Empty;
    public DateTimeOffset QueriedAt { get; set; }
}

/// Sidecar document under the data directory (`station-idn.json`).
public sealed class StationIdnDocument
{
    public List<StationIdnRecord> Records { get; set; } = [];
}

public interface IStationIdnStore
{
    IReadOnlyList<StationIdnRecord> List();
    StationIdnRecord? Find(string planId, string slotName);
    void Upsert(StationIdnRecord record);
}

/// Persists queried identities beside settings, without bumping AppSettings schema.
public sealed class FileStationIdnStore : IStationIdnStore
{
    public const string FileName = "station-idn.json";

    private readonly string _path;
    private readonly ILogger? _log;
    private readonly object _gate = new();

    public FileStationIdnStore(string dataDirectory, ILogger? log = null)
    {
        _path = Path.Combine(dataDirectory, FileName);
        _log = log;
    }

    public IReadOnlyList<StationIdnRecord> List() => Load().Records.ToList();

    public StationIdnRecord? Find(string planId, string slotName)
        => Load().Records.FirstOrDefault(r =>
            string.Equals(r.PlanId, planId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.SlotName, slotName, StringComparison.OrdinalIgnoreCase));

    public void Upsert(StationIdnRecord record)
    {
        lock (_gate)
        {
            var doc = LoadUnlocked();
            doc.Records.RemoveAll(r =>
                string.Equals(r.PlanId, record.PlanId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.SlotName, record.SlotName, StringComparison.OrdinalIgnoreCase));
            doc.Records.Add(record);
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var stream = File.Create(_path);
            JsonSerializer.Serialize(stream, doc, AppJsonContext.Default.StationIdnDocument);
        }
    }

    private StationIdnDocument Load()
    {
        lock (_gate)
        {
            return LoadUnlocked();
        }
    }

    private StationIdnDocument LoadUnlocked()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new StationIdnDocument();
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, AppJsonContext.Default.StationIdnDocument)
                   ?? new StationIdnDocument();
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "Could not read station IDN sidecar {Path}", _path);
            return new StationIdnDocument();
        }
    }
}
