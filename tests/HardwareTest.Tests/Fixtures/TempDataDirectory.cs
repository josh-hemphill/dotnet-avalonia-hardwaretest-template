namespace HardwareTest.Tests.Fixtures;

/// Creates and cleans a unique temp data directory for a test.
public sealed class TempDataDirectory : IDisposable
{
    public TempDataDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "HardwareTestTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string RunsDirectory => System.IO.Path.Combine(Path, "runs");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }
}
