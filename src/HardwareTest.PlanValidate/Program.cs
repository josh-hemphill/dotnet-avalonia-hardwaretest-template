using System.CommandLine;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.PlanValidate;

public static class Program
{
    public static int Main(string[] args)
    {
        var root = new Command("HardwareTest.PlanValidate", "Validate HardwareTest OpenTAP plans");
        var pluginDirsOption = new Option<string?>("--opentap-plugin-dirs");
        var strictOption = new Option<bool>("--strict");
        var formatOption = new Option<string?>("--format");
        var helpOption = new Option<bool>("--help", "-h");
        var targetsArgument = new Argument<string[]>("targets") { Arity = ArgumentArity.ZeroOrMore };
        root.Add(pluginDirsOption);
        root.Add(strictOption);
        root.Add(formatOption);
        root.Add(helpOption);
        root.Add(targetsArgument);

        var parsed = root.Parse(CliArgumentNormalizer.NormalizeOptionAliases(
            args,
            root.Options.SelectMany(option => option.Aliases.Prepend(option.Name))));
        if (parsed.Errors.Count > 0)
        {
            Console.Error.WriteLine(parsed.Errors[0].Message);
            return PlanContractCli.UsageExitCode;
        }

        if (parsed.GetValue(helpOption))
        {
            return PlanContractCli.Run([], settings: null, Console.Out);
        }

        if (!TryParseFormat(parsed.GetValue(formatOption) ?? "text", out var format))
        {
            Console.Error.WriteLine("Unknown --format. Use text, json, or sarif.");
            return PlanContractCli.UsageExitCode;
        }

        var pluginDirs = (parsed.GetValue(pluginDirsOption) ?? string.Empty).Split(
            [Path.PathSeparator, ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var targets = parsed.GetValue(targetsArgument) ?? [];

        var settings = new AppSettings
        {
            UseMockVisa = true,
            OpenTapPluginDirectories = [.. pluginDirs],
        };
        // Explicit --opentap-plugin-dirs on this authoring CLI are trusted for the process.
        // HardwareTest --validate-plan still uses appliance PluginDirectoryTrust.
        return PlanContractCli.Run(
            targets,
            Console.Out,
            new PlanContractOptions
            {
                Settings = settings,
                TrustConfiguredPluginDirectories = pluginDirs.Length > 0,
                Strict = parsed.GetValue(strictOption),
                Format = format,
            });
    }

    private static bool TryParseFormat(string? value, out PlanContractFormat format)
    {
        format = PlanContractFormat.Text;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value, "text", StringComparison.OrdinalIgnoreCase))
        {
            format = PlanContractFormat.Text;
            return true;
        }

        if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
        {
            format = PlanContractFormat.Json;
            return true;
        }

        if (string.Equals(value, "sarif", StringComparison.OrdinalIgnoreCase))
        {
            format = PlanContractFormat.Sarif;
            return true;
        }

        return false;
    }
}
