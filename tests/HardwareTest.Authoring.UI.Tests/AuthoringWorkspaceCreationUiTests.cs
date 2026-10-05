using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringWorkspaceCreationUiTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Welcome_creation_reviews_packages_and_files_then_opens_editable_standard_workspace(int template)
    {
        using var fixture = new AuthoringUiFixture(); fixture.Show();
        var root = Path.Combine(Path.GetDirectoryName(fixture.WorkspaceRoot)!, "created");
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace from welcome"));
        var dialog = Assert.IsType<WorkspaceCreationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Set(fixture, dialog, "Workspace destination", root); Set(fixture, dialog, "Workspace display name", "New board");
        Set(fixture, dialog, "Package name", "Board product"); Set(fixture, dialog, "Package version", "1.2.3");
        fixture.Control<ComboBox>("Workspace template", dialog).SelectedIndex = template;
        Assert.False(fixture.Control<CheckBox>("Include VISA DMM package", dialog).IsChecked == true);
        Assert.False(fixture.Control<Button>("Create workspace", dialog).IsEnabled);
        AuthoringUiFixture.Click(fixture.Control<Button>("Review workspace creation", dialog));
        var review = fixture.Control<TextBlock>("Workspace creation review", dialog).Text;
        Assert.Contains("Board product 1.2.3", review); Assert.Contains("HardwareTest Basic", review);
        Assert.Contains("authoring-drafts/workspace.authoring.json", review); Assert.Contains("authoring.json", review);
        Assert.False(Directory.Exists(root));
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace", dialog));
        Assert.Empty(fixture.Window.OwnedWindows); Assert.Equal(root, fixture.ViewModel.Workspace!.Root);
        Assert.False(fixture.ViewModel.HasUnsavedChanges); Assert.Equal(root, fixture.Preferences.Current.LastWorkspace);
        if (template == 0)
        {
            Assert.Empty(fixture.ViewModel.Programs);
            AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
            var plan = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window.OwnedWindows));
            AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", plan));
        }
        else
        {
            Assert.Equal("voltage", fixture.ViewModel.SelectedProgram!.PlanId);
            if (template == 1) Assert.Empty(fixture.ViewModel.SelectedProgram.Instruments);
            else Assert.Single(fixture.ViewModel.SelectedProgram.Instruments);
            fixture.ViewModel.DisplayName = "Edited"; Assert.True(fixture.ViewModel.SaveAll().Succeeded);
            fixture.ViewModel.Open(root); fixture.ViewModel.SelectProgram("voltage");
            Assert.Equal("Edited", fixture.ViewModel.DisplayName);
        }
    }

    [AvaloniaFact]
    public void Optional_continue_uses_existing_plan_initializer_and_cancel_does_not_publish()
    {
        using var fixture = new AuthoringUiFixture(); fixture.Show();
        var root = Path.Combine(Path.GetDirectoryName(fixture.WorkspaceRoot)!, "created");
        OpenCommand(fixture);
        var dialog = Assert.IsType<WorkspaceCreationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Set(fixture, dialog, "Workspace destination", root); Set(fixture, dialog, "Workspace display name", "Workspace"); Set(fixture, dialog, "Package name", "Package");
        AuthoringUiFixture.Click(fixture.Control<Button>("Review workspace creation", dialog));
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel workspace creation", dialog));
        Assert.False(Directory.Exists(root)); Assert.False(fixture.ViewModel.HasWorkspace);
        OpenCommand(fixture);
        dialog = Assert.IsType<WorkspaceCreationWindow>(Assert.Single(fixture.Window.OwnedWindows));
        Set(fixture, dialog, "Workspace destination", root); Set(fixture, dialog, "Workspace display name", "Workspace"); Set(fixture, dialog, "Package name", "Package");
        fixture.Control<CheckBox>("Continue to New test plan", dialog).IsChecked = true;
        AuthoringUiFixture.Click(fixture.Control<Button>("Review workspace creation", dialog));
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace", dialog));
        var continuation = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window.OwnedWindows));
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", continuation));
        Assert.True(fixture.ViewModel.HasWorkspace); Assert.Empty(fixture.ViewModel.Programs);
    }

    [AvaloniaFact]
    public async Task Dirty_current_workspace_cancel_preserves_current_session_and_creates_nothing()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true); fixture.Show(); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.SelectProgram("sample"); fixture.ViewModel.DisplayName = "Unsaved";
        var workspace = fixture.ViewModel.Workspace; var session = fixture.ViewModel.SelectedDocument;
        var root = Path.Combine(Path.GetDirectoryName(fixture.WorkspaceRoot)!, "cancelled");
        Assert.False(await fixture.Window!.CreateWorkspaceAsync(new(root, "Workspace", "Package")));
        Assert.Same(workspace, fixture.ViewModel.Workspace); Assert.Same(session, fixture.ViewModel.SelectedDocument);
        Assert.True(fixture.ViewModel.HasUnsavedChanges); Assert.False(Directory.Exists(root));
    }

    [AvaloniaFact]
    public void Changed_review_and_conflict_keep_form_open_and_preserve_bytes()
    {
        using var fixture = new AuthoringUiFixture(); fixture.Show();
        var root = Path.Combine(Path.GetDirectoryName(fixture.WorkspaceRoot)!, "created");
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace from welcome"));
        var dialog = Assert.IsType<WorkspaceCreationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Set(fixture, dialog, "Workspace destination", root); Set(fixture, dialog, "Workspace display name", "Workspace"); Set(fixture, dialog, "Package name", "Package");
        AuthoringUiFixture.Click(fixture.Control<Button>("Review workspace creation", dialog));
        Set(fixture, dialog, "Package name", "Changed"); AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace", dialog));
        Assert.Contains("Review the updated", fixture.Control<TextBlock>("Workspace creation error", dialog).Text); Assert.False(Directory.Exists(root));
        AuthoringUiFixture.Click(fixture.Control<Button>("Review workspace creation", dialog));
        Directory.CreateDirectory(root); var conflict = Path.Combine(root, "authoring.json"); File.WriteAllText(conflict, "keep");
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace", dialog));
        Assert.Same(dialog, Assert.Single(fixture.Window.OwnedWindows)); Assert.False(fixture.ViewModel.HasWorkspace); Assert.Equal("keep", File.ReadAllText(conflict));
    }

    [AvaloniaTheory]
    [InlineData(false, "Save all")]
    [InlineData(false, "Discard")]
    [InlineData(true, "Save all")]
    [InlineData(true, "Discard")]
    public void Closed_creation_form_ignores_late_real_dirty_workspace_choice(bool windowClose, string choice)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(realInteraction: true); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.SelectProgram("sample"); fixture.ViewModel.DisplayName = "Retain unsaved creation draft";
        var workspace = fixture.ViewModel.Workspace; var session = fixture.ViewModel.SelectedDocument;
        var program = fixture.ViewModel.SelectedProgram;
        var remembered = fixture.Preferences.Current.LastWorkspace;
        var authoredBytes = Directory.GetFiles(fixture.WorkspaceRoot).ToDictionary(path => path, File.ReadAllBytes);
        var root = Path.Combine(Path.GetDirectoryName(fixture.WorkspaceRoot)!, "cancelled-delayed");
        OpenCommand(fixture);
        var creation = Assert.IsType<WorkspaceCreationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Set(fixture, creation, "Workspace destination", root); Set(fixture, creation, "Workspace display name", "Workspace"); Set(fixture, creation, "Package name", "Package");
        fixture.Control<CheckBox>("Continue to New test plan", creation).IsChecked = true;
        AuthoringUiFixture.Click(fixture.Control<Button>("Review workspace creation", creation));
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace", creation));
        var prompt = Assert.Single(fixture.Window.OwnedWindows, window => window != creation);
        Assert.True(prompt.IsVisible); Assert.True(creation.IsVisible); Assert.False(Directory.Exists(root));
        if (windowClose) { creation.Close(); AuthoringUiFixture.Drain(); }
        else AuthoringUiFixture.Click(fixture.Control<Button>("Cancel workspace creation", creation));
        Assert.False(creation.IsVisible); Assert.True(prompt.IsVisible);
        AuthoringUiFixture.Click(Assert.Single(prompt.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, choice)));
        Assert.False(Directory.Exists(root)); Assert.Empty(fixture.Window.OwnedWindows);
        Assert.Same(workspace, fixture.ViewModel.Workspace); Assert.Same(session, fixture.ViewModel.SelectedDocument);
        Assert.Same(program, fixture.ViewModel.SelectedProgram);
        Assert.Equal("Retain unsaved creation draft", fixture.ViewModel.DisplayName);
        Assert.True(fixture.ViewModel.HasUnsavedChanges); Assert.Null(fixture.ViewModel.LastSaveAllResult);
        Assert.Equal(remembered, fixture.Preferences.Current.LastWorkspace);
        foreach (var (path, bytes) in authoredBytes) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.True(fixture.Window.IsVisible);
    }

    [AvaloniaFact]
    public void Product_manifest_supports_actual_guided_physical_selection_creation_save_and_reopen()
    {
        using var fixture = new AuthoringUiFixture(); fixture.Show();
        var root = Path.Combine(Path.GetDirectoryName(fixture.WorkspaceRoot)!, "physical-product");
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace from welcome"));
        var creation = Assert.IsType<WorkspaceCreationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Set(fixture, creation, "Workspace destination", root); Set(fixture, creation, "Workspace display name", "Physical product"); Set(fixture, creation, "Package name", "Physical tests");
        fixture.Control<ComboBox>("Workspace template", creation).SelectedIndex = 1;
        fixture.Control<CheckBox>("Continue to New test plan", creation).IsChecked = true;
        AuthoringUiFixture.Click(fixture.Control<Button>("Review workspace creation", creation));
        Assert.Contains("HardwareTest VISA ^0.1.0", fixture.Control<TextBlock>("Workspace creation review", creation).Text);
        Assert.Contains("Classification: Product", fixture.Control<TextBlock>("Workspace creation review", creation).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace", creation));
        var plan = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window.OwnedWindows));
        Set(fixture, plan, "Plan display name", "Physical voltage"); Set(fixture, plan, "Stable plan ID", "physical");
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", plan));
        fixture.Control<ComboBox>("Starting point", plan).SelectedIndex = 1;
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", plan));
        fixture.Control<ComboBox>("Hardware choice", plan).SelectedIndex = 1;
        Set(fixture, plan, "Instrument address", "TCPIP0::127.0.0.1::inst0::INSTR");
        Assert.Contains("dependency declared", fixture.Control<TextBlock>("Hardware readiness", plan).Text);
        for (var step = 0; step < 3; step++) AuthoringUiFixture.Click(fixture.Control<Button>("Next", plan));
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", plan));
        Assert.Empty(fixture.Window.OwnedWindows);
        var binding = Assert.Single(fixture.ViewModel.SelectedProgram!.Instruments);
        Assert.Equal(AuthoringInstrumentCatalog.All.Single(adapter => adapter.DisplayName == "VISA DMM").TypeId, binding.TypeId);
        Assert.Equal("TCPIP0::127.0.0.1::inst0::INSTR", binding.VisaAddress);
        fixture.ViewModel.DisplayName = "Saved physical draft";
        Assert.True(fixture.ViewModel.SaveAll().Succeeded);
        fixture.ViewModel.Open(root); fixture.ViewModel.SelectProgram("physical");
        Assert.Equal("Saved physical draft", fixture.ViewModel.DisplayName);
        var reopened = Assert.Single(fixture.ViewModel.SelectedProgram!.Instruments);
        Assert.Equal(binding.SlotName, reopened.SlotName);
        Assert.Equal(binding.TypeId, reopened.TypeId);
        Assert.Equal(binding.VisaAddress, reopened.VisaAddress);
        Assert.Equal(binding.OpaqueResourceXml, reopened.OpaqueResourceXml);
        Assert.Equal(binding.Settings.OrderBy(setting => setting.Key), reopened.Settings.OrderBy(setting => setting.Key));
        Assert.True(AuthoringInstrumentCatalog.DeclaresVisa(fixture.ViewModel.Workspace!));
        Assert.Equal("^0.1.0", Assert.Single(fixture.ViewModel.Workspace!.Manifest.Dependencies, package => package.Package == OpenTapHomeBootstrapper.VisaPackageName).Version);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }

    private static void OpenCommand(AuthoringUiFixture fixture)
    {
        AuthoringUiFixture.Click(fixture.Control<Button>("Command palette"));
        var palette = Assert.Single(fixture.Window!.OwnedWindows);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace command", palette));
    }

    private static void Set(AuthoringUiFixture fixture, Window dialog, string name, string value)
    {
        fixture.Control<TextBox>(name, dialog).Text = value; AuthoringUiFixture.Drain();
    }
}
