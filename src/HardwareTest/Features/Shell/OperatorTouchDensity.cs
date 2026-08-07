namespace HardwareTest.Features.Shell;

/// Documented Phase 18 operator touch-density floor (bench / tablet; not full kiosk bake).
public static class OperatorTouchDensity
{
    /// Buttons, chips, primary list rows, and expanded nav footer actions.
    public const double OperatorControlMinHeight = 40;

    /// Compact icon-only nav footer targets (matches FANavigationView CompactPaneLength).
    public const double CompactNavTargetSize = 48;

    /// List↔Details GridSplitter hit area.
    public const double DetailsSplitterMinHeight = 16;
}
