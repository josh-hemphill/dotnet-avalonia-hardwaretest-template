namespace HardwareTest.Features.Shell;

/// Documented Phase 18 operator touch-density floor and Phase 21 operational type scale
/// (bench / tablet; not full kiosk bake or Narrator certification).
public static class OperatorTouchDensity
{
    /// Buttons, chips, primary list rows, and expanded nav footer actions.
    public const double OperatorControlMinHeight = 40;

    /// Compact icon-only nav footer targets (matches FANavigationView CompactPaneLength).
    public const double CompactNavTargetSize = 48;

    /// List↔Details GridSplitter hit area (legacy; Details is now a full workspace).
    public const double DetailsSplitterMinHeight = 16;

    /// Chart workspace plot floor so the trend is readable at 900×600.
    public const double ChartPlotMinHeight = 300;

    /// Optional hierarchy overview rail to the right of the Run tabs.
    public const double OverviewSidebarWidth = 200;

    /// Phase 21 floor for Run chip / step / hero secondary / compact transport captions (px).
    public const double OperationalFontSize = 12;

    /// Outer operator-prompt card — keeps Continue docked in view at the 900×600 floor.
    public const double InteractionHostMaxHeight = 280;

    /// Scrollable prompt body (title, message, fields) inside the interaction host.
    public const double InteractionHostBodyMaxHeight = 180;
}
