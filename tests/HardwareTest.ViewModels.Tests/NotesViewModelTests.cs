using HardwareTest.ShellApps.Notes;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class NotesViewModelTests
{
    [Fact]
    public void Body_persists_under_host_app_data_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "notes-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FakeSettingsStore(root);
            Directory.CreateDirectory(store.RootDirectory);
            var host = new ShellHost(store);
            var first = new NotesViewModel(host);
            first.Body = "bench note";
            var path = Path.Combine(host.GetAppDataDirectory(NotesApplication.PageId), NotesViewModel.FileName);
            Assert.True(File.Exists(path));
            Assert.Equal("bench note", File.ReadAllText(path));

            var second = new NotesViewModel(host);
            Assert.Equal("bench note", second.Body);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
