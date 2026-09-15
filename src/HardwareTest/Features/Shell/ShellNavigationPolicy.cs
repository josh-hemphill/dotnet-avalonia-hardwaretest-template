using System.Collections.ObjectModel;
using HardwareTest.Features;
using HardwareTest.Shell;

namespace HardwareTest.Features.Shell;

/// Operator vs engineer left-nav policy. Engineer mode is presentation, not authentication.
public static class ShellNavigationPolicy
{
    public const string Home = ShellBuiltInPageIds.Home;
    public const string RunTest = ShellBuiltInPageIds.RunTest;
    public const string Inspect = ShellBuiltInPageIds.Inspect;
    public const string Results = ShellBuiltInPageIds.Results;
    public const string ReportPreview = ShellBuiltInPageIds.ReportPreview;
    public const string Instruments = ShellBuiltInPageIds.Instruments;
    public const string Settings = ShellBuiltInPageIds.Settings;

    public static readonly string[] OperatorPersistentIds =
        [Home, RunTest, Results, Settings];

    public static readonly string[] EngineerExtraPersistentIds =
        [Inspect, Instruments];

    /// Report Preview is opened from Results, not a standing nav item.
    public static bool IsContextual(string pageId)
        => BuiltinShellPages.Find(pageId)?.Placement == ShellPagePlacement.Contextual;

    public static string ContextualParentId(string pageId)
    {
        var descriptor = BuiltinShellPages.Find(pageId);
        return descriptor is null
            ? pageId
            : ShellNavigationRules.NavSelectionId(descriptor);
    }

    /// Operator commissioning is reachable without a left-nav item (Run deep-link / shell action).
    public static bool CanRemainOnPage(string pageId, bool engineerMode)
    {
        var descriptor = BuiltinShellPages.Find(pageId);
        return descriptor is not null
               && ShellNavigationRules.CanRemainOnPage(descriptor, engineerMode);
    }

    public static bool IsPersistentNav(string pageId, bool engineerMode)
    {
        var descriptor = BuiltinShellPages.Find(pageId);
        return descriptor is not null
               && ShellNavigationRules.IsPersistentNav(descriptor, engineerMode);
    }

    /// Rebuilds <paramref name="target"/> to match <paramref name="desired"/> order without replacing the collection.
    public static void SyncCollection(ObservableCollection<NavItem> target, IReadOnlyList<NavItem> desired)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var item = desired[index];
            var existing = target.IndexOf(item);
            if (existing < 0)
            {
                target.Insert(index, item);
                continue;
            }

            if (existing != index)
            {
                target.Move(existing, index);
            }
        }
    }
}
