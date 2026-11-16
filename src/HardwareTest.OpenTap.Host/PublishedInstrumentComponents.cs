namespace HardwareTest.OpenTap.Host;

/// <summary>The exact published device-library release bundled with the tools.</summary>
public static class PublishedInstrumentComponents
{
    public const string PackageName = "InstrumentComponents.OpenTap";
    public const string Version = "0.3.0-alpha.1";
    public const string AssemblyVersion = "0.3.0.0";
    public const string ArchiveName = "InstrumentComponents.OpenTap.0.3.0-alpha.1.TapPackage";
    public const string Origin = "https://github.com/josh-hemphill/instrument-components/releases/download/v0.3.0-alpha.1/InstrumentComponents.OpenTap.0.3.0-alpha.1.TapPackage";
    public const string Sha256 = "a7891374e1e613eff83dcb6dafd93a3e4b4584f4f2c4b5a942b694188148b9c4";

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
