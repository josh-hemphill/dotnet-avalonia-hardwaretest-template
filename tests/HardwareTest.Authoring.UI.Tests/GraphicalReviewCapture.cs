using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

// Review evidence is opt-in; ordinary tests retain the fast drawing backend.
public sealed class GraphicalReviewCapture
{
    public static bool CaptureEnabled => Environment.GetEnvironmentVariable("HT_CAPTURE_UI") == "1";

    [AvaloniaTheory(SkipUnless = nameof(CaptureEnabled), Skip = "Set HT_CAPTURE_UI=1 to capture review screenshots.")]
    [InlineData("Light", 1280, 800, 14)]
    [InlineData("Dark", 1280, 800, 14)]
    [InlineData("Light", 960, 600, 20)]
    [InlineData("Dark", 960, 600, 20)]
    public async Task Capture_authoring_tasks_and_empty_plan(string theme, int width, int height, int font)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        window.FontSize = font;
        AuthoringUiFixture.Drain();
        var suffix = $"{theme.ToLowerInvariant()}-{width}x{height}-font{font}";
        Capture(window, $"welcome-{suffix}");
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var quick = Assert.Single(window.OwnedWindows);
        Capture(quick, $"quick-create-{suffix}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", quick));
        Capture(quick, $"quick-create-validation-{suffix}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", quick));
        var routes = new (int Index, string Name)[]
        {
            (8, "overview"), (0, "sequence"), (1, "instruments"), (7, "operator-reports"),
            (3, "environment"), (4, "build"), (2, "issues"), (6, "definitions"), (5, "preview")
        };
        foreach (var route in routes)
        {
            fixture.NavigateTask(route.Index);
            if (route.Index == 5) fixture.ViewModel.UseExampleData();
            await fixture.WaitForReadinessAsync();
            Capture(window, $"{route.Name}-{suffix}");
            if (route.Index == 1)
            {
                AuthoringUiFixture.Click(fixture.Control<Button>("Load selected binding"));
                Capture(window, $"instruments-edit-{suffix}");
                var address = fixture.Control<TextBox>("Hardware adapter address");
                address.Text = "MOCK::REVIEW-CAPTURE";
                AuthoringUiFixture.Drain();
                AuthoringUiFixture.Click(fixture.Control<Button>("Review binding change"));
                var review = Assert.Single(window.OwnedWindows);
                Capture(review, $"instruments-review-{suffix}");
                review.Close(false);
                AuthoringUiFixture.Drain();
            }
            if (route.Index == 3)
            {
                fixture.Control<ItemsControl>("Environment package requirements").BringIntoView();
                Capture(window, $"environment-packages-{suffix}");
                AuthoringUiFixture.Click(fixture.Control<Button>("Show package sources and manual import"));
                Capture(window, $"environment-sources-{suffix}");
                fixture.Control<Button>("Copy package requirements").BringIntoView();
                Capture(window, $"environment-manual-end-{suffix}");
            }
        }
        fixture.NavigateTask(0);
        fixture.ViewModel.SelectSequence(fixture.ViewModel.SequenceItems.ToList().FindIndex(row => row.Label == "Acquire VDC"));
        await fixture.WaitForReadinessAsync();
        Capture(window, $"sequence-measurement-{suffix}");
        fixture.ViewModel.SelectSequence(fixture.ViewModel.SequenceItems.ToList().FindIndex(row => row.Label == "Mean GTE"));
        await fixture.WaitForReadinessAsync();
        Capture(window, $"sequence-criterion-{suffix}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        GuidanceUiActions.WaitForAssessment(fixture);
        var guidance = window.FindControl<Border>("GuidanceHost")!;
        Assert.True(guidance.IsEffectivelyVisible);
        Capture(window, $"guidance-{suffix}");
        foreach (var group in guidance.GetVisualDescendants().OfType<Expander>()) group.IsExpanded = true;
        AuthoringUiFixture.Drain();
        Capture(window, $"guidance-expanded-{suffix}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Close guidance"));
        AuthoringUiFixture.Click(fixture.Control<Button>("Add step"));
        var palette = Assert.Single(window.OwnedWindows);
        await fixture.WaitForReadinessAsync();
        Capture(palette, $"step-palette-{suffix}");
        fixture.Control<TextBox>("Search sequence palette", palette).Text = "no-such-step-zzzz";
        AuthoringUiFixture.Drain();
        Capture(palette, $"step-search-empty-{suffix}");
        palette.Close();
        AuthoringUiFixture.Drain();
        fixture.ViewModel.SelectMeasure(0);
        var renaming = window.ExecuteCommandAsync("rename");
        AuthoringUiFixture.Drain();
        var rename = Assert.Single(window.OwnedWindows);
        Capture(rename, $"rename-{suffix}");
        rename.Close(false);
        await renaming;

        var run = new TestRunRecord
        {
            PlanId = fixture.ViewModel.SelectedProgram!.PlanId,
            RunId = "review-recording",
            DutSerial = string.Join(" ", Enumerable.Repeat("Recorded device and test provenance", 30)),
            Samples = [new StoredSample { MetricKey = "VDC", Value = 1, ElapsedMs = 0 }, new StoredSample { MetricKey = "VDC", Value = 2, ElapsedMs = 5 }]
        };
        var recordingPath = Path.Combine(fixture.WorkspaceRoot, "review-recording.json");
        File.WriteAllText(recordingPath, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        fixture.ViewModel.ImportRecording(recordingPath, "review-recording");
        await fixture.WaitForReadinessAsync();
        fixture.NavigateTask(5);
        Capture(window, $"preview-recording-{suffix}");
        var sourceDetails = PreviewUiActions.OpenSourceDetails(fixture);
        Capture(sourceDetails, $"preview-source-details-{suffix}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Close preview data source", sourceDetails));
        var recordingPicker = PreviewUiActions.OpenRecordingPicker(fixture);
        Capture(recordingPicker, $"preview-recording-picker-{suffix}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel recording selection", recordingPicker));

        // Capture the real saved-plan diagnostic disclosure, including its continuation
        // below the fold. Both states use the page viewport rather than a nested list.
        var planPath = Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan");
        var plan = XDocument.Load(planPath);
        var acquisition = plan.Descendants("TestStep").Single(step =>
            ((string?)step.Attribute("type"))?.EndsWith("AcquireVoltageStep", StringComparison.Ordinal) == true);
        acquisition.Add(new XElement("SeriesCompliance", "allSamples"));
        plan.Save(planPath);
        fixture.ViewModel.Open(fixture.WorkspaceRoot);
        await fixture.WaitForReadinessAsync();
        fixture.ViewModel.Validate();
        fixture.NavigateTask(2);
        fixture.Control<Expander>("Editing findings").IsExpanded = false;
        var checkedFinding = Assert.Single(fixture.ViewModel.FindingRows, row =>
            row.Code == HardwareTest.OpenTap.Host.PlanContractValidator.Codes.ComplianceWithoutLimits);
        var savedRows = fixture.Control<ListBox>("Contract findings");
        var checkDetails = savedRows.GetVisualDescendants().OfType<Expander>()
            .Single(expander => ReferenceEquals(expander.DataContext, checkedFinding));
        checkDetails.IsExpanded = true;
        AuthoringUiFixture.Drain();
        var recovery = savedRows.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("authoringAction") && ReferenceEquals(button.DataContext, checkedFinding));
        recovery.BringIntoView();
        AuthoringUiFixture.Drain();
        var issuesViewport = fixture.Control<ScrollViewer>("Issues task viewport");
        var recoveryPosition = recovery.TranslatePoint(default, issuesViewport)!.Value;
        issuesViewport.Offset = new Vector(0, Math.Max(0, issuesViewport.Offset.Y + recoveryPosition.Y - 12));
        Capture(window, $"issues-checked-expanded-{suffix}");
        var lastDetail = checkDetails.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == checkedFinding.NavigationReason);
        lastDetail.BringIntoView();
        Capture(window, $"issues-checked-details-end-{suffix}");

        fixture.ViewModel.ReportError("Package preparation could not finish. The selected home was preserved. "
            + "Open the package source, copy requirements and import a matching offline package.\n"
            + string.Join("\n", Enumerable.Range(1, 12).Select(index => $"Requirement {index}: obtain the declared version from your publisher.")));
        AuthoringUiFixture.Click(fixture.Control<Button>("View operation details"));
        var details = Assert.Single(window.OwnedWindows);
        Capture(details, $"operation-details-{suffix}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Close operation details", details));

        fixture.ViewModel.InitializePlan(new("ui-review-empty"));
        await fixture.WaitForReadinessAsync();
        fixture.NavigateTask(0);
        Capture(window, $"empty-plan-{suffix}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove program"));
        var removal = Assert.Single(window.OwnedWindows);
        Capture(removal, $"program-removal-{suffix}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel destructive operation", removal));
    }

    private static void Capture(Window window, string name)
    {
        AuthoringUiFixture.Drain();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "ui-review");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "font-diagnostics.txt"),
            $"Default={ScottPlot.Fonts.Default}; System={ScottPlot.Fonts.System}; "
            + $"DefaultGlyphs={ScottPlot.Fonts.GetTypeface(ScottPlot.Fonts.Default, false, false).GlyphCount}; "
            + $"DejaVuGlyphs={ScottPlot.Fonts.GetTypeface("DejaVu Sans", false, false).GlyphCount}");
        bitmap.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
