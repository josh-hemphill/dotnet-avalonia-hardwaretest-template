using System.CommandLine;
using HardwareTest.Core.Settings;
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
        var root = new Command("HardwareTest.Authoring", "HardwareTest authoring commands");
        var bootstrapOption = new Option<bool>("--bootstrap");
        var validateOption = new Option<bool>("--validate");
        var packOption = new Option<bool>("--pack");
        var compatOption = new Option<bool>("--compat");
        var evalOption = new Option<bool>("--eval-formulas");
        var offlineOption = new Option<bool>("--offline");
        var strictOption = new Option<bool>("--strict");
        var outputOption = new Option<string?>("--out");
        var openTapHomeOption = new Option<string?>("--opentap-home");
        var formatOption = new Option<string?>("--format");
        var helpOption = new Option<bool>("--help", "-h");
        var workspaceArgument = new Argument<string[]>("workspace") { Arity = ArgumentArity.ZeroOrMore };
        root.Add(bootstrapOption);
        root.Add(validateOption);
        root.Add(packOption);
        root.Add(compatOption);
        root.Add(evalOption);
        root.Add(offlineOption);
        root.Add(strictOption);
        root.Add(outputOption);
        root.Add(openTapHomeOption);
        root.Add(formatOption);
        root.Add(helpOption);
        root.Add(workspaceArgument);

        var parsed = root.Parse(CliArgumentNormalizer.NormalizeOptionAliases(
            args,
            root.Options.SelectMany(option => option.Aliases.Prepend(option.Name))));
        if (parsed.Errors.Count > 0)
        {
            error.WriteLine(parsed.Errors[0].Message);
            WriteUsage(output);
            return UsageExitCode;
        }

        if (parsed.GetValue(helpOption))
        {
            WriteUsage(output);
            return UsageExitCode;
        }

        var commands = new[]
        {
            (Option: bootstrapOption, Command: AuthoringCliCommand.Bootstrap),
            (Option: validateOption, Command: AuthoringCliCommand.Validate),
            (Option: packOption, Command: AuthoringCliCommand.Pack),
            (Option: compatOption, Command: AuthoringCliCommand.Compat),
            (Option: evalOption, Command: AuthoringCliCommand.EvalFormulas),
        };
        var selected = commands.Where(item => parsed.GetValue(item.Option)).ToArray();
        if (selected.Length != 1)
        {
            error.WriteLine(selected.Length == 0 ? "A command flag is required." : "Specify only one command flag.");
            WriteUsage(output);
            return UsageExitCode;
        }

        var workspace = parsed.GetValue(workspaceArgument)?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(workspace))
        {
            WriteUsage(output);
            return UsageExitCode;
        }

        if (!TryParseFormat(parsed.GetValue(formatOption) ?? "text", out var format))
        {
            error.WriteLine("Unknown --format. Use text, json, or sarif.");
            return UsageExitCode;
        }

        return selected[0].Command switch
        {
            AuthoringCliCommand.Bootstrap => RunBootstrap(workspace, ResolveOpenTapHome(parsed.GetValue(openTapHomeOption)), parsed.GetValue(offlineOption), output),
            AuthoringCliCommand.Validate => RunValidate(workspace, parsed.GetValue(strictOption), format, output),
            AuthoringCliCommand.Pack => RunPack(workspace, parsed.GetValue(outputOption), ResolveOpenTapHome(parsed.GetValue(openTapHomeOption)), parsed.GetValue(offlineOption), output, error),
            AuthoringCliCommand.Compat => RunCompat(workspace, ResolveOpenTapHome(parsed.GetValue(openTapHomeOption)), parsed.GetValue(offlineOption), output, error),
            AuthoringCliCommand.EvalFormulas => RunEvalFormulas(workspace, output, error),
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

    private static int RunEvalFormulas(string workspaceRoot, TextWriter output, TextWriter error)
    {
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var draft = new PlanCompiler().LoadAll(workspace);
        var datasets = RunDatasetCatalog.List(workspace);
        var failed = false;
        foreach (var dataset in datasets)
        {
            var program = draft.Programs.FirstOrDefault(item =>
                string.Equals(item.PlanId, dataset.Run.PlanId, StringComparison.OrdinalIgnoreCase));
            if (program is null)
            {
                output.WriteLine($"skip {dataset.Run.PlanId} {dataset.Path} no matching program");
                continue;
            }

            try
            {
                var results = FormulaDatasetEval.EvaluateProgram(program, dataset.Run);
                foreach (var sample in results)
                {
                    output.WriteLine(
                        $"ok {program.PlanId} {dataset.Path} {sample.EffectiveMetricKey}={sample.Value}");
                }
            }
            catch (AuthoringWorkspaceException ex)
            {
                error.WriteLine($"{dataset.Path}: {ex.Message}");
                failed = true;
            }
        }

        return failed ? 1 : 0;
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("HardwareTest.Authoring <workspace>");
        output.WriteLine("HardwareTest.Authoring --bootstrap <workspace> [--opentap-home DIR] [--offline]");
        output.WriteLine("HardwareTest.Authoring --validate <workspace> [--strict] [--format text|json|sarif]");
        output.WriteLine("HardwareTest.Authoring --compat <workspace>");
        output.WriteLine("HardwareTest.Authoring --pack <workspace> --out dist/");
        output.WriteLine("HardwareTest.Authoring --eval-formulas <workspace>");
        output.WriteLine("HardwareTest.Authoring --help");
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

    /// CLI --opentap-home wins; otherwise authoring-preferences.json OpenTapHomeOverride.
    internal static string? ResolveOpenTapHome(string? flag, IAuthoringPreferencesStore? store = null)
    {
        if (!string.IsNullOrWhiteSpace(flag))
        {
            return flag;
        }

        try
        {
            var prefs = store ?? new AuthoringPreferencesStore();
            if (store is null)
            {
                prefs.Load();
            }

            return string.IsNullOrWhiteSpace(prefs.Current.OpenTapHomeOverride)
                ? null
                : prefs.Current.OpenTapHomeOverride;
        }
        catch (AuthoringPreferencesException)
        {
            return null;
        }
    }

    private enum AuthoringCliCommand
    {
        Help,
        Bootstrap,
        Validate,
        Pack,
        Compat,
        EvalFormulas,
    }
}
