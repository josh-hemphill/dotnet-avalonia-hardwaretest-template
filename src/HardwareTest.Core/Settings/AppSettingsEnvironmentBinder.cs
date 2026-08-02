using System.Collections;
using System.Globalization;
using System.Text;

namespace HardwareTest.Core.Settings;

/// Hand-written env/CLI binder for AppSettings (no Microsoft.Extensions.Configuration).
/// Precedence is applied by the caller: defaults → file → Environment → CommandLine.
public static class AppSettingsEnvironmentBinder
{
    public const string EnvPrefix = "HARDWARETEST_";
    /// Preserved legacy name (not HARDWARETEST_OPEN_TAP_PLUGIN_DIRECTORIES).
    public const string OpenTapPluginDirsEnv = "HARDWARETEST_OPENTAP_PLUGIN_DIRS";

    public static IReadOnlyList<SettingBinding> Bindings { get; } = BuildBindings();

    public static IReadOnlyDictionary<string, string> ReadEnvironment(IDictionary? env = null)
    {
        env ??= Environment.GetEnvironmentVariables();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (SettingBinding binding in Bindings)
        {
            if (binding.EnvNames.Count == 0)
            {
                continue;
            }

            foreach (var envName in binding.EnvNames)
            {
                var raw = GetEnv(env, envName);
                if (raw is null)
                {
                    continue;
                }

                map[binding.Key] = raw;
                break;
            }
        }

        // Indexed list overrides (Instruments__0__Id, …).
        foreach (System.Collections.DictionaryEntry entry in env)
        {
            var name = entry.Key?.ToString();
            if (name is null
                || !name.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase)
                || entry.Value is null)
            {
                continue;
            }

            if (TryMapIndexedEnv(name, out var key))
            {
                map[key] = Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        return map;
    }

    public static void Apply(
        AppSettings settings,
        IList<SettingProvenance> provenance,
        SettingSource source,
        IReadOnlyDictionary<string, string> values,
        Action<string>? warn = null)
    {
        foreach (var binding in Bindings)
        {
            if (!values.TryGetValue(binding.Key, out var raw) || raw is null)
            {
                continue;
            }

            if (!binding.TryApply(settings, raw, out var effective, out var error))
            {
                warn?.Invoke(
                    $"Ignoring {source} value for '{binding.Key}' from {Describe(source, binding)}: {error}");
                continue;
            }

            UpsertProvenance(
                provenance,
                binding.Key,
                effective,
                source,
                raw,
                Describe(source, binding));
        }

        ApplyIndexedLists(settings, provenance, source, values, warn);
    }

    public static string FormatEffective(AppSettings settings, string key)
    {
        var binding = Bindings.FirstOrDefault(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase));
        return binding?.Format(settings) ?? string.Empty;
    }

    public static bool IsOverridden(IReadOnlyList<SettingProvenance> provenance, string key)
        => provenance.Any(p =>
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)
            && p.Source is SettingSource.Environment or SettingSource.CommandLine);

    public static bool IsListOverridden(IReadOnlyList<SettingProvenance> provenance, string listKey)
        => provenance.Any(p =>
            (string.Equals(p.Key, listKey, StringComparison.OrdinalIgnoreCase)
             || p.Key.StartsWith(listKey + "[", StringComparison.OrdinalIgnoreCase)
             || p.Key.StartsWith(listKey + ".", StringComparison.OrdinalIgnoreCase))
            && p.Source is SettingSource.Environment or SettingSource.CommandLine);

    private static string Describe(SettingSource source, SettingBinding binding)
        => source switch
        {
            SettingSource.Environment => binding.EnvNames.FirstOrDefault() ?? binding.Key,
            SettingSource.CommandLine => binding.CliNames.FirstOrDefault() ?? binding.Key,
            SettingSource.SettingsFile => "settings.json",
            _ => "default",
        };

    private static void UpsertProvenance(
        IList<SettingProvenance> provenance,
        string key,
        string effective,
        SettingSource source,
        string? raw,
        string? detail)
    {
        for (var i = 0; i < provenance.Count; i++)
        {
            if (!string.Equals(provenance[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            provenance[i] = new SettingProvenance
            {
                Key = key,
                EffectiveValue = effective,
                Source = source,
                RawValue = raw,
                SourceDetail = detail,
            };
            return;
        }

        provenance.Add(new SettingProvenance
        {
            Key = key,
            EffectiveValue = effective,
            Source = source,
            RawValue = raw,
            SourceDetail = detail,
        });
    }

    private static string? GetEnv(IDictionary env, string name)
    {
        foreach (System.Collections.DictionaryEntry entry in env)
        {
            if (entry.Key is string key
                && string.Equals(key, name, StringComparison.OrdinalIgnoreCase)
                && entry.Value is not null)
            {
                return Convert.ToString(entry.Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static bool TryMapIndexedEnv(string envName, out string key)
    {
        key = string.Empty;
        // HARDWARETEST_INSTRUMENTS__0__ID → Instruments[0].Id
        var body = envName[EnvPrefix.Length..];
        var parts = body.Split("__", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        var list = SnakeToPascal(parts[0]);
        if (list is not ("Instruments" or "StationBindings" or "PlanSlotOverrides" or "PlanParameterOverrides"
            or "OpenTapPluginDirectories"))
        {
            return false;
        }

        if (list == "OpenTapPluginDirectories" && parts.Length == 2)
        {
            key = $"OpenTapPluginDirectories[{parts[1]}]";
            return true;
        }

        if (parts.Length < 3)
        {
            return false;
        }

        key = $"{list}[{parts[1]}].{SnakeToPascal(parts[2])}";
        return true;
    }

    private static string SnakeToPascal(string snake)
    {
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                continue;
            }

            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                sb.Append(part[1..].ToLowerInvariant());
            }
        }

        return sb.ToString();
    }

    private static void ApplyIndexedLists(
        AppSettings settings,
        IList<SettingProvenance> provenance,
        SettingSource source,
        IReadOnlyDictionary<string, string> values,
        Action<string>? warn)
    {
        ApplyStringList(settings.OpenTapPluginDirectories, "OpenTapPluginDirectories", provenance, source, values);
        ApplyObjectList(
            settings.Instruments,
            "Instruments",
            () => new VisaInstrument(),
            provenance,
            source,
            values,
            warn,
            (item, prop, raw) =>
            {
                switch (prop)
                {
                    case "Id": item.Id = raw; return true;
                    case "DisplayName": item.DisplayName = raw; return true;
                    case "Resource": item.Resource = raw; return true;
                    case "Enabled":
                        if (!TryParseBool(raw, out var enabled))
                        {
                            return false;
                        }

                        item.Enabled = enabled;
                        return true;
                    case "Notes": item.Notes = raw; return true;
                    default: return false;
                }
            });
        ApplyObjectList(
            settings.StationBindings,
            "StationBindings",
            () => new StationBinding(),
            provenance,
            source,
            values,
            warn,
            (item, prop, raw) =>
            {
                switch (prop)
                {
                    case "Role": item.Role = raw; return true;
                    case "InstrumentId": item.InstrumentId = raw; return true;
                    default: return false;
                }
            });
        ApplyObjectList(
            settings.PlanSlotOverrides,
            "PlanSlotOverrides",
            () => new PlanSlotOverride(),
            provenance,
            source,
            values,
            warn,
            (item, prop, raw) =>
            {
                switch (prop)
                {
                    case "PlanId": item.PlanId = raw; return true;
                    case "SlotName": item.SlotName = raw; return true;
                    case "RoleHint": item.RoleHint = raw; return true;
                    case "Resource": item.Resource = raw; return true;
                    default: return false;
                }
            });
        ApplyObjectList(
            settings.PlanParameterOverrides,
            "PlanParameterOverrides",
            () => new PlanParameterOverride(),
            provenance,
            source,
            values,
            warn,
            (item, prop, raw) =>
            {
                switch (prop)
                {
                    case "PlanId": item.PlanId = raw; return true;
                    case "MemberKey": item.MemberKey = raw; return true;
                    case "Value": item.Value = raw; return true;
                    default: return false;
                }
            });
    }

    private static void ApplyStringList(
        List<string> target,
        string listKey,
        IList<SettingProvenance> provenance,
        SettingSource source,
        IReadOnlyDictionary<string, string> values)
    {
        var indexed = values
            .Where(kv => kv.Key.StartsWith(listKey + "[", StringComparison.OrdinalIgnoreCase))
            .Select(kv =>
            {
                var start = kv.Key.IndexOf('[') + 1;
                var end = kv.Key.IndexOf(']');
                if (start <= 0 || end <= start
                    || !int.TryParse(kv.Key[start..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    return (-1, kv.Value);
                }

                return (i, kv.Value);
            })
            .Where(x => x.Item1 >= 0)
            .OrderBy(x => x.Item1)
            .ToArray();
        if (indexed.Length == 0)
        {
            return;
        }

        var max = indexed.Max(x => x.Item1);
        while (target.Count <= max)
        {
            target.Add(string.Empty);
        }

        foreach (var (index, value) in indexed)
        {
            target[index] = value;
            UpsertProvenance(
                provenance,
                $"{listKey}[{index}]",
                value,
                source,
                value,
                source == SettingSource.Environment ? $"{EnvPrefix}{ToSnake(listKey)}__{index}" : listKey);
        }
    }

    private static void ApplyObjectList<T>(
        List<T> target,
        string listKey,
        Func<T> factory,
        IList<SettingProvenance> provenance,
        SettingSource source,
        IReadOnlyDictionary<string, string> values,
        Action<string>? warn,
        Func<T, string, string, bool> applyProp)
    {
        var keyed = values
            .Where(kv => kv.Key.StartsWith(listKey + "[", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (keyed.Length == 0)
        {
            return;
        }

        foreach (var (mapKey, raw) in keyed)
        {
            // Instruments[0].Id
            var open = mapKey.IndexOf('[');
            var close = mapKey.IndexOf(']');
            var dot = mapKey.IndexOf('.', close + 1);
            if (open < 0 || close <= open || dot <= close
                || !int.TryParse(mapKey[(open + 1)..close], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                continue;
            }

            var prop = mapKey[(dot + 1)..];
            while (target.Count <= index)
            {
                target.Add(factory());
            }

            if (!applyProp(target[index], prop, raw))
            {
                warn?.Invoke($"Ignoring {source} value for '{mapKey}': could not parse '{raw}'.");
                continue;
            }

            UpsertProvenance(provenance, mapKey, raw, source, raw, mapKey);
        }
    }

    private static string ToSnake(string pascal)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append('_');
            }

            sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString();
    }

    internal static bool TryParseBool(string raw, out bool value)
    {
        value = false;
        var t = raw.Trim();
        if (bool.TryParse(t, out value))
        {
            return true;
        }

        if (t is "1" or "yes" or "on")
        {
            value = true;
            return true;
        }

        if (t is "0" or "no" or "off")
        {
            value = false;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<SettingBinding> BuildBindings()
        =>
        [
            SettingBinding.String(
                "DataDirectory",
                s => s.DataDirectory,
                (s, v) => s.DataDirectory = v,
                env: ["HARDWARETEST_DATA_DIRECTORY"],
                cli: ["--data-directory"]),
            SettingBinding.String(
                "DefaultVisaResource",
                s => s.DefaultVisaResource,
                (s, v) => s.DefaultVisaResource = v,
                env: ["HARDWARETEST_DEFAULT_VISA_RESOURCE"],
                cli: ["--default-visa-resource"]),
            SettingBinding.Bool(
                "UseMockVisa",
                s => s.UseMockVisa,
                (s, v) => s.UseMockVisa = v,
                env: ["HARDWARETEST_USE_MOCK_VISA"],
                cli: ["--mock-visa"]),
            SettingBinding.String(
                "LogMinimumLevel",
                s => s.LogMinimumLevel,
                (s, v) => s.LogMinimumLevel = v,
                env: ["HARDWARETEST_LOG_MINIMUM_LEVEL"],
                cli: ["--log-level"]),
            SettingBinding.Bool(
                "EnableOsEventSink",
                s => s.EnableOsEventSink,
                (s, v) => s.EnableOsEventSink = v,
                env: ["HARDWARETEST_ENABLE_OS_EVENT_SINK"],
                cli: ["--enable-os-event-sink"]),
            SettingBinding.Bool(
                "EnableSyslogOnUnix",
                s => s.EnableSyslogOnUnix,
                (s, v) => s.EnableSyslogOnUnix = v,
                env: ["HARDWARETEST_ENABLE_SYSLOG_ON_UNIX"],
                cli: ["--enable-syslog"]),
            SettingBinding.String(
                "SyslogHost",
                s => s.SyslogHost ?? string.Empty,
                (s, v) => s.SyslogHost = v,
                env: ["HARDWARETEST_SYSLOG_HOST"],
                cli: ["--syslog-host"]),
            SettingBinding.Int(
                "SyslogPort",
                s => s.SyslogPort,
                (s, v) => s.SyslogPort = v,
                env: ["HARDWARETEST_SYSLOG_PORT"],
                cli: ["--syslog-port"]),
            SettingBinding.Int(
                "PlotRefreshHz",
                s => s.PlotRefreshHz,
                (s, v) => s.PlotRefreshHz = v,
                env: ["HARDWARETEST_PLOT_REFRESH_HZ"],
                cli: ["--plot-refresh-hz"]),
            SettingBinding.String(
                "ThemePreference",
                s => s.ThemePreference,
                (s, v) => s.ThemePreference = v,
                env: ["HARDWARETEST_THEME_PREFERENCE"],
                cli: ["--theme"]),
            SettingBinding.Bool(
                "EmbedPlotsInReport",
                s => s.EmbedPlotsInReport,
                (s, v) => s.EmbedPlotsInReport = v,
                env: ["HARDWARETEST_EMBED_PLOTS_IN_REPORT"],
                cli: ["--embed-plots"]),
            SettingBinding.Bool(
                "ExportOpenTapResults",
                s => s.ExportOpenTapResults,
                (s, v) => s.ExportOpenTapResults = v,
                env: ["HARDWARETEST_EXPORT_OPENTAP_RESULTS"],
                cli: ["--export-opentap-results"]),
            SettingBinding.Bool(
                "ShowDutHistoryOnRun",
                s => s.ShowDutHistoryOnRun,
                (s, v) => s.ShowDutHistoryOnRun = v,
                env: ["HARDWARETEST_SHOW_DUT_HISTORY_ON_RUN"],
                cli: ["--show-dut-history-on-run"]),
            SettingBinding.Int(
                "OperatorSessionIdleHours",
                s => s.OperatorSessionIdleHours,
                (s, v) => s.OperatorSessionIdleHours = v,
                env: ["HARDWARETEST_OPERATOR_SESSION_IDLE_HOURS"],
                cli: ["--session-idle-hours"]),
            SettingBinding.Int(
                "OperatorSessionIdleMinutes",
                s => s.OperatorSessionIdleMinutes,
                (s, v) => s.OperatorSessionIdleMinutes = v,
                env: ["HARDWARETEST_OPERATOR_SESSION_IDLE_MINUTES"],
                cli: ["--session-idle-minutes"]),
            SettingBinding.Int(
                "OperatorSessionIdleWarnPercent",
                s => s.OperatorSessionIdleWarnPercent,
                (s, v) => s.OperatorSessionIdleWarnPercent = v,
                env: ["HARDWARETEST_OPERATOR_SESSION_IDLE_WARN_PERCENT"],
                cli: ["--session-idle-warn-percent"]),
            SettingBinding.Bool(
                "RequireDutConfirmEveryRun",
                s => s.RequireDutConfirmEveryRun,
                (s, v) => s.RequireDutConfirmEveryRun = v,
                env: ["HARDWARETEST_REQUIRE_DUT_CONFIRM_EVERY_RUN"],
                cli: ["--require-dut-confirm-every-run"]),
            SettingBinding.Bool(
                "IsEngineerDebugMode",
                s => s.IsEngineerDebugMode,
                (s, v) => s.IsEngineerDebugMode = v,
                env: ["HARDWARETEST_ENGINEER_DEBUG"],
                cli: ["--engineer-debug"]),
            SettingBinding.StringList(
                "OpenTapPluginDirectories",
                s => s.OpenTapPluginDirectories,
                (s, v) => s.OpenTapPluginDirectories = v,
                env: [OpenTapPluginDirsEnv],
                cli: ["--opentap-plugin-dirs"]),
            SettingBinding.String(
                "ReportTemplateName",
                s => s.ReportTemplateName,
                (s, v) => s.ReportTemplateName = v,
                env: ["HARDWARETEST_REPORT_TEMPLATE_NAME"],
                cli: ["--report-template"]),
            SettingBinding.Bool(
                "CrashEnabled",
                s => s.CrashEnabled,
                (s, v) => s.CrashEnabled = v,
                env: ["HARDWARETEST_CRASH_ENABLED"],
                cli: ["--crash-enabled"]),
            SettingBinding.String(
                "CrashDirectory",
                s => s.CrashDirectory,
                (s, v) => s.CrashDirectory = v,
                env: ["HARDWARETEST_CRASH_DIRECTORY"],
                cli: ["--crash-directory"]),
            SettingBinding.Int(
                "CrashRetentionCount",
                s => s.CrashRetentionCount,
                (s, v) => s.CrashRetentionCount = v,
                env: ["HARDWARETEST_CRASH_RETENTION_COUNT"],
                cli: ["--crash-retention"]),
            SettingBinding.Bool(
                "RedactIdentifiersInDiagnostics",
                s => s.RedactIdentifiersInDiagnostics,
                (s, v) => s.RedactIdentifiersInDiagnostics = v,
                env: ["HARDWARETEST_REDACT_IDENTIFIERS"],
                cli: ["--redact-identifiers"]),
            SettingBinding.String(
                "ExportDirectory",
                s => s.ExportDirectory,
                (s, v) => s.ExportDirectory = v,
                env: ["HARDWARETEST_EXPORT_DIRECTORY"],
                cli: ["--export-directory"]),
            SettingBinding.Bool(
                "PreferRemovableExport",
                s => s.PreferRemovableExport,
                (s, v) => s.PreferRemovableExport = v,
                env: ["HARDWARETEST_PREFER_REMOVABLE_EXPORT"],
                cli: ["--prefer-removable-export"]),
            SettingBinding.Int(
                "RunRetentionDays",
                s => s.RunRetentionDays,
                (s, v) => s.RunRetentionDays = v,
                env: ["HARDWARETEST_RUN_RETENTION_DAYS"],
                cli: ["--run-retention-days"]),
            SettingBinding.Int(
                "RunRetentionMaxRuns",
                s => s.RunRetentionMaxRuns,
                (s, v) => s.RunRetentionMaxRuns = v,
                env: ["HARDWARETEST_RUN_RETENTION_MAX_RUNS"],
                cli: ["--run-retention-max-runs"]),
            SettingBinding.Long(
                "DataFreeSpaceWarnBytes",
                s => s.DataFreeSpaceWarnBytes,
                (s, v) => s.DataFreeSpaceWarnBytes = v,
                env: ["HARDWARETEST_DATA_FREE_SPACE_WARN_BYTES"],
                cli: ["--data-free-space-warn-bytes"]),
            SettingBinding.Long(
                "DataFreeSpaceCriticalBytes",
                s => s.DataFreeSpaceCriticalBytes,
                (s, v) => s.DataFreeSpaceCriticalBytes = v,
                env: ["HARDWARETEST_DATA_FREE_SPACE_CRITICAL_BYTES"],
                cli: ["--data-free-space-critical-bytes"]),
            SettingBinding.Bool(
                "AllowOsFolderBrowse",
                s => s.AllowOsFolderBrowse,
                (s, v) => s.AllowOsFolderBrowse = v,
                env: ["HARDWARETEST_ALLOW_OS_FOLDER_BROWSE"],
                cli: ["--allow-os-folder-browse"]),
        ];
}

/// One overridable scalar/list setting entry.
public sealed class SettingBinding
{
    private readonly Func<AppSettings, string> _format;
    private readonly Func<AppSettings, string, (bool Ok, string Effective, string? Error)> _apply;

    private SettingBinding(
        string key,
        IReadOnlyList<string> envNames,
        IReadOnlyList<string> cliNames,
        Func<AppSettings, string> format,
        Func<AppSettings, string, (bool, string, string?)> apply)
    {
        Key = key;
        EnvNames = envNames;
        CliNames = cliNames;
        _format = format;
        _apply = apply;
    }

    public string Key { get; }
    public IReadOnlyList<string> EnvNames { get; }
    public IReadOnlyList<string> CliNames { get; }

    public string Format(AppSettings settings) => _format(settings);

    public bool TryApply(AppSettings settings, string raw, out string effective, out string? error)
    {
        var (ok, eff, err) = _apply(settings, raw);
        effective = eff;
        error = err;
        return ok;
    }

    public static SettingBinding String(
        string key,
        Func<AppSettings, string> get,
        Action<AppSettings, string> set,
        string[] env,
        string[] cli)
        => new(
            key,
            env,
            cli,
            get,
            (s, raw) =>
            {
                set(s, raw);
                return (true, raw, null);
            });

    public static SettingBinding Bool(
        string key,
        Func<AppSettings, bool> get,
        Action<AppSettings, bool> set,
        string[] env,
        string[] cli)
        => new(
            key,
            env,
            cli,
            s => get(s) ? "true" : "false",
            (s, raw) =>
            {
                if (!AppSettingsEnvironmentBinder.TryParseBool(raw, out var value))
                {
                    return (false, get(s) ? "true" : "false", $"expected bool, got '{raw}'");
                }

                set(s, value);
                return (true, value ? "true" : "false", null);
            });

    public static SettingBinding Int(
        string key,
        Func<AppSettings, int> get,
        Action<AppSettings, int> set,
        string[] env,
        string[] cli)
        => new(
            key,
            env,
            cli,
            s => get(s).ToString(CultureInfo.InvariantCulture),
            (s, raw) =>
            {
                if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return (false, get(s).ToString(CultureInfo.InvariantCulture), $"expected int, got '{raw}'");
                }

                set(s, value);
                return (true, value.ToString(CultureInfo.InvariantCulture), null);
            });

    public static SettingBinding Long(
        string key,
        Func<AppSettings, long> get,
        Action<AppSettings, long> set,
        string[] env,
        string[] cli)
        => new(
            key,
            env,
            cli,
            s => get(s).ToString(CultureInfo.InvariantCulture),
            (s, raw) =>
            {
                if (!long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return (false, get(s).ToString(CultureInfo.InvariantCulture), $"expected long, got '{raw}'");
                }

                set(s, value);
                return (true, value.ToString(CultureInfo.InvariantCulture), null);
            });

    public static SettingBinding StringList(
        string key,
        Func<AppSettings, List<string>> get,
        Action<AppSettings, List<string>> set,
        string[] env,
        string[] cli)
        => new(
            key,
            env,
            cli,
            s => string.Join(Path.PathSeparator.ToString(), get(s)),
            (s, raw) =>
            {
                var parts = raw
                    .Split([Path.PathSeparator, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                set(s, parts);
                return (true, string.Join(Path.PathSeparator.ToString(), parts), null);
            });
}
