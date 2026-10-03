using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class PackProtectionWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Edited_or_new_program_disables_pack_and_direct_guard_retains_findings(bool create)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();

        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1;
        AuthoringUiFixture.Drain();
        if (create) fixture.ViewModel.CreateProgram("new-dirty");
        else fixture.Type(fixture.Control<TextBox>("Display name"), "dirty GUI edit");
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 4;
        AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<Button>("Pack workspace").IsEnabled);
        var output = Path.Combine(fixture.WorkspaceRoot, "blocked-dist");
        var home = Path.Combine(fixture.WorkspaceRoot, "blocked-home");
        fixture.ViewModel.OpenTapHomeOverride = home;
        var ex = Assert.Throws<PackPreflightException>(() => fixture.ViewModel.Pack(output));
        AuthoringUiFixture.Drain();
        Assert.Same(ex.Report, fixture.ViewModel.LastPackPreflight);
        Assert.NotEmpty(fixture.Control<ItemsControl>("Pack preflight findings").Items);
        Assert.False(Directory.Exists(output));
        Assert.False(Directory.Exists(home));
        Assert.False(File.Exists(Path.Combine(fixture.WorkspaceRoot, "package.xml")));
    }
}
