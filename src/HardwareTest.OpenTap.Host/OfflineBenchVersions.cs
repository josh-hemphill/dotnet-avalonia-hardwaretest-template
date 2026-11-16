using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using OpenTap;
using OpenTap.Package;

namespace HardwareTest.OpenTap.Host;

/// Resolves only the trusted SDK assembly; offline checks never scan/load plugin packages.
internal static class OfflineBenchVersions
{
    private static readonly object Gate = new();

    internal static void RequireValid(string constraint)
    {
        EnsureSdk(); Parse(constraint);
    }

    internal static bool IsCompatible(string constraint, string version)
    {
        EnsureSdk(); return Compare(constraint, version);
    }

    private static void EnsureSdk()
    {
        lock (Gate)
        {
            if (AssemblyLoadContext.Default.Assemblies.Any(assembly => assembly.GetName().Name == "OpenTap.Package")) return;
            var directories = new[] { Path.GetDirectoryName(typeof(OfflineBenchVersions).Assembly.Location),
                Path.GetDirectoryName(typeof(TestPlan).Assembly.Location), AppContext.BaseDirectory };
            foreach (var directory in directories.OfType<string>().Distinct(StringComparer.Ordinal))
                foreach (var path in new[] { Path.Combine(directory, "OpenTap.Package.dll"), Path.Combine(directory, "Dependencies", "OpenTap.Package.dll") })
                    if (File.Exists(path))
                    {
                        OfflineBenchPaths.RejectLinks(path);
                        var name = AssemblyName.GetAssemblyName(path);
                        if (name.Name != "OpenTap.Package" || name.Version != typeof(TestPlan).Assembly.GetName().Version)
                            throw new InvalidDataException("The operator package SDK does not match its OpenTAP engine. Restore the matching operator release.");
                        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
                        return;
                    }
            throw new InvalidDataException("The operator package SDK is missing. Restore the complete matching operator release before checking a bundle.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Parse(string constraint) => VersionSpecifier.Parse(constraint);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Compare(string constraint, string version)
        => VersionSpecifier.Parse(constraint).IsCompatible(SemanticVersion.Parse(version));
}
