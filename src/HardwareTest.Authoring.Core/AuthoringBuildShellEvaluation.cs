using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    private sealed record ShellEvaluation(string[] References, string[] Imports, string[] Inputs,
        string ExtensionsPath, bool HasPackages);

    private static ShellEvaluation EvaluateShell(string project, string sdkRoot, IReadOnlyCollection<string> packageFolders)
    {
        ValidateShellEvaluationFiles(project);
        var json = RunShellDotNet(Path.GetDirectoryName(project)!, ["msbuild", project, "-nologo", "-p:Configuration=Release",
            "-getProperty:MSBuildProjectExtensionsPath,DirectoryBuildPropsPath,DirectoryBuildTargetsPath,DirectoryPackagesPropsPath",
            "-getItem:ProjectReference,PackageReference,Compile,None,Content,EmbeddedResource,AdditionalFiles,Analyzer,AnalyzerConfigFiles,EditorConfigFiles,Resource,AvaloniaResource,Reference"]);
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.GetProperty("Items");
        string[] Paths(string item)
        {
            var paths = new List<string>();
            foreach (var entry in items.GetProperty(item).EnumerateArray())
            {
                if (!entry.TryGetProperty("FullPath", out var fullPath)) continue;
                var path = Path.GetFullPath(fullPath.GetString()!);
                // The SDK discovers ancestor editor configurations as absolute items.
                // CaptureShellInputs preserves that bounded hierarchy beneath staging.
                var ancestorConfiguration = item is "EditorConfigFiles" or "AnalyzerConfigFiles"
                    && Path.GetFileName(path) is ".editorconfig" or ".globalconfig"
                    && ShellContains(Path.GetDirectoryName(path)!, project);
                if (Path.IsPathRooted(entry.GetProperty("Identity").GetString()!)
                    && !ancestorConfiguration && !ShellContains(sdkRoot, path) && !packageFolders.Any(f => ShellContains(f, path)))
                    Unsupported(project, $"evaluated {item} input '{path}' cannot be relocated");
                paths.Add(path);
            }
            return paths.ToArray();
        }
        var properties = document.RootElement.GetProperty("Properties");
        var imports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in new[] { "DirectoryBuildPropsPath", "DirectoryBuildTargetsPath", "DirectoryPackagesPropsPath" })
        {
            var value = properties.GetProperty(property).GetString();
            if (!string.IsNullOrWhiteSpace(value) && File.Exists(value)) imports.Add(Path.GetFullPath(value));
        }
        // Preprocessing expands imports without invoking targets, including imports from central configs.
        var temporary = Path.Combine(Path.GetTempPath(), "authoring-shell-evaluation-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            RunShellDotNet(Path.GetDirectoryName(project)!, ["msbuild", project, "-nologo", "-p:Configuration=Release", "-preprocess:" + temporary]);
            foreach (var line in File.ReadLines(temporary))
            {
                var path = line.Trim();
                if (!Path.IsPathRooted(path) || !File.Exists(path)) continue;
                path = Path.GetFullPath(path);
                if (!ShellContains(sdkRoot, path) && !packageFolders.Any(f => ShellContains(f, path))
                    && !path.Split(Path.DirectorySeparatorChar).Contains("obj", StringComparer.Ordinal)) imports.Add(path);
            }
        }
        finally { File.Delete(temporary); }
        var projectFiles = imports.Append(project).ToArray();
        var rawInputs = projectFiles.SelectMany(p => XDocument.Load(p).Descendants())
            .Where(e => e.Name.LocalName is "Compile" or "None" or "Content" or "EmbeddedResource" or "AdditionalFiles" or "Analyzer" or "AnalyzerConfigFiles" or "EditorConfigFiles" or "Resource" or "AvaloniaResource")
            .SelectMany(e => ((string?)e.Attribute("Include") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Where(v => !v.Contains('*') && !v.Contains('?'))
            .Select(v => Resolve(Path.GetDirectoryName(project)!, v.Replace('\\', Path.DirectorySeparatorChar))).ToArray();
        foreach (var input in rawInputs)
            if (!File.Exists(input)) Unsupported(project, "explicit source items must exist when captured, including conditional items");
        var declaredFiles = projectFiles.SelectMany(p => XDocument.Load(p).Descendants())
            .Where(e => e.Parent?.Name.LocalName == "ItemGroup"
                && e.Name.LocalName is not ("ProjectReference" or "PackageReference" or "PackageVersion" or "Reference"))
            .SelectMany(e => ((string?)e.Attribute("Include") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Where(v => !v.Contains('*') && !v.Contains('?'))
            .Select(v => Resolve(Path.GetDirectoryName(project)!, v.Replace('\\', Path.DirectorySeparatorChar)))
            .Where(File.Exists);
        var inputs = new[] { "Compile", "None", "Content", "EmbeddedResource", "AdditionalFiles", "Analyzer",
            "AnalyzerConfigFiles", "EditorConfigFiles", "Resource", "AvaloniaResource" }
            .SelectMany(Paths).Concat(rawInputs).Concat(declaredFiles).Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var reference in items.GetProperty("Reference").EnumerateArray())
            if (reference.TryGetProperty("HintPath", out var hint) && !string.IsNullOrWhiteSpace(hint.GetString()))
                inputs = inputs.Append(Resolve(Path.GetDirectoryName(project)!, hint.GetString()!)).ToArray();
        var references = Paths("ProjectReference").Concat(projectFiles.SelectMany(p => XDocument.Load(p).Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            .Select(e => Resolve(Path.GetDirectoryName(project)!, ((string?)e.Attribute("Include") ?? "").Replace('\\', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.Ordinal).ToArray();
        return new(references, imports.ToArray(), inputs,
            properties.GetProperty("MSBuildProjectExtensionsPath").GetString()!, items.GetProperty("PackageReference").GetArrayLength() > 0);
    }

    private static string RunShellDotNet(string directory, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(ResolveDotNetExecutable())
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = Process.Start(info) ?? throw new AuthoringWorkspaceException("BUILD_SHELL_INPUTS: Could not start SDK evaluation.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true);
            throw new AuthoringWorkspaceException("BUILD_SHELL_INPUTS: SDK evaluation timed out.");
        }
        var result = stdout.GetAwaiter().GetResult();
        var errors = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new AuthoringWorkspaceException($"BUILD_SHELL_INPUTS: SDK evaluation failed. {errors} {result}");
        return result;
    }

    private static void ValidateShellEvaluationFiles(string project)
    {
        var pending = new Queue<string>();
        pending.Enqueue(project);
        for (var directory = Path.GetDirectoryName(project); directory is not null; directory = Path.GetDirectoryName(directory))
            foreach (var name in ShellConfigurationNames)
                if (File.Exists(Path.Combine(directory, name))) pending.Enqueue(Path.Combine(directory, name));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var path))
        {
            path = Path.GetFullPath(path);
            if (!seen.Add(path)) continue;
            ValidateShellConfiguration(path);
            if (Path.GetFileName(path).Equals("global.json", StringComparison.Ordinal)
                || Path.GetFileName(path).Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path) is ".editorconfig" or ".globalconfig") continue;
            foreach (var import in XDocument.Load(path).Descendants().Where(e => e.Name.LocalName == "Import"))
            {
                var referenced = Resolve(Path.GetDirectoryName(path)!, ((string?)import.Attribute("Project") ?? "").Replace('\\', Path.DirectorySeparatorChar));
                if (File.Exists(referenced)) pending.Enqueue(referenced);
            }
        }
    }

    private static void ValidateShellConfiguration(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("global.json", StringComparison.Ordinal))
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            if (json.RootElement.TryGetProperty("sdk", out var sdk) && sdk.TryGetProperty("paths", out _))
                Unsupported(path, "external SDK search paths are unsupported");
            return;
        }
        if (name.Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase) || name is ".editorconfig" or ".globalconfig") return;
        var xml = XDocument.Load(path);
        foreach (var text in xml.DescendantNodes().OfType<XText>().Select(n => n.Value)
            .Concat(xml.Descendants().Attributes().Select(a => a.Value)))
            foreach (Match function in Regex.Matches(text, @"\$\(\[([^\]]+)\]::([A-Za-z0-9_]+)"))
                if ((function.Groups[1].Value, function.Groups[2].Value) is not
                    (("System.String", "Copy") or
                     ("System.Runtime.InteropServices.RuntimeInformation", "RuntimeIdentifier") or
                     ("MSBuild", "IsOSUnixLike")))
                    Unsupported(path, "custom evaluation property functions have undeclared inputs");
        if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            && (string?)xml.Root?.Attribute("Sdk") != "Microsoft.NET.Sdk")
            Unsupported(path, "only Microsoft.NET.Sdk projects are supported");
        foreach (var element in xml.Descendants())
        {
            if (element.Name.LocalName == "Sdk" || (element != xml.Root && element.Attribute("Sdk") is not null))
                Unsupported(path, "additional SDK imports are unsupported");
            if (element.Name.LocalName == "TargetFrameworks") Unsupported(path, "multi-targeted shell graphs require framework-specific evaluation");
            if (element.Name.LocalName == "ProjectReference")
            {
                var reference = (string?)element.Attribute("Include") ?? "";
                if (reference.Contains("$(", StringComparison.Ordinal) || reference.Contains("@(", StringComparison.Ordinal)
                    || reference.Contains('*') || reference.Contains(';'))
                    Unsupported(path, "project references must use literal relative paths");
            }
            if (element.Name.LocalName == "HintPath"
                && (Path.IsPathRooted(element.Value) || element.Value.Contains("$(", StringComparison.Ordinal)))
                Unsupported(path, "assembly hint paths must be literal relative paths");
            if (element.Name.LocalName == "UsingTask") Unsupported(path, "custom MSBuild tasks are unsupported");
            if (element.Name.LocalName == "Target")
            {
                // These repository targets only stamp version metadata. Publication supplies both
                // values explicitly, so their git invocations never access the live repository.
                var target = (string?)element.Attribute("Name");
                if (target is not ("ResolveHardwareTestSourceRevision" or "StampHardwareTestInformationalVersion")
                    || name != "Directory.Build.props") Unsupported(path, "custom build targets have undeclared inputs");
                foreach (var child in element.Elements())
                    if (child.Name.LocalName is not ("PropertyGroup" or "ItemGroup" or "Exec"))
                        Unsupported(path, "custom revision target task is unsupported");
                foreach (var task in element.Descendants().Where(e => e.Name.LocalName == "Exec"))
                {
                    var command = (string?)task.Attribute("Command");
                    if (command is not ("git rev-parse --short HEAD" or "git log -1 --format=%25cI" or "git log -1 --format=%25%25cI"))
                        Unsupported(path, "custom revision command is unsupported");
                }
            }
            if (element.Name.LocalName is "CustomBeforeMicrosoftCommonTargets" or "CustomAfterMicrosoftCommonTargets"
                or "MSBuildProjectExtensionsPath" or "BaseIntermediateOutputPath" or "RestorePackagesPath"
                or "NuGetPackageRoot" or "RestoreAdditionalProjectSources" or "MSBuildSDKsPath" or "MSBuildToolsPath"
                or "MSBuildExtensionsPath" or "MSBuildExtensionsPath32" or "MSBuildExtensionsPath64"
                or "RoslynTargetsPath" or "NETCoreSdkDir" or "FrameworkPathOverride"
                or "OutputPath" or "BaseOutputPath" or "IntermediateOutputPath" or "PublishDir" or "PublishUrl")
                Unsupported(path, $"{element.Name.LocalName} can redirect inputs outside staging");
            if (element.Name.LocalName == "Import")
            {
                var import = (string?)element.Attribute("Project") ?? "";
                if (Path.IsPathRooted(import) || import.Contains("$(", StringComparison.Ordinal)
                    || import.Contains('*') || import.Contains("@(", StringComparison.Ordinal))
                    Unsupported(path, "custom imports must use literal relative paths");
            }
            foreach (var attribute in element.Attributes().Where(a => a.Name.LocalName is "Include" or "Update" or "Remove" or "HintPath"))
            {
                if (Path.IsPathRooted(attribute.Value)) Unsupported(path, "absolute source item paths cannot be relocated");
                if (attribute.Value.Contains("$(", StringComparison.Ordinal) || attribute.Value.Contains("@(", StringComparison.Ordinal))
                    Unsupported(path, "dynamic source item paths require framework-specific graph evaluation");
                if ((attribute.Value.Contains('*') || attribute.Value.Contains('?'))
                    && attribute.Value.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal))
                    Unsupported(path, "source globs outside their project directory cannot be relocated safely");
            }
            if (!element.HasElements && element.Parent?.Name.LocalName == "PropertyGroup"
                && Path.IsPathRooted(element.Value.Trim())) Unsupported(path, "absolute project property paths cannot be relocated");
        }
    }
}
