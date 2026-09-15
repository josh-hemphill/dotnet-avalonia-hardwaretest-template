using HardwareTest.Core.IO;
using HardwareTest.Core.Storage;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Storage;

public sealed class ShellAppStorageTests
{
    [Fact]
    public void ResolveDirectory_creates_contained_app_folder()
    {
        using var temp = new TempDataDirectory();
        var path = ShellAppStorage.ResolveDirectory(temp.Path, "hardwaretest.notes");
        Assert.True(Directory.Exists(path));
        Assert.True(PathContainment.IsUnderRoot(temp.Path, path));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(temp.Path, ShellAppStorage.DirectoryName, "hardwaretest.notes")),
            path);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("foo/bar")]
    [InlineData("foo\\bar")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("app id")]
    public void ResolveDirectory_rejects_unsafe_app_id(string appId)
    {
        using var temp = new TempDataDirectory();
        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellAppStorage.ResolveDirectory(temp.Path, appId));
        Assert.Contains("safe directory", ex.Message, StringComparison.Ordinal);
        var shellApps = Path.Combine(temp.Path, ShellAppStorage.DirectoryName);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "escape")));
        if (Directory.Exists(shellApps))
        {
            Assert.Empty(Directory.GetFileSystemEntries(shellApps));
        }
    }

    [Fact]
    public void IsSafeAppId_accepts_dotted_product_ids()
    {
        Assert.True(ShellAppStorage.IsSafeAppId("hardwaretest.notes"));
        Assert.True(ShellAppStorage.IsSafeAppId("vendor-planning_1"));
        Assert.False(ShellAppStorage.IsSafeAppId("vendor/planning"));
    }
}
