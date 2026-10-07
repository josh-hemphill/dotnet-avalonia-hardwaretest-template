namespace HardwareTest.StandaloneVisa.Tests;

public sealed partial class StandaloneBoundaryTests
{
    private sealed class RejectionWorkingDirectory : IDisposable
    {
        internal string DirectoryPath { get; } = Directory.CreateTempSubdirectory("ht-standalone-rejection-").FullName;

        internal RejectionWorkingDirectory()
        {
            try
            {
                // SessionLogs also writes its recent-log index beside OpenTap.dll.
                // Use a known clean graph, never traverse the intentionally unsafe home.
                CopyTree(Path.Combine(AppContext.BaseDirectory, "BoundaryFixture"), DirectoryPath);
                RemoveBaseFromFixture(DirectoryPath);
            }
            catch
            {
                Directory.Delete(DirectoryPath, recursive: true);
                throw;
            }
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
