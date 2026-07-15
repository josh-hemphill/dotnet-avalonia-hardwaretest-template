namespace HardwareTest.Core.Settings;

/// Pure geometry helpers for restoring a window onto a remembered monitor.
public static class MonitorPlacement
{
    public readonly record struct ScreenInfo(string DeviceName, int X, int Y, int Width, int Height, bool IsPrimary);

    public readonly record struct Placement(int X, int Y, int Width, int Height, string? DeviceName);

    /// Picks a target screen by saved DeviceName, else primary, else first.
    public static ScreenInfo? ResolveScreen(string? savedDeviceName, IReadOnlyList<ScreenInfo> screens)
    {
        if (screens.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(savedDeviceName))
        {
            foreach (var screen in screens)
            {
                if (string.Equals(screen.DeviceName, savedDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return screen;
                }
            }
        }

        foreach (var screen in screens)
        {
            if (screen.IsPrimary)
            {
                return screen;
            }
        }

        return screens[0];
    }

    /// Clamps proposed bounds into the target screen working area.
    public static Placement ClampToScreen(int x, int y, int width, int height, ScreenInfo screen)
    {
        width = Math.Max(400, width);
        height = Math.Max(300, height);
        var maxX = screen.X + Math.Max(0, screen.Width - 50);
        var maxY = screen.Y + Math.Max(0, screen.Height - 50);
        if (x < screen.X || x > maxX)
        {
            x = screen.X + 40;
        }

        if (y < screen.Y || y > maxY)
        {
            y = screen.Y + 40;
        }

        width = Math.Min(width, Math.Max(400, screen.Width - 20));
        height = Math.Min(height, Math.Max(300, screen.Height - 20));
        return new Placement(x, y, width, height, screen.DeviceName);
    }
}
