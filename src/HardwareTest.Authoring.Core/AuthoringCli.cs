using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Headless bootstrap / validate / pack / compat flags. Does not start Avalonia.
public static class AuthoringCli
{
    public const int UsageExitCode = PlanContractCli.UsageExitCode;

    public static bool IsHeadless(IReadOnlyList<string> args)
        => args.Any(arg => arg.StartsWith('-'));

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            return RunCore(args, output, error);
        }
        catch (AuthoringWorkspaceException ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunCore(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        var command = AuthoringCliCommand.Help;
        string? workspace = null;
        string? outputDirectory = null;
        string? openTapHome = null;
        var offline = false;
        var strict = false;
        var format = PlanContractFormat.Text;
        var positionals = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
            {
                command = AuthoringCliCommand.Help;
                break;
            }

            if (string.Equals(arg, "--bootstrap", StringComparison.OrdinalIgnoreCase))
            {
                command = AuthoringCliCommand.Bootstrap;
                continue;
            }

            if (string.Equals(arg, "--validate", StringComparison.OrdinalIgnoreCase))
            {
                command = AuthoringCliCommand.Validate;
                continue;
            }

            if (string.Equals(arg, "--pack", StringComparison.OrdinalIgnoreCase))
            {
                command = AuthoringCliCommand.Pack;
                continue;
            }

            if (string.Equals(arg, "--compat", StringComparison.OrdinalIgnoreCase))
            {
                command = AuthoringCliCommand.Compat;
                continue;
            }

            if (string.Equals(arg, "--offline", StringComparison.OrdinalIgnoreCase))
            {
                offline = true;
                continue;
            }

            if (string.Equals(arg, "--strict", StringComparison.OrdinalIgnoreCase))
            {
                strict = true;
                continue;
            }

            if (TrySplit(arg, out var flag, out var inline)
                && string.Equals(flag, "--out", StringComparison.OrdinalIgnoreCase))
            {
                outputDirectory = inline ?? TakeNext(args, ref i);
                continue;
            }

            if (TrySplit(arg, out flag, out inline)
                && string.Equals(flag, "--opentap-home", StringComparison.OrdinalIgnoreCase))
            {
                openTapHome = inline ?? TakeNext(args, ref i);
                continue;
            }

            if (TrySplit(arg, out flag, out inline)
                && string.Equals(flag, "--format", StringComparison.OrdinalIgnoreCase))
            {
                var value = inline ?? TakeNext(args, ref i);
                if (!TryParseFormat(value, out format))
                {
                    error.WriteLine("Unknown --format. Use text, json, or sarif.");
                    return UsageExitCode;
                }

                continue;
            }

            if (arg.StartsWith('-'))
            {
                error.WriteLine($"Unknown flag: {arg}");
                WriteUsage(output);
                return UsageExitCode;
            }

            positionals.Add(arg);
        }

        if (command == AuthoringCliCommand.Help)
        {
            WriteUsage(output);
            return UsageExitCode;
        }

        workspace = positionals.Count > 0 ? positionals[0] : workspace;
        if (string.IsNullOrWhiteSpace(workspace))
        {
            WriteUsage(output);
            return UsageExitCode;
        }

        return command switch
        {
            AuthoringCliCommand.Bootstrap => RunBootstrap(workspace, openTapHome, offline, output),
            AuthoringCliCommand.Validate => RunValidate(workspace, strict, format, output),
            AuthoringCliCommand.Pack => RunPack(workspace, outputDirectory, openTapHome, offline, output, error),
            AuthoringCliCommand.Compat => RunCompat(workspace, openTapHome, offline, output, error),
            _ => UsageExitCode,
        };
    }

    private static int RunBootstrap(string workspaceRoot, string? homeDirectory, bool offline, TextWriter output)
    {
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions
            {
                HomeDirectory = homeDirectory,
                Offline = offline,
            });
        output.WriteLine(home.Root);
        return 0;
    }

    private static int RunValidate(string workspaceRoot, bool strict, PlanContractFormat format, TextWriter output)
    {
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        return PlanContractCli.Run(
            workspace.TapPlanPaths,
            output,
            new PlanContractOptions
            {
                Strict = strict,
                Format = format,
                ExcludeVisaAdapter = true,
            });
    }

    private static int RunPack(
        string workspaceRoot,
        string? outputDirectory,
        string? homeDirectory,
        bool offline,
        TextWriter output,
        TextWriter error)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            error.WriteLine("--pack requires --out <directory>.");
            WriteUsage(output);
            return UsageExitCode;
        }

        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        OpenTapHome? home = null;
        if (!string.IsNullOrWhiteSpace(homeDirectory))
        {
            home = new OpenTapHome(Path.GetFullPath(homeDirectory));
        }

        var manifest = WorkspacePacker.Pack(
            workspace,
            outputDirectory,
            new PackOptions
            {
                Home = home,
                TuiHome = home,
                Offline = offline,
                Compat = new TuiCompatChecker(),
            });
        output.WriteLine($"{manifest.PackageName} {manifest.Version}");
        foreach (var file in manifest.Files)
        {
            output.WriteLine(file);
        }

        return 0;
    }

    private static int RunCompat(
        string workspaceRoot,
        string? homeDirectory,
        bool offline,
        TextWriter output,
        TextWriter error)
    {
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions
            {
                HomeDirectory = homeDirectory,
                Offline = offline,
            });
        var report = new TuiCompatChecker().Compare(workspace, home, home);
        foreach (var delta in report.Catalog)
        {
            output.WriteLine($"catalog {delta.MissingOn} {delta.TypeName}");
        }

        foreach (var finding in report.RoundTrips)
        {
            output.WriteLine($"{finding.Code} {Path.GetFileName(finding.PlanPath)} {finding.Message}");
        }

        if (report.BlocksPack())
        {
            error.WriteLine("TUI compatibility report blocks pack.");
            return 1;
        }

        output.WriteLine("TUI compatibility ok.");
        return 0;
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("HardwareTest.Authoring <workspace>");
        output.WriteLine("HardwareTest.Authoring --bootstrap <workspace> [--opentap-home DIR] [--offline]");
        output.WriteLine("HardwareTest.Authoring --validate <workspace> [--strict] [--format text|json|sarif]");
        output.WriteLine("HardwareTest.Authoring --compat <workspace>");
        output.WriteLine("HardwareTest.Authoring --pack <workspace> --out dist/");
        output.WriteLine("HardwareTest.Authoring --help");
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

    private enum AuthoringCliCommand
    {
        Help,
        Bootstrap,
        Validate,
        Pack,
        Compat,
    }
}
