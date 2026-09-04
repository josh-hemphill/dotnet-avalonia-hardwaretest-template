using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.PlanValidate;

public static class Program
{
    public static int Main(string[] args)
    {
        var pluginDirs = new List<string>();
        var targets = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (TrySplit(arg, out var flag, out var inline)
                && string.Equals(flag, "--opentap-plugin-dirs", StringComparison.OrdinalIgnoreCase))
            {
                var value = inline ?? TakeNext(args, ref i);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    pluginDirs.AddRange(value.Split(
                        [Path.PathSeparator, ';'],
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }

                continue;
            }

            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
            {
                return PlanContractCli.Run([], settings: null, Console.Out);
            }

            targets.Add(arg);
        }

        var settings = new AppSettings
        {
            UseMockVisa = true,
            OpenTapPluginDirectories = pluginDirs,
        };
        // Explicit --opentap-plugin-dirs on this authoring CLI are trusted for the process.
        // HardwareTest --validate-plan still uses appliance PluginDirectoryTrust.
        return PlanContractCli.Run(
            targets,
            settings,
            Console.Out,
            trustConfiguredPluginDirectories: pluginDirs.Count > 0);
    }

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
