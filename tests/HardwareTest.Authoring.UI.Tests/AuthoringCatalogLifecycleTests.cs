using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringCatalogLifecycleTests
{
    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("enter")]
    [InlineData("escape")]
    [InlineData("dismiss")]
    public void Global_only_dirty_state_is_visible_in_real_lifecycle_modal_and_Pack_guard_and_cancels_safely(string route)
    {
        using var fixture = Loaded(); StageGlobalOnly(fixture);
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 4; AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<Button>("Pack workspace").IsEnabled); Assert.Contains("workspace catalog", fixture.Control<TextBlock>("Pack save guard").Text); Assert.Contains("Save All", fixture.Control<TextBlock>("Pack save guard").Text);
        fixture.Window!.Close(); AuthoringUiFixture.Drain(); var dialog = Assert.Single(fixture.Window!.OwnedWindows);
        Assert.Contains("workspace catalog", dialog.Title); var text = Assert.IsType<TextBlock>(fixture.Control<ScrollViewer>("Unsaved program list", dialog).Content);
        Assert.Contains("Workspace catalog changes", text.Text); Assert.Empty(fixture.ViewModel.DirtyPrograms);
        var cancel = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Cancel")); Assert.True(cancel.IsDefault); Assert.True(cancel.IsCancel); AssertInside(cancel, dialog);
        if (route == "cancel") AuthoringUiFixture.Click(cancel);
        else if (route == "dismiss") dialog.Close();
        else { var key = route == "enter" ? Key.Enter : Key.Escape; dialog.KeyPress(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape, null); AuthoringUiFixture.Drain(); }
        Assert.Empty(fixture.Window!.OwnedWindows); Assert.True(fixture.Window!.IsVisible); Assert.True(fixture.ViewModel.WorkspaceCatalogDirty);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_lifecycle_SaveAll_manifest_guard_failure_retains_window_session_and_catalog_feedback(bool navigate)
    {
        using var fixture = Loaded(); StageGlobalOnly(fixture); var session = fixture.ViewModel.Workspace; var draft = fixture.ViewModel.SelectedProgram;
        var manifest = Path.Combine(fixture.WorkspaceRoot, "authoring.json"); var before = File.ReadAllText(manifest);
        File.WriteAllText(manifest, before.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 999", StringComparison.Ordinal)); var futureBytes = File.ReadAllBytes(manifest);
        Task<bool>? request = null;
        if (navigate) request = fixture.Window!.ReopenWorkspaceAsync(); else fixture.Window!.Close();
        AuthoringUiFixture.Drain(); var dialog = Assert.Single(fixture.Window!.OwnedWindows); Assert.Contains("Workspace catalog changes", Assert.IsType<TextBlock>(fixture.Control<ScrollViewer>("Unsaved program list", dialog).Content).Text);
        AuthoringUiFixture.Click(Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Save all")));
        if (request is not null) Assert.False(await request); AuthoringUiFixture.Drain();
        Assert.True(fixture.Window!.IsVisible); Assert.Same(session, fixture.ViewModel.Workspace); Assert.Same(draft, fixture.ViewModel.SelectedProgram);
        Assert.Empty(fixture.ViewModel.DirtyProgramIds); Assert.True(fixture.ViewModel.WorkspaceCatalogDirty); Assert.False(fixture.ViewModel.LastSaveAllResult!.Succeeded);
        Assert.Contains("future-schema", fixture.ViewModel.WorkspaceCatalogSaveFailure); Assert.Equal(futureBytes, File.ReadAllBytes(manifest));
        Assert.Contains("Workspace catalog:", string.Join("\n", fixture.Control<ItemsControl>("Save all results").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
        Assert.Contains("retry Save All", fixture.Control<TextBlock>("Authoring error").Text);
        File.WriteAllText(manifest, before);
    }

    [AvaloniaFact]
    public void UI_SaveAll_reports_catalog_success_separately_and_preview_warning_does_not_make_persistence_fail()
    {
        using var fixture = Loaded(); StageGlobalOnly(fixture); fixture.ViewModel.OpenTapHomeOverride = "invalid\0home";
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all")); var result = fixture.ViewModel.LastSaveAllResult!;
        Assert.True(result.Succeeded); Assert.True(result.WorkspaceCatalogSaved); Assert.Empty(result.SavedProgramIds); Assert.Null(result.WorkspaceCatalogFailure);
        Assert.Contains(fixture.Control<ItemsControl>("Save all results").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Saved workspace catalog");
        Assert.Contains("OpenTAP home setting", fixture.Control<TextBlock>("Authoring error").Text); Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.Contains("fixtureId", AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot).Manifest.Catalogs!.RequiredFields);
    }

    [AvaloniaFact]
    public void Shown_SaveAll_preserves_invalid_document_feedback_after_valid_save_and_home_correction()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot); workspace.Manifest.Package.Name = "Warning feedback programs";
        AuthoringWorkspaceLoader.SaveManifest(fixture.WorkspaceRoot, workspace.Manifest);
        fixture.Show(960, 600, realInteraction: true); fixture.OpenRememberedWorkspace(); var vm = fixture.ViewModel;
        vm.CreateProgram("a-invalid"); vm.ApplyRecipe(AuthoringRecipeIds.MeanGte); vm.Threshold = string.Empty;
        vm.CreateProgram("z-valid"); vm.OpenTapHomeOverride = "invalid\0home";
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        Assert.True(vm.LastSaveAllResult!.Succeeded); Assert.Equal(["a-invalid", "z-valid"], vm.LastSaveAllResult.SavedProgramIds);
        Assert.False(vm.HasUnsavedChanges); Assert.Contains(AuthoringCompileCodes.MissingLimits, fixture.Control<TextBlock>("Authoring error").Text);
        var saved = File.ReadAllBytes(new AuthoringDocumentStore(fixture.WorkspaceRoot).GetDocumentPath("a-invalid"));
        vm.OpenTapHomeOverride = Path.Combine(fixture.WorkspaceRoot, "valid-missing-home"); AuthoringUiFixture.Drain();
        Assert.Contains(AuthoringCompileCodes.MissingLimits, fixture.Control<TextBlock>("Authoring error").Text);
        Assert.DoesNotContain("OpenTAP home setting", fixture.Control<TextBlock>("Authoring error").Text);
        Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.SavePreviewWarning!);
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 4; AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<Button>("Pack workspace").IsEffectivelyEnabled);
        Assert.Equal(saved, File.ReadAllBytes(new AuthoringDocumentStore(fixture.WorkspaceRoot).GetDocumentPath("a-invalid")));
        vm.SelectProgram("a-invalid"); vm.Threshold = "2"; vm.Apply(); AuthoringUiFixture.Drain();
        Assert.Null(vm.SavePreviewWarning); Assert.Null(vm.Error); Assert.False(fixture.Control<TextBlock>("Authoring error").IsVisible);
    }

    [AvaloniaFact]
    public void Catalog_and_many_program_changes_scroll_in_lifecycle_modal_with_all_choices_inside_minimum_window()
    {
        using var fixture = Loaded(); StageGlobalOnly(fixture);
        for (var i = 0; i < 35; i++) fixture.ViewModel.CreateProgram($"global-{i:00}-{new string('x', 110)}");
        fixture.Window!.Close(); AuthoringUiFixture.Drain(); var dialog = Assert.Single(fixture.Window!.OwnedWindows);
        var scroll = fixture.Control<ScrollViewer>("Unsaved program list", dialog); AssertInside(scroll, dialog); Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        foreach (var button in dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string)) AssertInside(button, dialog);
        var point = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), dialog); Assert.NotNull(point);
        dialog.MouseWheel(point.Value, new Vector(0, -1000), RawInputModifiers.None); AuthoringUiFixture.Drain(); Assert.True(scroll.Offset.Y > 0);
        AuthoringUiFixture.Click(Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Cancel"))); Assert.True(fixture.Window!.IsVisible);
    }

    private static AuthoringUiFixture Loaded()
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true); fixture.Show(960, 600, realInteraction: true); fixture.OpenRememberedWorkspace(); return fixture;
    }
    private static void StageGlobalOnly(AuthoringUiFixture fixture)
    {
        var vm = fixture.ViewModel; vm.NewRequiredField = "fixtureId"; vm.AddRequiredField(); vm.SaveProgram("sample"); AuthoringUiFixture.Drain();
        Assert.True(vm.WorkspaceCatalogDirty); Assert.True(vm.HasUnsavedChanges); Assert.Empty(vm.DirtyPrograms);
    }
    private static void AssertInside(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible); var point = control.TranslatePoint(default, window); Assert.NotNull(point); Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.True(new Rect(window.ClientSize).Contains(new Rect(point.Value, control.Bounds.Size)), $"{control.GetType().Name} at {point} ({control.Bounds.Size}) must fit inside {window.ClientSize}.");
    }
}
