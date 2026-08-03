using System.Collections;
using System.Text;

namespace HardwareTest.Core.Settings;

/// Two-stage configuration resolution.
/// Stage 1: env + command line only for DataDirectory / LogMinimumLevel (before logging / file I/O).
/// Stage 2: load settings.json, then re-apply env + command line on top.
public static class ConfigurationBootstrap
{
    public sealed class Stage1Result
    {
        public required string RootDirectory { get; init; }
        public required string LogMinimumLevel { get; init; }
        public required IReadOnlyList<SettingProvenance> Provenance { get; init; }
    }

    public sealed class ResolveResult
    {
        public required SettingsStore Store { get; init; }
        public required Stage1Result Stage1 { get; init; }
        public required IReadOnlyList<SettingProvenance> Provenance { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
    }

    /// Stage 1 — resolve root directory and log level from env + CLI only.
    public static Stage1Result ResolveStage1(
        ConfigurationArgs args,
        IDictionary? environment = null,
        string? defaultRoot = null)
    {
        var envMap = AppSettingsEnvironmentBinder.ReadEnvironment(environment);
        var settings = new AppSettings
        {
            DataDirectory = defaultRoot
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HardwareTest"),
            LogMinimumLevel = "Information",
        };
        var provenance = SeedDefaults(settings);
        var warnings = new List<string>();

        AppSettingsEnvironmentBinder.Apply(
            settings,
            provenance,
            SettingSource.Environment,
            envMap,
            warnings.Add);
        AppSettingsEnvironmentBinder.Apply(
            settings,
            provenance,
            SettingSource.CommandLine,
            args.Overlays,
            warnings.Add);

        if (!string.IsNullOrWhiteSpace(args.SettingsPath))
        {
            // --settings points at a file; root is that file's directory.
            var full = Path.GetFullPath(args.SettingsPath);
            settings.DataDirectory = Path.GetDirectoryName(full) ?? settings.DataDirectory;
        }

        if (string.IsNullOrWhiteSpace(settings.DataDirectory))
        {
            settings.DataDirectory = defaultRoot
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HardwareTest");
        }

        settings.DataDirectory = Path.GetFullPath(settings.DataDirectory);
        return new Stage1Result
        {
            RootDirectory = settings.DataDirectory,
            LogMinimumLevel = string.IsNullOrWhiteSpace(settings.LogMinimumLevel)
                ? "Information"
                : settings.LogMinimumLevel.Trim(),
            Provenance = provenance,
        };
    }

    /// Full resolve: stage1 root → load file → env → CLI.
    public static async Task<ResolveResult> ResolveAsync(
        ConfigurationArgs args,
        IDictionary? environment = null,
        string? defaultRoot = null,
        CancellationToken cancellationToken = default)
    {
        var stage1 = ResolveStage1(args, environment, defaultRoot);
        Directory.CreateDirectory(stage1.RootDirectory);

        var store = new SettingsStore(
            stage1.RootDirectory,
            settingsFilePath: string.IsNullOrWhiteSpace(args.SettingsPath) ? null : args.SettingsPath);

        var warnings = new List<string>();
        await store.LoadAsync(
            AppSettingsEnvironmentBinder.ReadEnvironment(environment),
            args.Overlays,
            warn: warnings.Add,
            cancellationToken).ConfigureAwait(false);

        return new ResolveResult
        {
            Store = store,
            Stage1 = stage1,
            Provenance = store.Provenance,
            Warnings = warnings,
        };
    }

    public static string FormatPrintConfig(SettingsStore store)
    {
        var sb = new StringBuilder();
        sb.AppendLine("HardwareTest effective configuration");
        sb.AppendLine($"RootDirectory={store.RootDirectory}");
        sb.AppendLine($"SettingsPath={store.SettingsPath}");
        sb.AppendLine($"PersistenceWritable={store.IsSettingsWritable}");
        if (!string.IsNullOrWhiteSpace(store.LastPersistenceError))
        {
            sb.AppendLine($"PersistenceError={store.LastPersistenceError}");
        }

        sb.AppendLine();
        sb.AppendLine("Key\tEffectiveValue\tSource\tSourceDetail");
        foreach (var row in store.Provenance.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(row.Key).Append('\t')
                .Append(Escape(row.EffectiveValue)).Append('\t')
                .Append(row.Source).Append('\t')
                .Append(Escape(row.SourceDetail)).AppendLine();
        }

        // Ensure every scalar binder key appears even if somehow missing from provenance.
        foreach (var binding in AppSettingsEnvironmentBinder.Bindings)
        {
            if (store.Provenance.Any(p => string.Equals(p.Key, binding.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            sb.Append(binding.Key).Append('\t')
                .Append(Escape(binding.Format(store.AppSettings))).Append('\t')
                .Append(SettingSource.Default).Append('\t')
                .AppendLine("missing-provenance");
        }

        return sb.ToString();
    }

    private static string Escape(string? value)
        => (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static List<SettingProvenance> SeedDefaults(AppSettings settings)
    {
        var list = new List<SettingProvenance>();
        foreach (var binding in AppSettingsEnvironmentBinder.Bindings)
        {
            list.Add(new SettingProvenance
            {
                Key = binding.Key,
                EffectiveValue = binding.Format(settings),
                Source = SettingSource.Default,
                RawValue = null,
                SourceDetail = "built-in default",
            });
        }

        return list;
    }
}
