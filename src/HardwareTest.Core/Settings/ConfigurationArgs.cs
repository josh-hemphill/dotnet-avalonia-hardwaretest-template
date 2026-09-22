using System.CommandLine;

namespace HardwareTest.Core.Settings;

/// Parsed CLI configuration flags (Avalonia args are filtered out separately).
public sealed class ConfigurationArgs
{
    public bool PrintConfig { get; init; }
    public bool PrintVersion { get; init; }
    /// Debug-only: fatal | recoverable | command.
    public string? SimulateCrash { get; init; }
    /// True when `--validate-plan` is present (even with no path). Never launches the UI.
    public bool ValidatePlan { get; init; }
    /// File or directory of `.TapPlan` files; empty/missing with <see cref="ValidatePlan"/> is usage.
    public string? ValidatePlanPath { get; init; }
    public string? SettingsPath { get; init; }
    public IReadOnlyDictionary<string, string> Overlays { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// Remaining args to forward to Avalonia.
    public IReadOnlyList<string> PassthroughArgs { get; init; } = [];

    public static ConfigurationArgs Parse(IReadOnlyList<string> args)
    {
        var root = new Command("HardwareTest", "HardwareTest") { TreatUnmatchedTokensAsErrors = false };
        var printConfigOption = new Option<bool>("--print-config");
        var printVersionOption = new Option<bool>("--version", "-v");
        var simulateCrashOption = new Option<string?>("--simulate-crash") { Arity = ArgumentArity.ZeroOrOne };
        var validatePlanOption = new Option<string?>("--validate-plan") { Arity = ArgumentArity.ZeroOrOne };
        var settingsOption = new Option<string?>("--settings");
        root.Add(printConfigOption);
        root.Add(printVersionOption);
        root.Add(simulateCrashOption);
        root.Add(validatePlanOption);
        root.Add(settingsOption);

        var overlayOptions = new Dictionary<SettingBinding, Option<string?>>();
        foreach (var binding in AppSettingsEnvironmentBinder.Bindings.Where(binding => binding.CliNames.Count > 0))
        {
            var option = new Option<string?>(binding.CliNames[0], binding.CliNames.Skip(1).ToArray());
            if (LooksBoolBinding(binding))
            {
                option.Arity = ArgumentArity.ZeroOrOne;
            }

            root.Add(option);
            overlayOptions[binding] = option;
        }

        var parsed = root.Parse(CliArgumentNormalizer.NormalizeOptionAliases(
            args,
            root.Options.SelectMany(option => option.Aliases.Prepend(option.Name))));
        var overlays = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (binding, option) in overlayOptions)
        {
            if (parsed.GetResult(option) is not null)
            {
                var value = parsed.GetValue(option);
                if (value is not null || LooksBoolBinding(binding))
                {
                    overlays[binding.Key] = value ?? "true";
                }
            }
        }

        var validatePlan = parsed.GetResult(validatePlanOption) is not null;
        var simulateCrash = parsed.GetResult(simulateCrashOption) is null
            ? null
            : parsed.GetValue(simulateCrashOption) ?? "fatal";

        return new ConfigurationArgs
        {
            PrintConfig = parsed.GetValue(printConfigOption),
            PrintVersion = parsed.GetValue(printVersionOption),
            SimulateCrash = simulateCrash,
            ValidatePlan = validatePlan,
            ValidatePlanPath = parsed.GetValue(validatePlanOption)
                ?? (args.Any(arg => string.Equals(arg, "--validate-plan=", StringComparison.OrdinalIgnoreCase))
                    ? string.Empty
                    : null),
            SettingsPath = parsed.GetValue(settingsOption),
            Overlays = overlays,
            PassthroughArgs = parsed.UnmatchedTokens,
        };
    }

    private static bool LooksBoolBinding(SettingBinding binding)
        => binding.Key is "UseMockVisa" or "EnableOsEventSink" or "EnableSyslogOnUnix"
            or "EmbedPlotsInReport" or "ExportOpenTapResults" or "ShowDutHistoryOnRun"
            or "IsEngineerDebugMode" or "CrashEnabled" or "RedactIdentifiersInDiagnostics"
            or "RequireDutConfirmEveryRun" or "PreferRemovableExport" or "AllowOsFolderBrowse"
            or "UseMockOperatorCredential" or "RequireCredentialForOperator"
            or "RequireAttestationBeforeExport" or "AllowPresenceInLieuOfSigning"
            or "ProbeBadgeWhenTechnicianFocused";

}
