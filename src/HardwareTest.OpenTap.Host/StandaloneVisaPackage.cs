namespace HardwareTest.OpenTap.Host;

/// <summary>HardwareTest-owned execution counterpart; the base remains the genuine TAP release.</summary>
public static class StandaloneVisaPackage
{
    public const string PackageName = "HardwareTest Standalone VISA";
    public const string Version = "0.1.0";
    public const string BaseVersion = "0.1.1";
    public const string OpenTapVersion = "9.35.0";
    public const string WrapperFileName = "HardwareTest.OpenTap.StandaloneVisa.dll";

    public static Stream OpenArchive() => typeof(StandaloneVisaPackage).Assembly.GetManifestResourceStream(
        "HardwareTest.OpenTap.Host.StandaloneVisa.TapPackage")
        ?? throw new InvalidOperationException("The standalone VISA counterpart archive is missing from the host.");
}
