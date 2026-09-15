namespace HardwareTest.Shell;

/// Placement-based left-nav policy. Engineer mode is presentation, not authentication.
public static class ShellNavigationRules
{
    /// True when the page is a standing left-nav item for the current presentation.
    public static bool IsPersistentNav(ShellPageDescriptor page, bool engineerMode)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Placement == ShellPagePlacement.Contextual)
        {
            return false;
        }

        if (page.Placement == ShellPagePlacement.Operator)
        {
            return true;
        }

        return engineerMode && page.Placement == ShellPagePlacement.Engineer;
    }

    /// True when the operator may stay on the page even if it is not in the left nav.
    public static bool CanRemainOnPage(ShellPageDescriptor page, bool engineerMode)
    {
        ArgumentNullException.ThrowIfNull(page);
        return IsPersistentNav(page, engineerMode)
               || page.Placement == ShellPagePlacement.Contextual
               || page.CanRemainWithoutNav;
    }

    /// Nav-pane selection id: contextual pages highlight their parent.
    public static string NavSelectionId(ShellPageDescriptor page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Placement != ShellPagePlacement.Contextual)
        {
            return page.Id;
        }

        return string.IsNullOrWhiteSpace(page.ContextualParentId)
            ? page.Id
            : page.ContextualParentId;
    }
}
