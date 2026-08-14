using HardwareTest.Core.IO;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.IO;

public sealed class PluginDirectoryTrustTests
{
    [Fact]
    public void Allows_path_under_data_plugins()
    {
        using var temp = new TempDataDirectory();
        var extra = Path.Combine(temp.Path, "plugins", "vendor");
        Directory.CreateDirectory(extra);
        Assert.True(PluginDirectoryTrust.Allows(temp.Path, extra, engineerDebug: false));
    }

    [Fact]
    public void Rejects_sibling_outside_plugins()
    {
        using var temp = new TempDataDirectory();
        var escape = Path.Combine(temp.Path, "escape");
        Directory.CreateDirectory(escape);
        Assert.False(PluginDirectoryTrust.Allows(temp.Path, escape, engineerDebug: false));
    }

    [Fact]
    public void Engineer_debug_allows_arbitrary_path()
    {
        using var temp = new TempDataDirectory();
        var escape = Path.Combine(temp.Path, "escape");
        Directory.CreateDirectory(escape);
        Assert.True(PluginDirectoryTrust.Allows(temp.Path, escape, engineerDebug: true));
    }

    [Fact]
    public void Empty_data_directory_rejects_without_debug()
    {
        Assert.False(PluginDirectoryTrust.Allows("", Path.GetTempPath(), engineerDebug: false));
    }
}
