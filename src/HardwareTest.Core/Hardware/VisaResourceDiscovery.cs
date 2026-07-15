using HardwareTest.Core.Settings;

namespace HardwareTest.Core.Hardware;

public sealed class VisaResourceInfo
{
    public required string Resource { get; init; }
    public string Description { get; init; } = string.Empty;
}

public interface IVisaResourceDiscovery
{
    Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default);
}

/// Deterministic mock catalog for demos and CI.
public sealed class MockVisaResourceDiscovery : IVisaResourceDiscovery
{
    public static readonly IReadOnlyList<VisaResourceInfo> Catalog =
    [
        new() { Resource = "MOCK::INSTR0", Description = "Mock DMM INSTR0" },
        new() { Resource = "MOCK::SCOPE1", Description = "Mock oscilloscope SCOPE1" },
        new() { Resource = "MOCK::PSU2", Description = "Mock power supply PSU2" },
    ];

    public Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Catalog);
    }
}

/// Discovers resources via IVI GlobalResourceManager.Find; soft-fails if runtime missing.
public sealed class IviVisaResourceDiscovery : IVisaResourceDiscovery
{
    public Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var found = global::Ivi.Visa.GlobalResourceManager.Find("?*");
            IReadOnlyList<VisaResourceInfo> list = found
                .Select(r => new VisaResourceInfo { Resource = r, Description = r })
                .ToArray();
            return Task.FromResult(list);
        }
        catch (Exception)
        {
            return Task.FromResult<IReadOnlyList<VisaResourceInfo>>([]);
        }
    }
}

public sealed class ConfigurableVisaResourceDiscovery : IVisaResourceDiscovery
{
    private readonly IVisaResourceDiscovery _inner;

    public ConfigurableVisaResourceDiscovery(bool useMockVisa)
    {
        _inner = useMockVisa
            ? new MockVisaResourceDiscovery()
            : new IviVisaResourceDiscovery();
    }

    public Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
        => _inner.FindAsync(cancellationToken);
}

/// Resolves roles / registry ids / display names / literal VISA strings.
public static class InstrumentResourceResolver
{
    public static string Resolve(
        string? resourceOrId,
        AppSettings settings,
        IReadOnlyDictionary<string, string>? roleMap = null)
    {
        if (string.IsNullOrWhiteSpace(resourceOrId))
        {
            return settings.DefaultVisaResource;
        }

        var key = resourceOrId.Trim();

        var station = settings.StationBindings.FirstOrDefault(b =>
            string.Equals(b.Role, key, StringComparison.OrdinalIgnoreCase));
        if (station is not null && !string.IsNullOrWhiteSpace(station.InstrumentId))
        {
            return ResolveRegistryOrLiteral(station.InstrumentId, settings);
        }

        if (roleMap is not null
            && roleMap.TryGetValue(key, out var mappedId)
            && !string.IsNullOrWhiteSpace(mappedId))
        {
            return ResolveRegistryOrLiteral(mappedId, settings);
        }

        return ResolveRegistryOrLiteral(key, settings);
    }

    private static string ResolveRegistryOrLiteral(string key, AppSettings settings)
    {
        var match = settings.Instruments.FirstOrDefault(i =>
            i.Enabled && (
                string.Equals(i.Id, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.DisplayName, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.Resource, key, StringComparison.OrdinalIgnoreCase)));
        return match?.Resource ?? key;
    }
}
