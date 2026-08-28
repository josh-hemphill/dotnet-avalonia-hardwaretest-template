namespace HardwareTest.Features.Shell;

/// Widths at which the shell collapses secondary chrome (900×600 floor minus nav).
public static class ShellLayoutBreakpoints
{
    /// Run board width at which stage chips collapse to a picker.
    public const double CompactBoardWidth = 720;

    /// Run board height at which secondary chrome (search, extra actions) condenses.
    public const double CompactBoardHeight = 520;

    /// Home cards wrap below this min width so three-up tiles do not crush at 900×600.
    public const double HomeTileMinWidth = 260;
}
