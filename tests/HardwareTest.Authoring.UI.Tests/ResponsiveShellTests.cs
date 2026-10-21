using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class ResponsiveShellTests
{
    [AvaloniaTheory]
    [InlineData(960, 600, 14, 1)]
    [InlineData(1280, 800, 14, 1)]
    [InlineData(960, 600, 20, 1.5)]
    [InlineData(1280, 800, 20, 1.5)]
    public void Actual_client_bounds_keep_sequence_inspector_and_preview_visible(int width, int height, int fontSize, double scaling)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        window.FontSize = fontSize;
        window.SetRenderScaling(scaling);
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.SelectSequence(fixture.ViewModel.SequenceItems.ToList().FindIndex(row => row.Label == "Acquire VDC"));
        AuthoringUiFixture.Drain();
        Assert.Equal(new Size(width, height), window.ClientSize);
        Assert.Equal(scaling, window.RenderScaling);
        var instrumentLabel = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MetricSlotLabel");
        instrumentLabel.Text = "Instrument slot with a longer translated description for this measurement";
        AuthoringUiFixture.Drain();
        var sequence = fixture.Control<ListBox>("Program sequence");
        var inspector = fixture.Control<ScrollViewer>("Selected step inspector");
        Inside(sequence, window);
        Inside(inspector, window);
        Assert.True(sequence.Bounds.Height >= 40, $"Sequence viewport {sequence.Bounds} must be usable.");
        Assert.True(inspector.Bounds.Height >= 80);
        foreach (var name in new[] { "Metric instrument slot", "Channel key", "SampleCount" })
        {
            var field = window.GetVisualDescendants().OfType<Control>()
                .First(c => c.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(c) == name);
            field.BringIntoView();
            AuthoringUiFixture.Drain();
            Inside(field, window);
        }
        fixture.ViewModel.SelectSequence(fixture.ViewModel.SequenceItems.ToList().FindIndex(row => row.Label == "Mean GTE"));
        AuthoringUiFixture.Drain();
        var criterion = fixture.Control<TextBox>("Threshold");
        criterion.BringIntoView();
        AuthoringUiFixture.Drain();
        Inside(criterion, window);

        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 5;
        AuthoringUiFixture.Drain();
        var preview = fixture.Control<OperatorPreviewPane>("Operator preview chrome");

        Inside(preview, window);
        Inside(fixture.Control<ListBox>("Recordings"), window);
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0;
        AuthoringUiFixture.Drain();
        Inside(sequence, window);
        Inside(inspector, window);
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1;
        AuthoringUiFixture.Drain();
        foreach (var name in new[] { "Instrument slot", "VISA address" })
        {
            var field = window.GetVisualDescendants().OfType<Control>().First(c => c.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(c) == name);
            field.BringIntoView();
            AuthoringUiFixture.Drain();
            Inside(field, window);
        }
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public void Navigation_restores_editor_focus_selection_and_scroll()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.SelectSequence(fixture.ViewModel.SequenceItems.ToList().FindIndex(row => row.Label == "Acquire VDC"));
        AuthoringUiFixture.Drain();
        var selection = fixture.ViewModel.SelectedSequenceIndex;
        var field = fixture.Control<TextBox>("History watch percent");
        field.BringIntoView();
        AuthoringUiFixture.Drain();
        Assert.True(field.Focus());
        var scroll = fixture.Control<ScrollViewer>("Selected step inspector");
        var offset = scroll.Offset;
        var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
        fixture.NavigateTask( 3);
        Assert.Equal(3, tabs.SelectedIndex);
        fixture.NavigateTask( 0);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.True(field.IsFocused);
        Inside(field, window);
        Assert.Equal(selection, fixture.ViewModel.SelectedSequenceIndex);
        Assert.Equal(offset, scroll.Offset);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task Protection_measures_large_heading_and_decision_rows_with_many_programs()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.FontSize = 22;
        fixture.OpenRememberedWorkspace();
        var interaction = new AuthoringLifecycleInteraction(window);
        var choice = interaction.ChooseAsync(Enumerable.Range(0, 100)
            .Select(i => new DirtyProgramSummary($"Program {i} with a much longer translated display name", true, true)).ToArray(), true);
        AuthoringUiFixture.Drain();
        var dialog = Assert.Single(window.OwnedWindows);
        dialog.SetRenderScaling(1.5);
        var decisions = dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string content && new[] { "Cancel", "Discard", "Save all" }.Contains(content)).ToArray();
        var cancel = Assert.Single(decisions, b => Equals(b.Content, "Cancel"));
        foreach (var button in decisions) button.Content = button.Content + " — keep this translated decision label completely visible";
        AuthoringUiFixture.Drain();
        var scroll = fixture.Control<ScrollViewer>("Unsaved program list", dialog);
        Inside(scroll, dialog);
        Assert.True(scroll.Bounds.Height > 40);
        Assert.Equal(3, decisions.Length);
        foreach (var button in decisions)
        {
            Inside(button, dialog);
            Inside(Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()), dialog);
        }
        Assert.True(cancel.IsFocused);
        AuthoringUiFixture.Click(cancel);
        Assert.Equal(UnsavedChangesChoice.Cancel, await choice);
    }

    [AvaloniaFact]
    public void Many_long_finding_rows_scroll_and_open_the_real_program_from_issues()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var path = Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan");
        var xml = XDocument.Load(path);
        var acquisition = xml.Descendants("TestStep").Single(step => ((string?)step.Attribute("type"))?.EndsWith("AcquireVoltageStep", StringComparison.Ordinal) == true);
        acquisition.Add(new XElement("SeriesCompliance", "allSamples"));
        xml.Save(path);
        var window = fixture.Show(960, 600);
        window.FontSize = 20;
        window.SetRenderScaling(1.5);
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.Validate();
        var seed = Assert.Single(fixture.ViewModel.FindingRows, item => item.Code == PlanContractValidator.Codes.ComplianceWithoutLimits);
        Assert.False(seed.IsStale);
        Assert.NotNull(seed.CheckedIdentity);
        var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
        fixture.NavigateTask( 2);
        var findings = fixture.Control<ListBox>("Contract findings");
        findings.BringIntoView(); AuthoringUiFixture.Drain();
        // Expand the projection while preserving a real check's session, revision and saved-byte identity.
        var rows = Enumerable.Range(0, 80).Select(i => seed with
        {
            Finding = new PlanContractFinding(PlanContractSeverity.Warning, $"W{i}",
                "A longer translated finding message that remains readable in the constrained Issues viewport", "Measure"),
            NodeId = null,
            NavigationLabel = "Open program settings",
            NavigationReason = "No verified source field is available; opens program settings."
        }).ToArray();
        typeof(AuthoringWorkspaceViewModel).GetProperty(nameof(AuthoringWorkspaceViewModel.FindingRows))!
            .SetValue(fixture.ViewModel, rows);
        AuthoringUiFixture.Drain();
        Inside(findings, window);
        findings.ScrollIntoView(rows[^1]);
        AuthoringUiFixture.Drain();
        var scroll = Assert.Single(findings.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.True(scroll.Offset.Y > 0);
        var row = Assert.IsAssignableFrom<Control>(findings.ContainerFromIndex(rows.Length - 1));
        var open = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Open program settings"));
        open.BringIntoView();
        AuthoringUiFixture.Drain();
        Inside(open, window);
        AuthoringUiFixture.Click(open);
        Assert.Equal(7, tabs.SelectedIndex);
        Assert.Equal("sample", fixture.ViewModel.SelectedProgram!.PlanId);
        Assert.Null(fixture.ViewModel.Error);
        var displayName = fixture.Control<TextBox>("Display name");
        displayName.BringIntoView();
        AuthoringUiFixture.Drain();
        Assert.True(displayName.Focus());
        Inside(displayName, window);
        fixture.NavigateTask( 0);
        Inside(fixture.Control<ListBox>("Program sequence"), window);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }

    internal static void Inside(Control control, TopLevel window)
    {
        Assert.True(control.IsEffectivelyVisible, $"{control.GetType().Name} {Avalonia.Automation.AutomationProperties.GetName(control)} must be visible");
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, $"{control.GetType().Name} {Avalonia.Automation.AutomationProperties.GetName(control)} must have positive bounds: {control.Bounds}");
        var origin = control.TranslatePoint(default, window);
        Assert.NotNull(origin);
        Assert.True(new Rect(window.ClientSize).Contains(new Rect(origin.Value, control.Bounds.Size)),
            $"{control.GetType().Name}: {origin} / {control.Bounds.Size} outside {window.ClientSize}");
        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>()
            .Where(ancestor => ancestor is ScrollContentPresenter || ancestor.ClipToBounds))
        {
            var point = control.TranslatePoint(default, ancestor);
            Assert.NotNull(point);
            // Layout at fractional DPI can round the viewport edge by less than one logical pixel.
            var clip = new Rect(ancestor.Bounds.Size).Inflate(0.75);
            Assert.True(clip.Contains(new Rect(point.Value, control.Bounds.Size)),
                $"{control.GetType().Name} at {point} / {control.Bounds.Size} must fit {ancestor.GetType().Name} viewport {ancestor.Bounds.Size}");
        }
    }
}
