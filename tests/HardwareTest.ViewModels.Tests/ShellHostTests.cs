using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ShellHostTests
{
    [Fact]
    public void GetAppDataDirectory_uses_settings_root()
    {
        var store = new FakeSettingsStore();
        Directory.CreateDirectory(store.RootDirectory);
        var host = new ShellHost(store);
        var path = host.GetAppDataDirectory("hardwaretest.notes");
        Assert.StartsWith(Path.GetFullPath(store.RootDirectory), path, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(path));
        Assert.EndsWith(
            Path.Combine("shell-apps", "hardwaretest.notes"),
            path,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAppDataDirectory_rejects_escape()
    {
        var store = new FakeSettingsStore();
        Directory.CreateDirectory(store.RootDirectory);
        var host = new ShellHost(store);
        Assert.Throws<InvalidOperationException>(() => host.GetAppDataDirectory("../escape"));
    }
}
