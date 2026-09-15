using HardwareTest.Core.IO;
using HardwareTest.Core.Storage;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Storage;

public sealed class ShellAppPackageTrustTests
{
    [Fact]
    public void TrustedRoot_is_distinct_from_plugins_and_app_data()
    {
        Assert.NotEqual(PluginDirectoryTrust.FolderName, ShellAppPackageTrust.FolderName);
        Assert.NotEqual(ShellAppStorage.DirectoryName, ShellAppPackageTrust.FolderName);
        Assert.Equal("shell-app-packages", ShellAppPackageTrust.FolderName);
        Assert.Equal("shell-apps", ShellAppStorage.DirectoryName);
        Assert.Equal("plugins", PluginDirectoryTrust.FolderName);
    }

    [Fact]
    public void Allows_only_paths_under_data_packages_root()
    {
        using var temp = new TempDataDirectory();
        var inside = Path.Combine(temp.Path, ShellAppPackageTrust.FolderName, "vendor.planning");
        Directory.CreateDirectory(inside);
        var plugins = Path.Combine(temp.Path, PluginDirectoryTrust.FolderName, "vendor");
        Directory.CreateDirectory(plugins);
        var data = Path.Combine(temp.Path, ShellAppStorage.DirectoryName, "vendor.planning");
        Directory.CreateDirectory(data);

        Assert.True(ShellAppPackageTrust.Allows(temp.Path, inside));
        Assert.False(ShellAppPackageTrust.Allows(temp.Path, plugins));
        Assert.False(ShellAppPackageTrust.Allows(temp.Path, data));
        Assert.False(ShellAppPackageTrust.Allows(temp.Path, Path.Combine(temp.Path, "..")));
    }
}
