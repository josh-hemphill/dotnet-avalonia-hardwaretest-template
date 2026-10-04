using System.Collections;
using System.Collections.ObjectModel;
using System.Text;

namespace HardwareTest.Authoring;

public sealed record AuthoringBuildEnvironmentIdentity(string Name, string? ValueSha256);

public static partial class AuthoringBuildService
{
    private static readonly string[] BuildEnvironmentNames = ["DOTNET_ROOT", "PATH", "MSBuildSDKsPath", "MSBUILD_EXE_PATH",
        "MSBuildExtensionsPath", "NUGET_PACKAGES", "NUGET_FALLBACK_PACKAGES", "DOTNET_ROLL_FORWARD", "DOTNET_HOST_PATH",
        "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "DOTNET_STARTUP_HOOKS", "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR",
        "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER"];
    private static readonly HashSet<string> ShellRedirectProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "CustomBeforeMicrosoftCommonTargets", "CustomAfterMicrosoftCommonTargets", "MSBuildProjectExtensionsPath",
        "BaseIntermediateOutputPath", "IntermediateOutputPath", "OutputPath", "BaseOutputPath", "OutDir",
        "PublishDir", "PublishUrl", "TargetPath", "DocumentationFile", "PdbFile", "PdbFileName",
        "DirectoryBuildPropsPath", "DirectoryBuildTargetsPath", "DirectoryPackagesPropsPath",
        "CustomBeforeMicrosoftCommonProps", "CustomAfterMicrosoftCommonProps", "MSBuildOverrideTasksPath",
        "MSBuildUserExtensionsPath", "MSBuildStartupDirectory", "RestorePackagesPath", "NuGetPackageRoot",
        "RestoreConfigFile", "RestoreRootConfigDirectory", "RestoreOutputPath", "RestoreSources",
        "RestoreAdditionalProjectSources", "RestoreFallbackFolders", "RestoreAdditionalProjectFallbackFolders",
        "NuGetFallbackFolder", "NUGET_FALLBACK_PACKAGES", "NUGET_HTTP_CACHE_PATH", "NUGET_SCRATCH",
        "NUGET_PLUGINS_CACHE_PATH", "MSBUILDTEMPPATH", "RoslynAssembliesPath", "CscToolPath", "CscToolExe",
        "VbcToolPath", "VbcToolExe", "FscToolPath", "FscToolExe", "CompilerResponseFile", "AssemblySearchPaths",
        "ReferencePath", "AdditionalLibPaths", "Win32Resource", "Win32Manifest", "ApplicationIcon",
        "AssemblyOriginatorKeyFile", "KeyOriginatorFile", "CodeAnalysisRuleSet"
    };
    private static readonly HashSet<string> ContainedSdkEnvironmentProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildToolsPath", "MSBuildExtensionsPath",
        "MSBuildExtensionsPath32", "MSBuildExtensionsPath64", "RoslynTargetsPath", "NETCoreSdkDir",
        "FrameworkPathOverride", "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR"
    };
    internal static IReadOnlyDictionary<string, string?> CaptureEnvironment()
    {
        foreach (var name in new[] { "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "DOTNET_STARTUP_HOOKS" })
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
                throw new AuthoringWorkspaceException($"BUILD_ENVIRONMENT: '{name}' redirects runtime/build inputs. Clear it before capturing this build.");
        var sdkRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            var value = (string?)entry.Value;
            if (string.IsNullOrWhiteSpace(value)) continue;
            // MSBuild consumes inherited properties before project evaluation. In particular,
            // publish -o does not contain OutputPath or intermediate/cache/import redirects.
            if (ShellRedirectProperties.Contains(name))
                throw new AuthoringWorkspaceException($"BUILD_ENVIRONMENT: '{name}' redirects SDK inputs or outputs outside the owned build profile. Clear it before capturing this build.");
            if (!ContainedSdkEnvironmentProperties.Contains(name)) continue;
            if (string.IsNullOrWhiteSpace(sdkRoot)) throw new AuthoringWorkspaceException($"BUILD_ENVIRONMENT: '{name}' requires a declared DOTNET_ROOT.");
            if (!Path.IsPathFullyQualified(value) || value.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Any(p => p is "." or ".."))
                throw new AuthoringWorkspaceException($"BUILD_ENVIRONMENT: '{name}' must identify an unambiguous absolute path inside DOTNET_ROOT.");
            EnsureContained(sdkRoot, ResolvedPath(value, Directory.Exists(value)));
        }
        var values = BuildEnvironmentNames.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        // MSBuild imports arbitrary environment variables as initial properties. Freeze all
        // inherited values rather than guessing which custom project expressions use them.
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) values[(string)entry.Key] = (string?)entry.Value;
        return new ReadOnlyDictionary<string, string?>(values.OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
    }
    internal static IReadOnlyList<AuthoringBuildEnvironmentIdentity> EnvironmentIdentity(IReadOnlyDictionary<string, string?> values)
        => Array.AsReadOnly(values.Select(value => new AuthoringBuildEnvironmentIdentity(value.Key,
            value.Value is null ? null : Hash(Encoding.UTF8.GetBytes(value.Value)))).ToArray());
}
