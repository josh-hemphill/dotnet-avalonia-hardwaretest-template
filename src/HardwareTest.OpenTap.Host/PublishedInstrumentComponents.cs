namespace HardwareTest.OpenTap.Host;

/// <summary>The exact published device-library release bundled with the tools.</summary>
public static class PublishedInstrumentComponents
{
    public const string PackageName = "InstrumentComponents.OpenTap";
    public const string Version = "0.1.1";
    public const string ArchiveName = "InstrumentComponents.OpenTap.0.1.1.TapPackage";
    public const string Origin = "https://github.com/josh-hemphill/instrument-components/releases/download/v0.1.1/InstrumentComponents.OpenTap.0.1.1.TapPackage";
    public const string Sha256 = "779fc31299fa4624f34b0031ce67a3278ff5f895a54ef3cbcc6f64c3a79ff2ee";

    /// <summary>Opens an independent, caller-owned stream of the verified release archive.</summary>
    public static Stream OpenArchive() => typeof(PublishedInstrumentComponents).Assembly.GetManifestResourceStream(
        "HardwareTest.OpenTap.Host.InstrumentComponents.OpenTap.TapPackage")
        ?? throw new InvalidOperationException("The published InstrumentComponents archive is missing from the host.");

    /// <summary>Writes the archive to a new caller-owned file; never overwrites an existing file.</summary>
    public static void MaterializeArchive(string path)
    {
        using var source = OpenArchive();
        using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }
}
