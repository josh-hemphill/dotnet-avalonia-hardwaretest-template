namespace HardwareTest.Authoring;

public sealed record OpenTapHome(string Root);

public sealed class BootstrapOptions
{
    public string? HomeDirectory { get; init; }

    public string? InstrumentComponentsPackagePath { get; init; }

    public string? TuiPackagePath { get; init; }

    public bool Offline { get; init; }
}

public static class AuthoringBootstrapCodes
{
    public const string InstrumentComponentsPackageMissing = "INSTRUMENT_COMPONENTS_PACKAGE_MISSING";
}

public sealed record AuthoringInstalledPackage(string Name, string Version, string Path);

public interface IOpenTapHomeBootstrapper
{
    OpenTapHome Bootstrap(AuthoringWorkspace workspace, BootstrapOptions options);
}
