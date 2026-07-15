using HardwareTest.Core.Settings;
using Xunit;

namespace HardwareTest.Tests.Settings;

public sealed class MonitorPlacementTests
{
    [Fact]
    public void ResolveScreen_prefers_saved_device_name()
    {
        var screens = new[]
        {
            new MonitorPlacement.ScreenInfo("Primary", 0, 0, 1920, 1080, true),
            new MonitorPlacement.ScreenInfo("Secondary", 1920, 0, 1440, 900, false),
        };

        var resolved = MonitorPlacement.ResolveScreen("Secondary", screens);
        Assert.NotNull(resolved);
        Assert.Equal("Secondary", resolved!.Value.DeviceName);
    }

    [Fact]
    public void ResolveScreen_falls_back_to_primary()
    {
        var screens = new[]
        {
            new MonitorPlacement.ScreenInfo("A", 0, 0, 800, 600, false),
            new MonitorPlacement.ScreenInfo("B", 800, 0, 800, 600, true),
        };

        var resolved = MonitorPlacement.ResolveScreen("missing", screens);
        Assert.Equal("B", resolved!.Value.DeviceName);
    }

    [Fact]
    public void ClampToScreen_moves_offscreen_window_into_target()
    {
        var screen = new MonitorPlacement.ScreenInfo("M", 100, 200, 1000, 800, true);
        var placement = MonitorPlacement.ClampToScreen(-500, -500, 1280, 800, screen);
        Assert.True(placement.X >= screen.X);
        Assert.True(placement.Y >= screen.Y);
        Assert.Equal("M", placement.DeviceName);
    }
}
