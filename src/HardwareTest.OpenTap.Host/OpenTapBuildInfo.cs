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

    public static BuildInfo Attach(BuildInfo buildInfo)
        => buildInfo.WithOpenTapEngineVersion(EngineVersion);
}
