using HardwareTest.Core.Hardware;
using HardwareTest.Core.IO;
using HardwareTest.Core.Storage;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ShellHostTests
{
    [Fact]
    public void GetAppDataDirectory_uses_settings_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "shell-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FakeSettingsStore(root);
            Directory.CreateDirectory(store.RootDirectory);
            var host = new ShellHost(store);
            var path = host.GetAppDataDirectory("hardwaretest.notes");
            Assert.True(PathContainment.IsUnderRoot(store.RootDirectory, path));
            Assert.True(Directory.Exists(path));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(store.RootDirectory, ShellAppStorage.DirectoryName, "hardwaretest.notes")),
                path);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void GetAppDataDirectory_rejects_escape()
    {
        var root = Path.Combine(Path.GetTempPath(), "shell-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FakeSettingsStore(root);
            Directory.CreateDirectory(store.RootDirectory);
            var host = new ShellHost(store);
            Assert.Throws<InvalidOperationException>(() => host.GetAppDataDirectory("../escape"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DeviceTransfer_lease_can_write_under_app_data_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "shell-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FakeSettingsStore(root);
            Directory.CreateDirectory(store.RootDirectory);
            var host = new ShellHost(store);
            var bench = new BenchOperationCoordinator();
            Assert.True(bench.TryEnter(BenchOperation.DeviceTransfer, out var lease, out _));
            using (lease)
            {
                var dir = host.GetAppDataDirectory("hardwaretest.notes");
                var file = Path.Combine(dir, "payload.txt");
                File.WriteAllText(file, "ok");
                Assert.True(PathContainment.IsUnderRoot(store.RootDirectory, file));
                Assert.Equal("ok", File.ReadAllText(file));
            }
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
