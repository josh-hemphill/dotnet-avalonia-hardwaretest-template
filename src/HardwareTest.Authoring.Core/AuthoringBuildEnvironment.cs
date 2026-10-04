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
    internal static IReadOnlyDictionary<string, string?> CaptureEnvironment()
    {
        foreach (var name in new[] { "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "DOTNET_STARTUP_HOOKS" })
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
                throw new AuthoringWorkspaceException($"BUILD_ENVIRONMENT: '{name}' redirects runtime/build inputs. Clear it before capturing this build.");
        var sdkRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        foreach (var name in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (string.IsNullOrWhiteSpace(sdkRoot)) throw new AuthoringWorkspaceException($"BUILD_ENVIRONMENT: '{name}' requires a declared DOTNET_ROOT.");
            EnsureContained(sdkRoot, ResolvedPath(value, Directory.Exists(value)));
        }
        return new ReadOnlyDictionary<string, string?>(BuildEnvironmentNames.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal));
    }
    internal static IReadOnlyList<AuthoringBuildEnvironmentIdentity> EnvironmentIdentity(IReadOnlyDictionary<string, string?> values)
        => Array.AsReadOnly(values.Select(value => new AuthoringBuildEnvironmentIdentity(value.Key,
            value.Value is null ? null : Hash(Encoding.UTF8.GetBytes(value.Value)))).ToArray());
}
