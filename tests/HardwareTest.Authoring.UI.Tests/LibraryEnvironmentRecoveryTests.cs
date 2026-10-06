using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HardwareTest.Authoring.Tests;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class LibraryEnvironmentRecoveryTests
{
    [AvaloniaFact]
    public void Missing_library_environment_route_retains_raw_inputs_and_resumes_without_installing()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.OpenTapHomeOverride = Path.Combine(fixture.WorkspaceRoot, "missing-library-home");
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        fixture.Control<TextBox>("Plan display name", dialog).Text = "Retained hardware task";
        fixture.Control<TextBox>("Stable plan ID", dialog).Text = "retained-hardware";
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        fixture.Control<TextBox>("Instrument slot", dialog).Text = "Power rail";
        fixture.Control<TextBox>("Instrument address", dialog).Text = "TCPIP::192.0.2.18::INSTR";
        fixture.Control<TextBox>("Instrument I/O timeout ms", dialog).Text = "8123";
        Assert.Contains("Environment", fixture.Control<TextBlock>("Hardware readiness", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Open Environment — retain inputs", dialog));
        Assert.Empty(fixture.Window.OwnedWindows);
        Assert.Equal(3, fixture.Window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        AuthoringUiFixture.Click(fixture.Control<Button>("Declare Instrument Components dependency"));
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.True(fixture.ViewModel.CanUndoWorkspace);
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume New test plan"));
        dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window.OwnedWindows));
        Assert.Equal("retained-hardware", fixture.Control<TextBox>("Stable plan ID", dialog).Text);
        Assert.Equal("Retained hardware task", fixture.Control<TextBox>("Plan display name", dialog).Text);
        Assert.Equal("Power rail", fixture.Control<TextBox>("Instrument slot", dialog).Text);
        Assert.Equal("TCPIP::192.0.2.18::INSTR", fixture.Control<TextBox>("Instrument address", dialog).Text);
        Assert.Equal("8123", fixture.Control<TextBox>("Instrument I/O timeout ms", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", dialog));
    }

    [AvaloniaFact]
    public void Actual_library_choices_and_retained_type_survive_home_change_with_honest_capability_copy()
    {
        var package = PublishedLibraryFixture.PackageRoot;
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var home = Path.Combine(fixture.WorkspaceRoot, "library-home");
        var payload = Path.Combine(home, "Packages", AuthoringInstrumentCatalog.LibraryPackage);
        Directory.CreateDirectory(payload);
        foreach (var file in new[] { "InstrumentComponents.OpenTap.dll", "InstrumentComponents.dll" }) File.Copy(Path.Combine(package!, file), Path.Combine(payload, file));
        File.Copy(Path.Combine(package!, "package.xml"), Path.Combine(payload, "package.xml"));
        fixture.ViewModel.OpenTapHomeOverride = home;
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        fixture.Control<ComboBox>("Starting point", dialog).SelectedIndex = 2;
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        Assert.Equal("MOCK::INSTR0", fixture.Control<TextBox>("Instrument address", dialog).Text);
        var hardware = fixture.Control<ComboBox>("Hardware choice", dialog);
        Assert.Equal(8, hardware.Items.Count(item => item!.ToString()!.Contains("— Instrument Components", StringComparison.Ordinal)));
        hardware.SelectedItem = hardware.Items.Single(item => item!.ToString()!.StartsWith("Create DC Power Supply", StringComparison.Ordinal));
        Assert.Equal("", fixture.Control<TextBox>("Instrument address", dialog).Text);
        fixture.Control<TextBox>("Instrument slot", dialog).Text = "Rail";
        fixture.Control<TextBox>("Instrument address", dialog).Text = "TCPIP::192.0.2.9::INSTR";
        fixture.Control<TextBox>("Instrument I/O timeout ms", dialog).Text = "8123";
        Assert.Contains("Voltage and average-voltage measurements are unavailable", fixture.Control<TextBlock>("Hardware readiness", dialog).Text);
        var state = dialog.CaptureGuidedForm();
        Assert.Equal("InstrumentComponents.OpenTap.DcPowerSupplyInstrument", state.HardwareTypeId);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", dialog));
        fixture.ViewModel.OpenTapHomeOverride = Path.Combine(fixture.WorkspaceRoot, "foreign-home");
        dialog = new PlanInitializationWindow(fixture.ViewModel, retained: state); dialog.Show(fixture.Window); AuthoringUiFixture.Drain();
        Assert.Contains("DC Power Supply", fixture.Control<ComboBox>("Hardware choice", dialog).SelectedItem!.ToString());
        Assert.Equal("8123", fixture.Control<TextBox>("Instrument I/O timeout ms", dialog).Text);
        Assert.Contains("unavailable", fixture.Control<TextBlock>("Hardware readiness", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Before measurements", StringComparison.Ordinal) == true);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("After measurements", StringComparison.Ordinal) == true);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", dialog));
    }
}
