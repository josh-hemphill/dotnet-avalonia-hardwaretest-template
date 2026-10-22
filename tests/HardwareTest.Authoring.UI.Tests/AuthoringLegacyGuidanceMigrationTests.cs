using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringLegacyGuidanceMigrationTests
{
    [AvaloniaFact]
    public async Task Legacy_section_migration_offers_save_and_check_instead_of_reopening_unchanged_source()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var draft = AuthoringRecipeCatalog.Apply(MockDmmDraftFixture.Create("legacy-guidance"), AuthoringRecipeIds.MeanGte);
        var path = Path.Combine(fixture.WorkspaceRoot, "legacy-guidance.TapPlan");
        new PlanCompiler().Save(draft, path);
        var store = new AuthoringDocumentStore(fixture.WorkspaceRoot);
        store.Save(AuthoringDocumentDto.FromDraft(draft,
            compiledPlanHash: AuthoringDocumentStore.ComputeHash(path),
            compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)))
            with
        { HasConfigurableMeasureSections = false });
        var sourceBytes = File.ReadAllBytes(store.GetDocumentPath(draft.PlanId));
        var planBytes = File.ReadAllBytes(path);
        var adapter = AuthoringInstrumentCatalog.All.Single(item => item.DisplayName == "Mock DMM");
        var package = Path.Combine(fixture.WorkspaceRoot, OpenTapHomeBootstrapper.DefaultHomeRelativePath, "Packages", adapter.RequiredPackage);
        Directory.CreateDirectory(package);
        new XElement("Package", new XAttribute("Name", adapter.RequiredPackage),
            new XElement("Files", new XElement("File", new XAttribute("Path", adapter.AssemblyFile))))
            .Save(Path.Combine(package, "package.xml"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, adapter.AssemblyFile), Path.Combine(package, adapter.AssemblyFile));

        fixture.Show(); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.SelectProgram(draft.PlanId);
        await fixture.WaitForReadinessAsync();
        Assert.IsType<SectionNode>(Assert.Single(fixture.ViewModel.SelectedProgram!.Measure));
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        var feedback = GuidanceUiActions.Feedback(fixture).Text!;
        Assert.DoesNotContain("Saved source differs", feedback);
        Assert.DoesNotContain("First voltage test complete", feedback);
        Assert.Equal("Save and check draft", fixture.Control<Button>("Guidance next action").Content);
        Assert.Equal(sourceBytes, File.ReadAllBytes(store.GetDocumentPath(draft.PlanId)));
        Assert.Equal(planBytes, File.ReadAllBytes(path));
        AuthoringUiFixture.Click(GuidanceUiActions.Action(fixture, "Save and check draft"));
        Assert.Contains("First voltage test complete", GuidanceUiActions.Feedback(fixture).Text);
        Assert.True(store.Load(draft.PlanId).Document!.HasConfigurableMeasureSections);
    }
}
