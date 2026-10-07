namespace HardwareTest.Core.Hardware;

public sealed class VisaResourceInfo
{
    public required string Resource { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Interface { get; init; } = "Other";
    public string Detail { get; init; } = string.Empty;
    public bool LooksLikeAlias { get; init; }
    public bool SupportsMessageQuery { get; init; }

    public static VisaResourceInfo FromResource(string resource, string? description = null)
    {
        var parsed = VisaResourceParser.Parse(resource);
        return new VisaResourceInfo
        {
            Resource = resource,
            Description = description ?? resource,
            Interface = parsed.Interface,
            Detail = parsed.Detail,
            LooksLikeAlias = parsed.LooksLikeAlias,
            SupportsMessageQuery = parsed.SupportsMessageQuery,
        };
    }
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
        VisaResourceInfo.FromResource("MOCK::INSTR0", "Mock DMM INSTR0"),
        VisaResourceInfo.FromResource("MOCK::SCOPE1", "Mock oscilloscope SCOPE1"),
        VisaResourceInfo.FromResource("MOCK::PSU2", "Mock power supply PSU2"),
    ];

    public Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Catalog);
    }
}

/// Discovers resources via IVI GlobalResourceManager.Find; reports failures instead of silent empty.
public sealed class IviVisaResourceDiscovery : IVisaResourceDiscovery
{
    private readonly Action<string>? _onError;

    public IviVisaResourceDiscovery(Action<string>? onError = null)
    {
        _onError = onError;
    }

    public Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var found = global::Ivi.Visa.GlobalResourceManager.Find("?*");
            IReadOnlyList<VisaResourceInfo> list = found
                .Select(r => VisaResourceInfo.FromResource(r))
                .ToArray();
            return Task.FromResult(list);
        }
        catch (Exception ex)
        {
            var message =
                $"VISA discovery failed: {ex.Message}. Install a vendor VISA runtime or enable Use mock VISA.";
            _onError?.Invoke(message);
            throw new InvalidOperationException(message, ex);
        }
    }
}

public sealed class ConfigurableVisaResourceDiscovery : IVisaResourceDiscovery
{
    private readonly IVisaResourceDiscovery _inner;

    public ConfigurableVisaResourceDiscovery(bool useMockVisa, Action<string>? onIviError = null)
    {
        _inner = useMockVisa
            ? new MockVisaResourceDiscovery()
            : new IviVisaResourceDiscovery(onIviError);
    }

    public Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
        => _inner.FindAsync(cancellationToken);
}
