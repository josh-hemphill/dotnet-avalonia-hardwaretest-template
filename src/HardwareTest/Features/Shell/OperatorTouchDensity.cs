namespace HardwareTest.Features.Shell;

/// Documented Phase 18 operator touch-density floor and Phase 21 operational type scale
/// (bench / tablet; not full kiosk bake or Narrator certification).
public static class OperatorTouchDensity
{
    /// Buttons, chips, primary list rows, and expanded nav footer actions.
    public const double OperatorControlMinHeight = 40;

    /// Compact icon-only nav footer targets (matches FANavigationView CompactPaneLength).
    public const double CompactNavTargetSize = 48;

    /// List↔Details GridSplitter hit area.
    public const double DetailsSplitterMinHeight = 16;

    /// Phase 21 floor for Run chip / step / hero secondary / compact transport captions (px).
    public const double OperationalFontSize = 12;
}
