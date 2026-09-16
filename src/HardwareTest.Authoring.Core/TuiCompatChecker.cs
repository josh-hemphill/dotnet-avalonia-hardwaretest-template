using System.Reflection;
using System.Xml.Linq;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using OpenTap;

namespace HardwareTest.Authoring;

/// Compares authoring vs TUI OpenTAP homes: catalog, round-trip mixins, and plan contract.
public sealed class TuiCompatChecker : ITuiCompatChecker
{
    private static readonly XNamespace PackageNs = "http://opentap.io/schemas/package";

    public TuiCompatReport Compare(AuthoringWorkspace workspace, OpenTapHome authoringHome, OpenTapHome tuiHome)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(authoringHome);
        ArgumentNullException.ThrowIfNull(tuiHome);

        var authoringCatalog = ScanCatalog(authoringHome);
        var tuiCatalog = ScanCatalog(tuiHome);
        var catalog = DiffCatalog(authoringCatalog, tuiCatalog);
        var roundTrips = new List<RoundTripFinding>();
        foreach (var planPath in workspace.TapPlanPaths)
        {
            roundTrips.AddRange(InspectPlan(planPath, tuiCatalog, tuiHome));
        }

        return new TuiCompatReport(catalog, roundTrips);
    }

    internal static IReadOnlyDictionary<string, string> ScanCatalog(OpenTapHome home)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var packages = Path.Combine(home.Root, "Packages");
        if (!Directory.Exists(packages))
        {
            return map;
        }

        foreach (var xmlPath in Directory.EnumerateFiles(packages, "package.xml", SearchOption.AllDirectories))
        {
            foreach (var dll in EnumeratePackageDlls(xmlPath))
            {
                if (string.Equals(
                        Path.GetFileName(dll),
                        OpenTapHomeBootstrapper.VisaAssemblyFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddPluginTypes(dll, map);
            }
        }

        return map;
    }

    internal static IReadOnlyList<CatalogDelta> DiffCatalog(
        IReadOnlyDictionary<string, string> authoring,
        IReadOnlyDictionary<string, string> tui)
    {
        var deltas = new List<CatalogDelta>();
        foreach (var pair in authoring)
        {
            if (IsTuiAppType(pair.Key) || tui.ContainsKey(pair.Key))
            {
                continue;
            }

            deltas.Add(new CatalogDelta(pair.Key, pair.Value, CatalogSides.TuiHome));
        }

        foreach (var pair in tui)
        {
            if (IsTuiAppType(pair.Key) || authoring.ContainsKey(pair.Key))
            {
                continue;
            }

            deltas.Add(new CatalogDelta(pair.Key, pair.Value, CatalogSides.AuthoringHome));
        }

        return deltas
            .OrderBy(d => d.TypeName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<RoundTripFinding> InspectPlan(
        string planPath,
        IReadOnlyDictionary<string, string> tuiCatalog,
        OpenTapHome tuiHome)
    {
        var findings = new List<RoundTripFinding>();
        if (!File.Exists(planPath))
        {
            findings.Add(new RoundTripFinding(planPath, TuiCompatCodes.TypeUnknown, "TapPlan file is missing."));
            return findings;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(planPath);
        }
        catch (Exception ex)
        {
            findings.Add(new RoundTripFinding(planPath, TuiCompatCodes.TypeUnknown, ex.Message));
            return findings;
        }

        var xmlTypes = EnumerateXmlTypeNames(document).ToArray();
        var channelKeys = EnumerateChannelKeys(document).ToArray();
        foreach (var typeName in xmlTypes)
        {
            if (IsIgnoredXmlType(typeName) || tuiCatalog.ContainsKey(typeName))
            {
                continue;
            }

            findings.Add(new RoundTripFinding(
                planPath,
                TuiCompatCodes.TypeUnknown,
                $"TUI home cannot load type '{typeName}'."));
        }

        if (channelKeys.Length > 0
            && xmlTypes.Any(type => type.Contains("PresentationMixinBuilder", StringComparison.Ordinal)
                                    && !tuiCatalog.ContainsKey(type)))
        {
            findings.Add(new RoundTripFinding(
                planPath,
                TuiCompatCodes.MixinDropped,
                "Presentation mixin types are missing from the TUI home; ChannelKeys would drop."));
        }

        try
        {
            var savedKeys = RoundTripChannelKeys(planPath, tuiHome);
            if (channelKeys.Length > 0 && savedKeys.Length == 0)
            {
                findings.Add(new RoundTripFinding(
                    planPath,
                    TuiCompatCodes.MixinDropped,
                    "ChannelKey members were dropped after TUI Load/Save."));
            }
            else if (!channelKeys.ToHashSet(StringComparer.Ordinal).SetEquals(savedKeys))
            {
                findings.Add(new RoundTripFinding(
                    planPath,
                    TuiCompatCodes.XmlDrift,
                    "Normalized ChannelKeys drifted after TUI Load/Save."));
            }
        }
        catch (Exception ex)
        {
            findings.Add(new RoundTripFinding(planPath, TuiCompatCodes.TypeUnknown, ex.Message));
        }

        var contract = PlanContractValidator.Validate(
            [planPath],
            new PlanContractOptions
            {
                Strict = true,
                ExcludeVisaAdapter = true,
                TrustConfiguredPluginDirectories = true,
                Settings = new AppSettings
                {
                    OpenTapPluginDirectories = EnumeratePluginDirectories(tuiHome).ToList(),
                },
            });
        if (contract.HasErrors)
        {
            findings.Add(new RoundTripFinding(
                planPath,
                TuiCompatCodes.ContractFail,
                string.Join("; ", contract.Plans.SelectMany(p => p.Findings)
                    .Where(f => f.Severity == PlanContractSeverity.Error)
                    .Select(f => f.Message))));
        }

        return findings;
    }

    private static IEnumerable<string> EnumeratePackageDlls(string packageXmlPath)
    {
        var dir = Path.GetDirectoryName(packageXmlPath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            yield break;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(packageXmlPath);
        }
        catch
        {
            yield break;
        }

        foreach (var file in document.Descendants(PackageNs + "File"))
        {
            var relative = file.Attribute("Path")?.Value;
            if (string.IsNullOrWhiteSpace(relative)
                || !relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(dir, relative));
            if (File.Exists(full))
            {
                yield return full;
            }
        }
    }

    private static void AddPluginTypes(string dllPath, IDictionary<string, string> map)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(dllPath);
        }
        catch
        {
            return;
        }

        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
        catch
        {
            return;
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || !IsPluginType(type) || string.IsNullOrWhiteSpace(type.FullName))
            {
                continue;
            }

            map[type.FullName] = DisplayNameOf(type);
        }
    }

    private static bool IsPluginType(Type type)
        => typeof(ITestStep).IsAssignableFrom(type)
           || typeof(Instrument).IsAssignableFrom(type)
           || typeof(IMixinBuilder).IsAssignableFrom(type);

    private static string DisplayNameOf(Type type)
    {
        var display = type.GetCustomAttribute<DisplayAttribute>();
        return string.IsNullOrWhiteSpace(display?.Name) ? type.Name : display.Name;
    }

    internal static string[] ReadChannelKeys(string planPath)
        => EnumerateChannelKeys(XDocument.Load(planPath)).ToArray();

    internal static string[] RoundTripChannelKeys(string planPath, OpenTapHome tuiHome)
    {
        string? savedPath = null;
        try
        {
            return AuthoringPluginSearch.RunIsolated(
                EnumeratePluginDirectories(tuiHome),
                () =>
                {
                    var loaded = TestPlan.Load(planPath);
                    savedPath = Path.Combine(
                        Path.GetTempPath(),
                        "ht-tui-" + Guid.NewGuid().ToString("N") + ".TapPlan");
                    loaded.Save(savedPath);
                    return EnumerateChannelKeys(XDocument.Load(savedPath)).ToArray();
                });
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                try
                {
                    File.Delete(savedPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup of the round-trip temp plan.
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateXmlTypeNames(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            var raw = element.Attribute("type")?.Value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            yield return NormalizeTypeName(raw);
        }
    }

    private static IEnumerable<string> EnumerateChannelKeys(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            if (!element.Name.LocalName.EndsWith("ChannelKey", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = (element.Value ?? string.Empty).Trim();
            if (value.Length > 0)
            {
                yield return value;
            }
        }
    }

    private static string NormalizeTypeName(string raw)
    {
        var type = raw.Trim();
        const string embed = "emb:";
        if (type.StartsWith(embed, StringComparison.OrdinalIgnoreCase))
        {
            type = type[embed.Length..];
        }

        return type;
    }

    private static bool IsIgnoredXmlType(string typeName)
        => string.Equals(typeName, "OpenTap.TestPlan", StringComparison.Ordinal)
           || IsTuiAppType(typeName);

    internal static bool IsTuiAppType(string typeName)
        => typeName.StartsWith("OpenTap.TUI", StringComparison.Ordinal);

    internal static IEnumerable<string> EnumeratePluginDirectories(OpenTapHome home)
    {
        yield return home.Root;
        var packages = Path.Combine(home.Root, "Packages");
        if (!Directory.Exists(packages))
        {
            yield break;
        }

        foreach (var dir in Directory.EnumerateDirectories(packages))
        {
            yield return dir;
        }
    }
}
