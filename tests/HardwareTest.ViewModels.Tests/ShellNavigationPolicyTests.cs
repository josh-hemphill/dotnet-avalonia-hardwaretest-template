using HardwareTest.Features;
using HardwareTest.Features.Shell;
using HardwareTest.Shell;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Operator vs engineer left-nav presentation (not authentication).
public sealed class ShellNavigationPolicyTests
{
    [Fact]
    public void Operator_persistent_nav_hides_inspect_instruments_and_report_preview()
    {
        Assert.True(ShellNavigationPolicy.IsPersistentNav(ShellNavigationPolicy.Home, engineerMode: false));
        Assert.True(ShellNavigationPolicy.IsPersistentNav(ShellNavigationPolicy.RunTest, engineerMode: false));
        Assert.True(ShellNavigationPolicy.IsPersistentNav(ShellNavigationPolicy.Results, engineerMode: false));
        Assert.True(ShellNavigationPolicy.IsPersistentNav(ShellNavigationPolicy.Settings, engineerMode: false));
        Assert.False(ShellNavigationPolicy.IsPersistentNav(ShellNavigationPolicy.Inspect, engineerMode: false));
        Assert.False(ShellNavigationPolicy.IsPersistentNav(ShellNavigationPolicy.Instruments, engineerMode: false));
        Assert.False(ShellNavigationPolicy.IsPersistentNav(ShellNavigationPolicy.ReportPreview, engineerMode: false));
        Assert.True(ShellNavigationPolicy.IsContextual(ShellNavigationPolicy.ReportPreview));
        Assert.Equal(ShellNavigationPolicy.Results, ShellNavigationPolicy.ContextualParentId(ShellNavigationPolicy.ReportPreview));
        Assert.True(ShellNavigationPolicy.CanRemainOnPage(ShellNavigationPolicy.Instruments, engineerMode: false));
        Assert.False(ShellNavigationPolicy.CanRemainOnPage(ShellNavigationPolicy.Inspect, engineerMode: false));
    }

    [Fact]
    public void Engineer_persistent_nav_adds_inspect_and_instruments_not_report_preview()
    {
        Assert.True(ShellNavigationPolicy.IsPersistentNav(ShellNavigationPolicy.Inspect, engineerMode: true));
        Assert.True(ShellNavigationPolicy.IsPersistentNav(ShellNavigationPolicy.Instruments, engineerMode: true));
        Assert.False(ShellNavigationPolicy.IsPersistentNav(ShellNavigationPolicy.ReportPreview, engineerMode: true));
    }

    [Fact]
    public void Navigation_rules_follow_descriptor_placement_not_builtin_ids()
    {
        var guest = new ShellPageDescriptor
        {
            Id = "vendor.planning",
            Title = "Plan",
            SymbolName = "Calendar",
            ViewModelType = typeof(object),
            Placement = ShellPagePlacement.Operator,
            Order = 60,
        };
        Assert.True(ShellNavigationRules.IsPersistentNav(guest, engineerMode: false));
        Assert.True(ShellNavigationRules.CanRemainOnPage(guest, engineerMode: false));
        Assert.Equal("vendor.planning", ShellNavigationRules.NavSelectionId(guest));
        Assert.False(ShellBuiltInPageIds.IsReserved(guest.Id));
        Assert.False(ShellNavigationPolicy.IsPersistentNav(guest.Id, engineerMode: false));
    }

    [Fact]
    public void Guest_engineer_page_is_hidden_from_operator_nav()
    {
        var guest = new ShellPageDescriptor
        {
            Id = "vendor.diagnostics",
            Title = "Diagnostics",
            SymbolName = "Repair",
            ViewModelType = typeof(object),
            Placement = ShellPagePlacement.Engineer,
            Order = 70,
        };
        Assert.False(ShellNavigationRules.IsPersistentNav(guest, engineerMode: false));
        Assert.True(ShellNavigationRules.IsPersistentNav(guest, engineerMode: true));
        Assert.False(ShellNavigationRules.CanRemainOnPage(guest, engineerMode: false));
    }

    [Fact]
    public void Builtin_descriptors_match_reserved_ids_and_operator_set()
    {
        Assert.Equal(ShellBuiltInPageIds.All.Count, BuiltinShellPages.Descriptors.Count);
        Assert.All(BuiltinShellPages.Descriptors, d => Assert.True(ShellBuiltInPageIds.IsReserved(d.Id)));
        var operatorIds = BuiltinShellPages.Descriptors
            .Where(d => ShellNavigationRules.IsPersistentNav(d, engineerMode: false))
            .Select(d => d.Id)
            .ToArray();
        Assert.Equal(ShellNavigationPolicy.OperatorPersistentIds, operatorIds);
        var engineerExtra = BuiltinShellPages.Descriptors
            .Where(d => d.Placement == ShellPagePlacement.Engineer)
            .Select(d => d.Id)
            .ToArray();
        Assert.Equal(ShellNavigationPolicy.EngineerExtraPersistentIds, engineerExtra);
        Assert.Equal(90, BuiltinShellPages.Find(ShellBuiltInPageIds.Settings)?.Order);
    }

    [Fact]
    public void SyncCollection_inserts_moves_and_removes_without_replacing_instance()
    {
        var home = new NavItem { Id = "Home", Title = "Home", ViewModel = new object(), Symbol = default };
        var run = new NavItem { Id = "RunTest", Title = "Run", ViewModel = new object(), Symbol = default };
        var inspect = new NavItem { Id = "Inspect", Title = "Inspect", ViewModel = new object(), Symbol = default };
        var target = new System.Collections.ObjectModel.ObservableCollection<NavItem> { inspect, home };
        ShellNavigationPolicy.SyncCollection(target, [home, run]);
        Assert.Equal([home, run], target);
    }
}
