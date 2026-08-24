namespace HardwareTest.Features.Shell;

/// Widths at which the shell collapses secondary chrome (900×600 floor minus nav).
public static class ShellLayoutBreakpoints
{
    /// Run board width at which the stage rail hides and stages move into the hero picker.
    public const double CompactBoardWidth = 720;

    /// Home cards wrap below this min width so three-up tiles do not crush at 900×600.
    public const double HomeTileMinWidth = 260;
}
