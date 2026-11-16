using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringFirstSaveGuidanceTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compiled_only_import_offers_a_visible_first_authoring_save_and_preserves_the_editable_content(bool missingSidecar)
    {
        using var fixture = LoadedCompiledOnly(missingSidecar);
        var vm = fixture.ViewModel;
        var store = new AuthoringDocumentStore(fixture.WorkspaceRoot);
        var imported = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        Assert.NotNull(vm.SelectedFirstAuthoringSaveBaseline);
        Assert.False(store.Load("sample").Exists);

        Resume(fixture);
        Assert.Equal("Imported plan needs its first authoring save", GuidanceUiActions.Feedback(fixture).Text);
        var save = fixture.Control<Button>("Guidance next action");
        Assert.Equal("Save authoring draft", save.Content);
        save.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(save.IsEffectivelyVisible);
        Assert.True(save.IsEnabled);
        AuthoringUiFixture.Click(save);

        var saved = Assert.IsType<AuthoringDocumentDto>(store.Load("sample").Document);
        Assert.True(imported.ContentEquals(AuthoringDocumentSnapshot.Capture(saved.ToDraft())));
        Assert.Null(vm.SelectedFirstAuthoringSaveBaseline);
        Resume(fixture);
        Assert.DoesNotContain("first authoring save", GuidanceUiActions.Feedback(fixture).Text!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("Save authoring draft", fixture.Control<Button>("Guidance next action").Content);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void External_compiled_or_sidecar_change_replaces_first_save_with_recovery_without_publishing_source(bool sidecar)
    {
        using var fixture = LoadedCompiledOnly();
        var vm = fixture.ViewModel;
        var store = new AuthoringDocumentStore(fixture.WorkspaceRoot);
        var document = vm.SelectedDocument;
        var imported = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        Assert.NotNull(vm.SelectedFirstAuthoringSaveBaseline);
        var compiled = vm.Workspace!.TapPlanPaths.Single(path => Path.GetFileNameWithoutExtension(path) == "sample");
        var changed = sidecar ? PlanCompiler.SidecarPath(compiled) : compiled;
        // Both edits remain syntactically readable; this checks identity, not parser failure.
        File.AppendAllText(changed, sidecar ? "\n " : "\n<!-- external modification -->\n");
        var externalPlan = File.ReadAllBytes(compiled);
        var externalSidecar = File.ReadAllBytes(PlanCompiler.SidecarPath(compiled));

        Resume(fixture);
        Assert.Equal("Saved input is unavailable or unsafe", GuidanceUiActions.Feedback(fixture).Text);
        Assert.Equal("Review recovery issues", fixture.Control<Button>("Guidance next action").Content);
        var details = fixture.Control<Expander>("Guidance recovery details");
        details.IsExpanded = true; AuthoringUiFixture.Drain();
        Assert.Contains(details.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text?.Contains("Imported files changed outside this session", StringComparison.Ordinal) == true);
        AuthoringUiFixture.Click(fixture.Control<Button>("Guidance next action"));
        Assert.Equal(2, fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        Assert.False(store.Load("sample").Exists);
        Assert.Equal(externalPlan, File.ReadAllBytes(compiled));
        Assert.Equal(externalSidecar, File.ReadAllBytes(PlanCompiler.SidecarPath(compiled)));
        Assert.Same(document, vm.SelectedDocument);
        Assert.True(imported.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
    }

    [AvaloniaFact]
    public void Deleted_source_after_first_authoring_save_never_reoffers_import_creation()
    {
        using var fixture = LoadedCompiledOnly();
        var vm = fixture.ViewModel;
        var store = new AuthoringDocumentStore(fixture.WorkspaceRoot);
        Assert.NotNull(vm.SelectedFirstAuthoringSaveBaseline);
        Resume(fixture);
        Assert.Equal("Save authoring draft", fixture.Control<Button>("Guidance next action").Content);
        AuthoringUiFixture.Click(fixture.Control<Button>("Guidance next action"));
        Assert.True(store.Load("sample").Exists);
        Assert.Null(vm.SelectedFirstAuthoringSaveBaseline);
        var document = vm.SelectedDocument;
        var saved = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        File.Delete(store.GetDocumentPath("sample"));

        Resume(fixture);
        Assert.Equal("Saved input is unavailable or unsafe", GuidanceUiActions.Feedback(fixture).Text);
        Assert.Equal("Review recovery issues", fixture.Control<Button>("Guidance next action").Content);
        Assert.Null(vm.SelectedFirstAuthoringSaveBaseline);
        Assert.False(store.Load("sample").Exists);
        Assert.Same(document, vm.SelectedDocument);
        Assert.True(saved.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
    }

    private static AuthoringUiFixture LoadedCompiledOnly(bool missingSidecar = false)
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        if (missingSidecar) File.Delete(PlanCompiler.SidecarPath(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan")));
        fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.ViewModel.SelectProgram("sample");
        AuthoringUiFixture.Drain();
        return fixture;
    }

    private static void Resume(AuthoringUiFixture fixture)
    {
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        GuidanceUiActions.WaitForAssessment(fixture);
    }
}
