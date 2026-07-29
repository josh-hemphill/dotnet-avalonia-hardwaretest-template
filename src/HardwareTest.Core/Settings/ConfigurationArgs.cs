namespace HardwareTest.Core.Settings;

/// Parsed CLI configuration flags (Avalonia args are filtered out separately).
public sealed class ConfigurationArgs
{
    public bool PrintConfig { get; init; }
    public string? SettingsPath { get; init; }
    public IReadOnlyDictionary<string, string> Overlays { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// Remaining args to forward to Avalonia.
    public IReadOnlyList<string> PassthroughArgs { get; init; } = [];

    public static ConfigurationArgs Parse(IReadOnlyList<string> args)
    {
        var overlays = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var passthrough = new List<string>();
        var printConfig = false;
        string? settingsPath = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--print-config", StringComparison.OrdinalIgnoreCase))
            {
                printConfig = true;
                continue;
            }

            if (TrySplit(arg, out var flag, out var inlineValue))
            {
                if (string.Equals(flag, "--settings", StringComparison.OrdinalIgnoreCase))
                {
                    settingsPath = inlineValue ?? TakeNext(args, ref i);
                    continue;
                }

                var binding = AppSettingsEnvironmentBinder.Bindings
                    .FirstOrDefault(b => b.CliNames.Any(n => string.Equals(n, flag, StringComparison.OrdinalIgnoreCase)));
                if (binding is not null)
                {
                    var value = inlineValue;
                    if (value is null)
                    {
                        // Bool flags may be bare (--mock-visa) meaning true.
                        if (LooksBoolBinding(binding) && (i + 1 >= args.Count || args[i + 1].StartsWith('-')))
                        {
                            value = "true";
                        }
                        else
                        {
                            value = TakeNext(args, ref i);
                        }
                    }

                    if (value is not null)
                    {
                        overlays[binding.Key] = value;
                    }

                    continue;
                }
            }

            passthrough.Add(arg);
        }

        return new ConfigurationArgs
        {
            PrintConfig = printConfig,
            SettingsPath = settingsPath,
            Overlays = overlays,
            PassthroughArgs = passthrough,
        };
    }

    private static bool LooksBoolBinding(SettingBinding binding)
        => binding.Key is "UseMockVisa" or "EnableOsEventSink" or "EnableSyslogOnUnix"
            or "EmbedPlotsInReport" or "ExportOpenTapResults" or "ShowDutHistoryOnRun"
            or "IsEngineerDebugMode";

    private static bool TrySplit(string arg, out string flag, out string? inlineValue)
    {
        flag = arg;
        inlineValue = null;
        if (!arg.StartsWith('-'))
        {
            return false;
        }

        var eq = arg.IndexOf('=');
        if (eq > 0)
        {
            flag = arg[..eq];
            inlineValue = arg[(eq + 1)..];
        }

        return true;
    }

    private static string? TakeNext(IReadOnlyList<string> args, ref int i)
    {
        if (i + 1 >= args.Count)
        {
            return null;
        }

        i++;
        return args[i];
    }
}
