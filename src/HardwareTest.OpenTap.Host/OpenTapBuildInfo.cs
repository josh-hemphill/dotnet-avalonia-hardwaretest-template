using System.Reflection;
using HardwareTest.Core.Diagnostics;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Resolves OpenTAP engine version without leaking OpenTAP types into Core.
public static class OpenTapBuildInfo
{
    public static string? EngineVersion
    {
        get
        {
            var assembly = typeof(TestPlan).Assembly;
            return assembly.GetName().Version?.ToString()
                   ?? assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                       ?.InformationalVersion;
        }
    }

    /// Identifies the engine actually loaded in this process, including same-version binary variants.
    public static string ExecutionEngineIdentity
    {
        get
        {
            var assembly = typeof(TestPlan).Assembly;
            return $"{assembly.GetName().Version}/{assembly.ManifestModule.ModuleVersionId:D}";
        }
    }

    public static BuildInfo Attach(BuildInfo buildInfo)
        => buildInfo.WithOpenTapEngineVersion(EngineVersion);
}
