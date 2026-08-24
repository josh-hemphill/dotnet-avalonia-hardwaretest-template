using HardwareTest.Features;
using HardwareTest.Features.Shell;
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
