using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HardwareTest.Authoring.Tests;
using HardwareTest.Widgets.MeasurementPlot;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringUsabilityTests
{
    [AvaloniaFact]
    public void Wheel_over_a_chart_scrolls_the_board_and_plot_input_is_an_explicit_choice()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 5;
        AuthoringUiFixture.Drain();
        var viewport = fixture.Control<ScrollViewer>("Operator board viewport");
        Assert.Empty(viewport.GetVisualAncestors().OfType<ScrollViewer>());
        var plot = viewport.GetVisualDescendants().OfType<MeasurementPlotView>().First(p => p.IsVisible);
        Assert.False(plot.IsHitTestVisible);
        plot.BringIntoView(); AuthoringUiFixture.Drain();
        var before = viewport.Offset.Y;
        var point = plot.TranslatePoint(new Point(20, 20), window)!.Value;
        window.MouseWheel(point, new Vector(0, -2), RawInputModifiers.None);
        AuthoringUiFixture.Drain();
        Assert.True(viewport.Offset.Y > before);
        var tile = plot.GetVisualAncestors().OfType<BoardPreviewTileView>().Single();
        var toggle = Assert.Single(tile.GetVisualDescendants().OfType<CheckBox>());
        toggle.IsChecked = true;
        Assert.True(plot.IsHitTestVisible);
        toggle.IsChecked = false;
        Assert.False(plot.IsHitTestVisible);
    }

    [AvaloniaFact]
    public void Resized_inspector_survives_navigation_and_edits_and_clamps_at_the_minimum_window()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(1440, 900); fixture.OpenRememberedWorkspace();
        var divider = window.FindControl<GridSplitter>("InspectorSplitter")!;
        var point = divider.TranslatePoint(new Point(4, 80), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point - new Vector(80, 0));
        window.MouseUp(point - new Vector(80, 0), MouseButton.Left);
        AuthoringUiFixture.Drain();
        var layout = window.FindControl<Grid>("ProgramLayout")!;
        var width = layout.ColumnDefinitions[2].Width.Value;
        Assert.True(width > 400);
        fixture.NavigateTask(3); fixture.NavigateTask(0);
        fixture.ViewModel.SelectMeasure(0); AuthoringUiFixture.Drain();
        Assert.Equal(width, layout.ColumnDefinitions[2].Width.Value);
        window.Width = 960; window.Height = 600; window.FontSize = 20;
        layout.ColumnDefinitions[2].Width = new GridLength(800);
        AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(fixture.Control<ScrollViewer>("Selected step inspector"), window);
        ResponsiveShellTests.Inside(fixture.Control<ListBox>("Program sequence"), window);
        Assert.True(fixture.Control<ListBox>("Program sequence").Bounds.Width >= 360);
    }

    [AvaloniaFact]
    public void Hardware_exposes_physical_catalog_readiness_and_creation_without_leaving_membership_controls()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1;
        AuthoringUiFixture.Drain();
        Assert.Contains("No physical instrument types", fixture.Control<TextBlock>("Supported instrument types summary").Text);
        var package = PublishedLibraryFixture.PackageRoot;
        var home = Path.Combine(fixture.WorkspaceRoot, "library-home");
        var payload = Path.Combine(home, "Packages", AuthoringInstrumentCatalog.LibraryPackage);
        Directory.CreateDirectory(payload);
        foreach (var file in new[] { "InstrumentComponents.OpenTap.dll", "InstrumentComponents.dll" })
            File.Copy(Path.Combine(package, file), Path.Combine(home, file));
        File.Copy(Path.Combine(package, "package.xml"), Path.Combine(payload, "package.xml"));
        vm.OpenTapHomeOverride = home;
        AuthoringUiFixture.Drain();
        Assert.Contains("8 supported physical instrument types", fixture.Control<TextBlock>("Supported instrument types summary").Text);
        window.GetVisualDescendants().OfType<Expander>().Single(expander => Equals(expander.Header, "Browse available types")).IsExpanded = true;
        AuthoringUiFixture.Drain();
        Assert.Equal(9, fixture.Control<ItemsControl>("Supported instrument types").Items.Count);
        fixture.NavigateTask(7);
        fixture.Type(fixture.Control<TextBox>("New operator field from operator settings"), "fixtureId");
        var addField = fixture.Control<Button>("Add operator field from operator settings");
        addField.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(addField);
        Assert.Contains(vm.RequiredFieldChoices, field => field.Id == "fixtureId" && !field.Included);
        fixture.Type(fixture.Control<TextBox>("New report kind from operator settings"), "production");
        var addReport = fixture.Control<Button>("Add report kind from operator settings");
        addReport.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(addReport);
        Assert.Contains(vm.ReportKindChoices, report => report.Id == "production" && !report.Included);
        fixture.NavigateTask(1);
        var templates = fixture.Control<Button>("Manage reusable hardware templates");
        templates.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(templates);
        Assert.Equal(6, window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        var definitions = window.GetVisualDescendants().OfType<WorkspaceDefinitionsView>().Single();
        var heading = definitions.FindControl<Border>("HardwareTemplateSection")!.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "Hardware template catalog");
        ResponsiveShellTests.Inside(heading, window);
    }

    [AvaloniaFact]
    public void Guidance_has_one_scroll_owner_and_a_fixed_close_action_at_large_text()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600); window.FontSize = 20;
        fixture.OpenRememberedWorkspace();
        var returnFocus = fixture.Control<Button>("Add step");
        Assert.True(returnFocus.Focus());
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        var host = window.FindControl<Border>("GuidanceHost")!;
        var viewport = Assert.Single(host.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.Empty(viewport.GetVisualAncestors().OfType<ScrollViewer>());
        viewport.Offset = new Vector(0, 10000); AuthoringUiFixture.Drain();
        var close = fixture.Control<Button>("Close guidance");
        ResponsiveShellTests.Inside(close, window);
        Assert.DoesNotContain(viewport, close.GetVisualAncestors());
        Assert.True(close.IsFocused);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        AuthoringUiFixture.Drain();
        Assert.False(host.IsVisible);
        Assert.True(returnFocus.IsFocused);
    }
}
