using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringAppSettingsTests
{
    [Fact]
    public void Open_remembers_last_workspace_and_offer_does_not_auto_open()
    {
        var workspace = NewWorkspace();
        var store = NewStore();
        store.Current.LastWorkspace = workspace;
        store.Save();

        var idle = new AuthoringWorkspaceViewModel(preferences: Reload(store.FilePath));
        Assert.True(idle.CanOfferLastWorkspace);
        Assert.Null(idle.Workspace);
        Assert.Equal(workspace, idle.LastWorkspacePath);

        idle.OpenLastWorkspace();
        Assert.NotNull(idle.Workspace);
        Assert.False(idle.CanOfferLastWorkspace);
        Assert.Equal(Path.GetFullPath(workspace), idle.LastWorkspacePath);
    }

    [Fact]
    public void Theme_and_show_raw_persist_and_raise_theme_changed()
    {
        var store = NewStore();
        var vm = new AuthoringWorkspaceViewModel(preferences: store);
        string? seen = null;
        vm.ThemePreferenceChanged += theme => seen = theme;
        vm.ThemePreference = "dark";
        vm.ShowRawStepXml = false;

        Assert.Equal(AuthoringThemePreference.Dark, seen);
        var reload = new AuthoringWorkspaceViewModel(preferences: Reload(store.FilePath));
        Assert.Equal(AuthoringThemePreference.Dark, reload.ThemePreference);
        Assert.False(reload.ShowRawStepXml);
    }

    [Fact]
    public void Bootstrap_uses_override_only_when_home_directory_is_unset()
    {
        var store = NewStore();
        var vm = new AuthoringWorkspaceViewModel(preferences: store);
        vm.OpenTapHomeOverride = "/opt/authoring-home";
        var fromPrefs = vm.ResolveBootstrapOptions(new BootstrapOptions { Offline = true });
        Assert.Equal("/opt/authoring-home", fromPrefs.HomeDirectory);
        Assert.True(fromPrefs.Offline);

        var explicitHome = vm.ResolveBootstrapOptions(new BootstrapOptions
        {
            HomeDirectory = "/tmp/cli-home",
            Offline = false,
        });
        Assert.Equal("/tmp/cli-home", explicitHome.HomeDirectory);
        Assert.False(explicitHome.Offline);
    }

    [Fact]
    public void Raw_xml_editor_follows_show_raw_pref_on_a_raw_row()
    {
        var store = NewStore();
        var vm = new AuthoringWorkspaceViewModel(preferences: store);
        vm.Open(NewWorkspace());
        vm.CreateProgram("raw-pref");
        vm.ReplaceSelected(vm.SelectedProgram! with
        {
            Measure = [new RawStepNode("HangForeverStep", "<TestStep />")],
        });
        var raw = vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Raw);
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(raw));
        Assert.True(vm.HasRawStep);
        Assert.True(vm.HasRawStepEditor);
        vm.ShowRawStepXml = false;
        Assert.True(vm.HasRawStep);
        Assert.False(vm.HasRawStepEditor);
        Assert.Equal("<TestStep />", vm.RawXml);
    }

    [Fact]
    public void Prefix_completions_appear_for_a_partial_ident()
    {
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(NewWorkspace());
        vm.CreateProgram("prefix");
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.FormulaSource = "me";
        vm.RefreshFormulaCompletions(2);
        Assert.True(vm.HasFormulaPrefixCompletions);
        Assert.Contains(vm.FormulaPrefixCompletions, item => item.Name == "mean");
        Assert.DoesNotContain(vm.FormulaPrefixCompletions, item => item.Name == "std");
        vm.FormulaSource = "mean(";
        vm.RefreshFormulaCompletions(5);
        Assert.False(vm.HasFormulaPrefixCompletions);
    }

    [Fact]
    public void Default_view_model_does_not_write_application_data()
    {
        var path = AuthoringPreferencesStore.DefaultFilePath();
        var existed = File.Exists(path);
        var stamp = existed ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        var vm = new AuthoringWorkspaceViewModel();
        vm.ThemePreference = "Dark";
        vm.ShowRawStepXml = false;
        if (existed)
        {
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        }
        else
        {
            Assert.False(File.Exists(path));
        }
    }

    [Fact]
    public void Read_only_prefs_do_not_overwrite_future_schema()
    {
        var path = Path.Combine(NewTempDir(), AuthoringPreferencesStore.FileName);
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 999,
              "themePreference": "Light",
              "futureOnly": "keep"
            }
            """);
        var store = new AuthoringPreferencesStore(path);
        store.Load();
        var vm = new AuthoringWorkspaceViewModel(preferences: store);
        Assert.True(vm.PreferencesReadOnly);
        Assert.False(vm.PreferencesEditable);
        vm.ThemePreference = "Dark";
        Assert.Equal(AuthoringThemePreference.Light, vm.ThemePreference);
        Assert.Contains("futureOnly", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(AuthoringThemePreference.Light, Reload(path).Current.ThemePreference);
    }

    private static AuthoringPreferencesStore NewStore()
    {
        var store = new AuthoringPreferencesStore(Path.Combine(NewTempDir(), AuthoringPreferencesStore.FileName));
        store.Load();
        return store;
    }

    private static AuthoringPreferencesStore Reload(string path)
    {
        var store = new AuthoringPreferencesStore(path);
        store.Load();
        return store;
    }

    private static string NewWorkspace()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        File.Copy(
            Path.Combine(FindRepoRoot(), "plans", "opentap", "authoring.json"),
            Path.Combine(dest, "authoring.json"));
        return dest;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-settings-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("HardwareTest.slnx").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate HardwareTest.slnx above '{AppContext.BaseDirectory}'.");
    }
}
