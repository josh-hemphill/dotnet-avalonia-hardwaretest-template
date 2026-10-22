using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class EnvironmentPresentationTests
{
    [AvaloniaFact]
    public async Task Readiness_separates_diagnostics_and_copies_exact_package_requirements()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
        workspace.Manifest.Dependencies.Add(new() { Package = "Acme Scope", Version = "^2.3.0" });
        AuthoringWorkspaceLoader.SaveManifest(fixture.WorkspaceRoot, workspace.Manifest);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        fixture.NavigateTask(3);
        var vm = fixture.ViewModel;
        Assert.Equal("Optional for mock plans", fixture.Control<TextBlock>("Instrument Components readiness").Text);
        Assert.DoesNotContain(fixture.WorkspaceRoot, fixture.Control<TextBlock>("Instrument Components readiness reason").Text!);
        Assert.False(fixture.Control<Expander>("Instrument Components diagnostic details").IsExpanded);
        AuthoringUiFixture.Click(fixture.Control<Button>("Copy package requirements"));
        var copied = await window.Clipboard!.TryGetTextAsync();
        Assert.Contains("Acme Scope: required ^2.3.0", copied!);
        Assert.Contains("https://github.com/josh-hemphill/instrument-components/releases", copied!);
        Assert.Contains("publisher", vm.EnvironmentPackages.Single(package => package.Package == "Acme Scope").Resolution);
        vm.DeclareLibraryDependency();
        AuthoringUiFixture.Drain();
        Assert.Equal("Needs attention", fixture.Control<TextBlock>("Instrument Components readiness").Text);
        vm.OpenTapHomeOverride = "invalid\0home";
        AuthoringUiFixture.Drain();
        Assert.Equal("Cannot inspect", vm.EnvironmentLibraryReadiness.State);
        Assert.Equal("Cannot inspect", vm.EnvironmentExecutionReadiness.State);
        Assert.Contains("Settings", vm.EnvironmentLibraryReadiness.NextStep);
    }

    [AvaloniaFact]
    public void Loadable_library_does_not_mask_a_declared_version_mismatch()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
        workspace.Manifest.Dependencies.Add(new() { Package = AuthoringInstrumentCatalog.LibraryPackage, Version = "0.1.1" });
        var home = new OpenTapHomeBootstrapper().Bootstrap(workspace, new()
        {
            HomeDirectory = Path.Combine(workspace.Root, "prepared-home"),
            Offline = true
        });
        Assert.True(AuthoringInstrumentCatalog.LibraryReadiness(home).Available);
        workspace.Manifest.Dependencies.Single(dependency => dependency.Package == AuthoringInstrumentCatalog.LibraryPackage).Version = "9.9.0";
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        fixture.ViewModel.OpenTapHomeOverride = home.Root;
        fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(3);
        Assert.Equal("Needs attention", fixture.Control<TextBlock>("Instrument Components readiness").Text);
        Assert.Contains("does not match", fixture.Control<TextBlock>("Instrument Components readiness reason").Text);
        Assert.Contains("9.9.0", fixture.ViewModel.EnvironmentLibraryReadiness.Details);
        Assert.Contains("version mismatch", fixture.ViewModel.EnvironmentLibraryReadiness.Details);
    }

    [AvaloniaFact]
    public void Build_readiness_has_direct_route_to_package_resolution()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(4);
        AuthoringUiFixture.Click(fixture.Control<Button>("Review build environment"));
        Assert.Equal(3, window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        Assert.True(fixture.Control<Button>("Copy package requirements").IsEffectivelyVisible);
    }
}
