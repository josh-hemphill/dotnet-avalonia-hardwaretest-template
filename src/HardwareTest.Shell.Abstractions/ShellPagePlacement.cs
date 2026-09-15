namespace HardwareTest.Shell;

/// How a catalog page appears in the operator vs engineer left nav.
public enum ShellPagePlacement
{
    /// Standing left-nav item for every operator.
    Operator = 0,

    /// Standing left-nav item only when engineer/debug presentation is on.
    Engineer = 1,

    /// No standing nav item; opened from a parent page (for example Report Preview).
    Contextual = 2,
}
