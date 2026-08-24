using System.Collections.ObjectModel;
using HardwareTest.Features;

namespace HardwareTest.Features.Shell;

/// Operator vs engineer left-nav policy. Engineer mode is presentation, not authentication.
public static class ShellNavigationPolicy
{
    public const string Home = "Home";
    public const string RunTest = "RunTest";
    public const string Inspect = "Inspect";
    public const string Results = "Results";
    public const string ReportPreview = "ReportPreview";
    public const string Instruments = "Instruments";
    public const string Settings = "Settings";

    public static readonly string[] OperatorPersistentIds =
        [Home, RunTest, Results, Settings];

    public static readonly string[] EngineerExtraPersistentIds =
        [Inspect, Instruments];

    /// Report Preview is opened from Results, not a standing nav item.
    public static bool IsContextual(string pageId)
        => string.Equals(pageId, ReportPreview, StringComparison.Ordinal);

    public static string ContextualParentId(string pageId)
        => IsContextual(pageId) ? Results : pageId;

    public static bool IsPersistentNav(string pageId, bool engineerMode)
    {
        if (IsContextual(pageId))
        {
            return false;
        }

        if (OperatorPersistentIds.Contains(pageId, StringComparer.Ordinal))
        {
            return true;
        }

        return engineerMode && EngineerExtraPersistentIds.Contains(pageId, StringComparer.Ordinal);
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
