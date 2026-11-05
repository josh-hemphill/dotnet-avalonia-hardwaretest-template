using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class EnvironmentPresentationTests
{
    [AvaloniaFact]
    public async Task Environment_keeps_versions_visible_and_reveals_sources_for_a_failed_preparation()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.FontSize = 20;
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        fixture.NavigateTask(3);
        var sources = fixture.Control<Expander>("Manual package download help");
        Assert.False(sources.IsExpanded);
        Assert.False(fixture.Control<Expander>("Environment dependency setup").IsExpanded);
        Assert.NotNull(fixture.Control<ItemsControl>("Environment package requirements"));
        OpenSources(fixture);
        Assert.True(fixture.Control<TextBox>("Environment package repository URL").IsFocused);
        sources.IsExpanded = false;
        fixture.ViewModel.ReportError("Package preparation failed; import a matching archive.");
        AuthoringUiFixture.Drain();
        Assert.True(sources.IsExpanded);
        Assert.True(fixture.Control<Button>("Import offline authoring package").IsEffectivelyVisible);
        Assert.False(fixture.ViewModel.DownloadRequiredPackages);
    }

    [AvaloniaFact]
    public void Environment_home_action_opens_the_exact_setting_without_mutating_the_plan()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var owner = fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(3);
        var before = AuthoringDocumentSnapshot.Capture(fixture.ViewModel.SelectedProgram!);
        AuthoringUiFixture.Click(fixture.Control<Button>("Change authoring home"));
        var settings = Assert.IsType<SettingsWindow>(Assert.Single(owner.OwnedWindows));
        var home = fixture.Control<TextBox>("OpenTAP home override", settings);
        Assert.True(home.IsFocused);
        ResponsiveShellTests.Inside(home, settings);
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(fixture.ViewModel.SelectedProgram!)));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_actions_use_the_configured_source_and_manual_import_recovers_after_failure(bool automatic)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
        workspace.Manifest.Dependencies.Add(new() { Package = "Download fixture", Version = "^1.0.0" });
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        var home = Path.Combine(workspace.Root, "selected-home");
        Directory.CreateDirectory(home); File.WriteAllText(Path.Combine(home, "marker.txt"), "preserved");
        var archive = Path.Combine(workspace.Root, "manual.TapPackage");
        AuthoringEnvironmentBuildWindowTests.WriteArchive(archive, "Download fixture", "1.2.0");
        var picker = new TestWorkspacePicker { Path = archive };
        fixture.Show(offlinePackagePicker: picker); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(3);
        fixture.ViewModel.OpenTapHomeOverride = home;
        fixture.ViewModel.DownloadRequiredPackages = automatic;
        // Invalid HTTPS settings prove that the chosen repository reaches the child without using the network.
        fixture.ViewModel.PackageRepositoryUrl = "http://invalid.example.test";
        fixture.ViewModel.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(
            Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")), action => Dispatcher.UIThread.Post(action));
        if (!automatic) fixture.Control<Expander>("Manual package download help").IsExpanded = true;
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>(automatic ? "Prepare selected authoring environment" : "Download missing required packages"));
        Assert.True(fixture.ViewModel.OperationBusy);
        Assert.False(fixture.Control<TextBox>("Environment package repository URL").IsEffectivelyEnabled);
        Assert.True(fixture.Control<Button>("Cancel environment preparation").IsEffectivelyVisible);
        await UntilCompleted();
        Assert.Contains("HTTPS", fixture.Control<TextBlock>("Environment preparation error").Text);
        Assert.Equal(["marker.txt"], Directory.GetFiles(home).Select(Path.GetFileName));
        Assert.Equal(automatic, fixture.ViewModel.DownloadRequiredPackages);
        Assert.True(fixture.Control<Button>("Copy package requirements").IsEffectivelyEnabled);
        AuthoringUiFixture.Click(fixture.Control<Button>("Import offline authoring package"));
        await UntilCompleted();
        Assert.Null(fixture.ViewModel.Error);
        Assert.Contains(fixture.ViewModel.EnvironmentPackages, p => p.Package == "Download fixture" && p.Satisfied);
        Assert.Equal("preserved", File.ReadAllText(Path.Combine(home, "marker.txt")));

        async Task UntilCompleted()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            while (fixture.ViewModel.OperationBusy) { AuthoringUiFixture.Drain(); await Task.Delay(10, timeout.Token); }
            await fixture.WaitForReadinessAsync();
        }
    }

    [AvaloniaFact]
    public async Task Download_controls_persist_settings_and_keep_manual_helpers_available_for_invalid_sources()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(3);
        var automatic = fixture.Control<CheckBox>("Automatically download required packages");
        Assert.False(automatic.IsChecked);
        automatic.IsChecked = true;
        OpenSources(fixture);
        var repository = fixture.Control<TextBox>("Environment package repository URL");
        repository.Text = "https://packages.example.test/feed";
        AuthoringUiFixture.Drain();
        fixture.Preferences.Load();
        Assert.True(fixture.Preferences.Current.DownloadRequiredPackages);
        Assert.Equal(repository.Text, fixture.Preferences.Current.PackageRepositoryUrl);
        Assert.True(fixture.Control<Button>("Download missing required packages").IsEffectivelyEnabled);
        repository.Text = "http://invalid.example.test";
        AuthoringUiFixture.Drain();
        Assert.Contains("HTTPS", fixture.Control<TextBlock>("Environment repository validation").Text);
        Assert.True(fixture.Control<Button>("Import offline authoring package").IsEffectivelyEnabled);
        OpenSources(fixture);
        AuthoringUiFixture.Click(fixture.Control<Button>("Copy package requirements"));
        Assert.Contains("correct the invalid URL", await window.Clipboard!.TryGetTextAsync());
        Assert.True(fixture.Control<Button>("Open Instrument Components releases").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Future_preferences_disable_download_settings_without_disabling_manual_import()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        File.WriteAllText(fixture.Preferences.FilePath, """{"schemaVersion":999,"futureOnly":true}""");
        fixture.Preferences.Load();
        fixture.Show(); fixture.ViewModel.Open(fixture.WorkspaceRoot); fixture.NavigateTask(3);
        Assert.False(fixture.Control<CheckBox>("Automatically download required packages").IsEffectivelyEnabled);
        OpenSources(fixture);
        Assert.False(fixture.Control<TextBox>("Environment package repository URL").IsEffectivelyEnabled);
        Assert.True(fixture.Control<Button>("Import offline authoring package").IsEffectivelyEnabled);
        fixture.ViewModel.DownloadRequiredPackages = true;
        Assert.False(fixture.ViewModel.DownloadRequiredPackages);
        Assert.Contains("futureOnly", File.ReadAllText(fixture.Preferences.FilePath));
    }

    [AvaloniaFact]
    public async Task Readiness_separates_diagnostics_and_copies_exact_package_requirements()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
        workspace.Manifest.Dependencies.Add(new() { Package = "Acme Scope", Version = "^2.3.0" });
        AuthoringWorkspaceLoader.SaveManifest(fixture.WorkspaceRoot, workspace.Manifest);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        await fixture.ViewModel.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        fixture.NavigateTask(3);
        var vm = fixture.ViewModel;
        Assert.Equal("Optional for mock plans", fixture.Control<TextBlock>("Instrument Components readiness").Text);
        var diagnostics = fixture.Control<Expander>("Instrument Components diagnostic details");
        diagnostics.IsExpanded = true; AuthoringUiFixture.Drain();
        Assert.DoesNotContain(fixture.WorkspaceRoot, fixture.Control<TextBlock>("Instrument Components readiness reason").Text!);
        diagnostics.IsExpanded = false; AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<Expander>("Instrument Components diagnostic details").IsExpanded);
        OpenSources(fixture);
        AuthoringUiFixture.Click(fixture.Control<Button>("Copy package requirements"));
        var copied = await window.Clipboard!.TryGetTextAsync();
        Assert.Contains("Acme Scope: required ^2.3.0", copied!);
        Assert.Contains("https://github.com/josh-hemphill/instrument-components/releases", copied!);
        Assert.Contains("publisher", vm.EnvironmentPackages.Single(package => package.Package == "Acme Scope").Resolution);
        vm.DeclareLibraryDependency();
        await vm.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        Assert.Equal("Needs attention", fixture.Control<TextBlock>("Instrument Components readiness").Text);
        vm.OpenTapHomeOverride = "invalid\0home";
        await vm.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        Assert.Equal("Cannot inspect", vm.EnvironmentLibraryReadiness.State);
        Assert.Equal("Cannot inspect", vm.EnvironmentExecutionReadiness.State);
        Assert.Contains("Settings", vm.EnvironmentLibraryReadiness.NextStep);
    }

    [AvaloniaFact]
    public async Task Loadable_library_does_not_mask_a_declared_version_mismatch()
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
        fixture.Show(); fixture.OpenRememberedWorkspace();
        await fixture.ViewModel.WaitForEnvironmentInspectionAsync();
        AuthoringUiFixture.Drain();
        fixture.NavigateTask(3);
        Assert.Equal("Needs attention", fixture.Control<TextBlock>("Instrument Components readiness").Text);
        fixture.Control<Expander>("Instrument Components diagnostic details").IsExpanded = true; AuthoringUiFixture.Drain();
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
        Assert.True(fixture.Control<Button>("Show package sources and manual import").IsEffectivelyVisible);
        OpenSources(fixture);
        Assert.True(fixture.Control<Button>("Copy package requirements").IsEffectivelyVisible);
    }

    private static void OpenSources(AuthoringUiFixture fixture)
    {
        AuthoringUiFixture.Click(fixture.Control<Button>("Show package sources and manual import"));
        Assert.True(fixture.Control<Expander>("Manual package download help").IsExpanded);
    }

}
