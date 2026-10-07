using System.IO.Compression;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring.Tests;

internal static class PublishedLibraryFixture
{
    internal static string PackageRoot => Environment.GetEnvironmentVariable("HARDWARETEST_LIBRARY_TEST_PACKAGE_ROOT") is { Length: > 0 } path
        ? path : BundledFolder.Value;
    internal static string Archive => Environment.GetEnvironmentVariable("HARDWARETEST_LIBRARY_TEST_ARCHIVE") is { Length: > 0 } path
        ? path : BundledArchive.Value;
    private static readonly Lazy<string> BundledArchive = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "ht-published-fixture-" + Guid.NewGuid().ToString("N") + ".TapPackage");
        RegisterCleanup(path, directory: false);
        PublishedInstrumentComponents.MaterializeArchive(path);
        return path;
    });
    private static readonly Lazy<string> BundledFolder = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "ht-published-fixture-" + Guid.NewGuid().ToString("N"));
        RegisterCleanup(path, directory: true);
        using var archive = ZipFile.OpenRead(Archive);
        archive.ExtractToDirectory(path);
        // An external unpacked input keeps the genuine release declarations and hashes.
        var metadata = XDocument.Load(Path.Combine(path, "Packages", PublishedInstrumentComponents.PackageName, "package.xml"));
        metadata.Save(Path.Combine(path, "package.xml"));
        return path;
    });

    private static void RegisterCleanup(string path, bool directory)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (directory) Directory.Delete(path, recursive: true);
                else File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
