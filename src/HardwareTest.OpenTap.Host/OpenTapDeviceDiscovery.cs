using HardwareTest.Core.Hardware;
using OpenTap;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host;

public sealed class OpenTapDiscoveredAddress
{
    public required string Address { get; init; }
    public required string Source { get; init; }
    public required string Kind { get; init; }
    public string Interface { get; init; } = "Other";
    public string Detail { get; init; } = string.Empty;
    public bool LooksLikeAlias { get; init; }
    public bool SupportsMessageQuery { get; init; }
}

/// Enumerates VisaAddress strings via OpenTAP IDeviceDiscovery plugins.
public static class OpenTapDeviceDiscovery
{
    /// Collect addresses from all IDeviceDiscovery plugins that support VisaAddressAttribute.
    public static IReadOnlyList<OpenTapDiscoveredAddress> ListVisaAddresses(ILogger? logger = null)
    {
        var log = logger ?? Serilog.Log.ForContext(typeof(OpenTapDeviceDiscovery));
        var byAddress = new Dictionary<string, OpenTapDiscoveredAddress>(StringComparer.OrdinalIgnoreCase);
        var attribute = new VisaAddressAttribute();

        IEnumerable<Type> pluginTypes;
        try
        {
            pluginTypes = PluginManager.GetPlugins<IDeviceDiscovery>();
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "PluginManager.GetPlugins<IDeviceDiscovery> failed");
            return [];
        }

        foreach (var type in pluginTypes)
        {
            IDeviceDiscovery? discovery;
            try
            {
                discovery = Activator.CreateInstance(type) as IDeviceDiscovery;
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "Could not create IDeviceDiscovery {Type}", type.FullName);
                continue;
            }

            if (discovery is null)
            {
                continue;
            }

            try
            {
                if (!discovery.CanDetect(attribute))
                {
                    continue;
                }

                var addresses = discovery.DetectDeviceAddresses(attribute) ?? [];
                var source = type.Name;
                foreach (var address in addresses)
                {
                    if (string.IsNullOrWhiteSpace(address))
                    {
                        continue;
                    }

                    var trimmed = address.Trim();
                    if (byAddress.ContainsKey(trimmed))
                    {
                        continue;
                    }

                    var parsed = VisaResourceParser.Parse(trimmed);
                    byAddress[trimmed] = new OpenTapDiscoveredAddress
                    {
                        Address = trimmed,
                        Source = source,
                        Kind = "VisaAddress",
                        Interface = parsed.Interface,
                        Detail = parsed.Detail,
                        LooksLikeAlias = parsed.LooksLikeAlias,
                        SupportsMessageQuery = parsed.SupportsMessageQuery,
                    };
                }
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "IDeviceDiscovery {Type} failed DetectDeviceAddresses", type.FullName);
            }
        }

        return byAddress.Values
            .OrderBy(a => a.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
