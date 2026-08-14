using HardwareTest.Core.Hardware;
using HardwareTest.Core.IO;
using HardwareTest.Core.Settings;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host;

/// Plugin search, package catalog, and device discovery — kept off the run type.
public sealed class OpenTapHostCatalog : IOpenTapHostCatalog
{
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly IVisaBroker? _visaBroker;
    private bool _pluginSearchDone;

    public OpenTapHostCatalog(AppSettings settings, ILogger logger, IVisaBroker? visaBroker = null)
    {
        _settings = settings;
        _logger = logger;
        _visaBroker = visaBroker;
    }

    public void EnsurePlugins()
    {
        if (_pluginSearchDone)
        {
            return;
        }

        var extras = new List<string>();
        foreach (var dir in CollectConfiguredPluginDirectories())
        {
            if (PluginDirectoryTrust.Allows(_settings.DataDirectory, dir, _settings.IsEngineerDebugMode))
            {
                extras.Add(dir);
                continue;
            }

            _logger.Warning(
                "Skipping OpenTAP plugin directory outside trusted root {Root}: {Dir}",
                PluginDirectoryTrust.TrustedRoot(_settings.DataDirectory),
                dir);
        }

        // Directory list mutations + Search share one gate (OpenTapPluginSearch.SearchSerialized).
        OpenTapPluginSearch.SearchSerialized(extras, _visaBroker);
        _pluginSearchDone = true;
    }

    public IReadOnlyList<OpenTapPluginDirectoryInfo> ListPluginDirectories()
    {
        EnsurePlugins();
        return OpenTapPackageCatalog.ListPluginDirectories(_settings);
    }

    public IReadOnlyList<OpenTapPackageInfo> ListInstalledPackages()
    {
        EnsurePlugins();
        return OpenTapPackageCatalog.ListInstalledPackages(_settings, _logger);
    }

    public IReadOnlyList<OpenTapDiscoveredAddress> ListDiscoveredDeviceAddresses()
    {
        EnsurePlugins();
        return OpenTapDeviceDiscovery.ListVisaAddresses(_logger);
    }

    private IEnumerable<string> CollectConfiguredPluginDirectories()
    {
        foreach (var dir in _settings.OpenTapPluginDirectories)
        {
            if (!string.IsNullOrWhiteSpace(dir))
            {
                yield return dir;
            }
        }

        var env = Environment.GetEnvironmentVariable("HARDWARETEST_OPENTAP_PLUGIN_DIRS");
        if (string.IsNullOrWhiteSpace(env))
        {
            yield break;
        }

        foreach (var part in env.Split(
                     [Path.PathSeparator, ';'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return part;
        }
    }
}
