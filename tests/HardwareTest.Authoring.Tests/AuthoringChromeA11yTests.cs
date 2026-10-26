using System.Xml.Linq;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringChromeA11yTests
{
    [Fact]
    public void Task_workspace_composes_named_routes_and_async_plan_selection()
    {
        var root = Path.Combine(FindRepoRoot(), "src", "HardwareTest.Authoring");
        var shell = XDocument.Load(Path.Combine(root, "MainWindow.axaml"));
        var host = Assert.Single(shell.Descendants(), element => element.Name.LocalName == "TaskPageHost");
        Assert.Equal("Authoring task workspace", (string?)host.Attribute("AutomationProperties.Name"));
        Assert.Equal(new[] { "Sequence", "Instruments", "Issues", "Environment", "Build", "Preview", "Definitions", "Operator & reports", "Overview" },
            host.Elements().Where(element => element.Name.LocalName == "TabItem").Select(element => (string?)element.Attribute("Header")));
        foreach (var view in new[] { "SequenceEditorView", "SelectedStepInspectorView", "ProgramSettingsView", "WorkspaceEnvironmentView", "WorkspaceBuildView", "WorkspaceDefinitionsView", "OperatorReportsView", "WorkspaceOverviewView" })
            Assert.Single(shell.Descendants(), element => element.Name.LocalName == view);
        Assert.DoesNotContain(shell.Descendants(), element => element.Name.LocalName is "ProgramsRailView" or "OperatorPreviewPane" or "TreeView");
        var plan = Assert.Single(shell.Descendants(), element => (string?)element.Attribute("AutomationProperties.Name") == "Selected test plan");
        Assert.Equal("ComboBox", plan.Name.LocalName);
        Assert.Equal("{Binding SelectedProgramRow, Mode=OneWay}", (string?)plan.Attribute("SelectedItem"));
        Assert.Equal("OnPlanSelectionChanged", (string?)plan.Attribute("SelectionChanged"));
        var loading = File.ReadAllText(Path.Combine(root, "MainWindow.Loading.cs"));
        Assert.Contains("SelectProgramAsync", loading, StringComparison.Ordinal);
        var code = File.ReadAllText(Path.Combine(root, "MainWindow.Shell.cs"));
        Assert.Equal(1, CountOccurrences(code, "WorkspacePreviewView _previewView = new()"));
        Assert.Equal(1, CountOccurrences(code, "WorkspaceIssuesView _issuesView = new()"));
        Assert.Contains("SeparatePreview.Content = _previewView", code, StringComparison.Ordinal);
        Assert.Single(XDocument.Load(Path.Combine(root, "WorkspacePreviewView.axaml")).Descendants(), element => element.Name.LocalName == "OperatorPreviewPane");
        foreach (var name in new[] { "Workspace overview", "Switch task", "Back to previous section", "Open full preview", "Loading workspace or program" })
            Assert.Single(shell.Descendants(), element => (string?)element.Attribute("AutomationProperties.Name") == name);
    }

    [Fact]
    public void Task_lists_and_editor_labels_have_accessible_names_and_wired_handlers()
    {
        var root = Path.Combine(FindRepoRoot(), "src", "HardwareTest.Authoring");
        string[] views = ["SequenceEditorView", "SelectedStepInspectorView", "WorkspaceIssuesView", "WorkspaceEnvironmentView",
            "WorkspaceBuildView", "WorkspacePreviewView", "HardwareView", "WorkspaceDefinitionsView", "ProgramSettingsView",
            "OperatorReportsView", "WorkspaceOverviewView", "StepPaletteView"];
        var documents = views.ToDictionary(view => view, view => XDocument.Load(Path.Combine(root, view + ".axaml")));
        var lists = documents.Values.SelectMany(document => document.Descendants()).Where(element => element.Name.LocalName == "ListBox").ToArray();
        string[] sources = ["SequenceItems", "FindingRows", "DatasetItems", "Instruments", "HardwareDefinitions", "Recipes"];
        Assert.Equal(sources.Length, lists.Length);
        foreach (var source in sources)
        {
            var list = Assert.Single(lists, element => (string?)element.Attribute("ItemsSource") == "{Binding " + source + "}");
            Assert.False(string.IsNullOrWhiteSpace((string?)list.Attribute("AutomationProperties.Name")));
            Assert.Equal("Once", (string?)list.Attribute("KeyboardNavigation.TabNavigation"));
        }
        Assert.Equal(lists.Length, lists.Select(element => (string?)element.Attribute("AutomationProperties.Name")).Distinct().Count());
        foreach (var (view, document) in documents)
        {
            var names = document.Descendants().Attributes(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")).Select(attribute => attribute.Value).ToArray();
            foreach (var label in document.Descendants().Attributes("AutomationProperties.LabeledBy"))
            {
                Assert.StartsWith("{Binding #", label.Value, StringComparison.Ordinal);
                Assert.Single(names, candidate => candidate == label.Value["{Binding #".Length..^1]);
            }
            var code = string.Join(Environment.NewLine, Directory.EnumerateFiles(root, view + ".*.cs").Select(File.ReadAllText));
            foreach (var handler in document.Descendants().Attributes()
                .Where(attribute => attribute.Name.LocalName is "Click" or "KeyDown" or "LostFocus" or "SelectionChanged" or "ValueChanged" or "TextChanged" or "KeyUp" or "PointerReleased" or "GotFocus")
                .Select(attribute => attribute.Value).Distinct())
                Assert.True(code.Contains("void " + handler + "(", StringComparison.Ordinal), $"{view} must handle {handler}.");
        }
        var inspector = documents["SelectedStepInspectorView"];
        var nameBox = Assert.Single(inspector.Descendants(), element => (string?)element.Attribute("AutomationProperties.Name") == "Selected step name");
        Assert.Equal("{Binding SequenceRename}", (string?)nameBox.Attribute("Text"));
        foreach (var name in new[] { "Configure selected step", "Operator display selected step", "Advanced selected step", "Selected step errors", "Raw step XML", "Destination section" })
            Assert.Single(inspector.Descendants(), element => (string?)element.Attribute("AutomationProperties.Name") == name);
        var settingLabel = Assert.Single(inspector.Descendants(), element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{Binding Label}");
        Assert.Equal("0", (string?)settingLabel.Attribute("Grid.Row"));
        var settingError = Assert.Single(settingLabel.Parent!.Elements(), element => (string?)element.Attribute("Grid.Row") == "2");
        Assert.Equal("{Binding Error}", (string?)settingError.Attribute("Text"));
        Assert.Equal("Polite", (string?)settingError.Attribute("AutomationProperties.LiveSetting"));
    }

    [Fact]
    public void Task_actions_keep_guarded_section_palette_preview_and_catalog_workflows()
    {
        var root = Path.Combine(FindRepoRoot(), "src", "HardwareTest.Authoring");
        string Read(string file) => File.ReadAllText(Path.Combine(root, file));
        var sequence = Read("SequenceEditorView.axaml");
        var sequenceCode = Read("SequenceEditorView.axaml.cs");
        Assert.Contains("{Binding NewSectionName}", sequence, StringComparison.Ordinal);
        Assert.Contains("{Binding SectionToggleLabel}", sequence, StringComparison.Ordinal);
        Assert.Contains("Vm?.AddSection()", sequenceCode, StringComparison.Ordinal);
        Assert.Contains("Vm?.ExpandAllSections()", sequenceCode, StringComparison.Ordinal);
        Assert.Contains("SequenceList.AddHandler(KeyDownEvent, OnSequenceKeyDown, RoutingStrategies.Tunnel)", sequenceCode, StringComparison.Ordinal);
        var inspectorCode = Read("SelectedStepInspectorView.axaml.cs");
        foreach (var action in new[] { "RenameSelectedSequence()", "DuplicateSelectedSequence()", "MoveSelectedSequence(-1)", "MoveSelectedSequence(1)", "MoveSelectedToSection()", "ToggleSelectedSection()" })
            Assert.Contains(action, inspectorCode, StringComparison.Ordinal);
        var palette = Read("StepPaletteView.axaml");
        Assert.Contains("{Binding InsertionPositions}", palette, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedRecipePrerequisites}", palette, StringComparison.Ordinal);
        Assert.Contains("{Binding CanInsertRecipe}", palette, StringComparison.Ordinal);
        var paletteCode = Read("MainWindow.StepPalette.cs");
        Assert.Contains("TargetCurrent()", paletteCode, StringComparison.Ordinal);
        Assert.Contains("OnAddRecipe(sender, e)", paletteCode, StringComparison.Ordinal);
        var preview = Read("WorkspacePreviewView.axaml.cs");
        Assert.Contains("ReferenceEquals(workspace, vm.Workspace)", preview, StringComparison.Ordinal);
        Assert.Contains("await vm.ImportRecordingAsync(path, ownerIsCurrent: CurrentSession)", preview, StringComparison.Ordinal);
        var hardware = Read("HardwareView.axaml.cs");
        Assert.Contains("ConfirmHardwareEditAsync()", hardware, StringComparison.Ordinal);
        Assert.Contains("ConfirmInstrumentRemovalAsync()", hardware, StringComparison.Ordinal);
        var definitions = Read("WorkspaceDefinitionsView.axaml.cs");
        foreach (var kind in new[] { "RequiredField", "ReportKind", "ProgramKind" })
            Assert.Contains("RemoveCatalogAsync(CatalogDeletionKind." + kind, definitions, StringComparison.Ordinal);
        var reports = Read("OperatorReportsView.axaml");
        foreach (var label in new[] { "Identity &amp; DUT", "Operator session", "Reports" }) Assert.Contains(label, reports, StringComparison.Ordinal);
        Assert.Contains("{Binding SidecarHelp}", reports, StringComparison.Ordinal);
        Assert.DoesNotContain("Identity &amp; DUT", Read("ProgramSettingsView.axaml"), StringComparison.Ordinal);
        var code = string.Join(Environment.NewLine, Directory.EnumerateFiles(root, "MainWindow.*.cs").Select(File.ReadAllText));
        Assert.Contains("e.Key != Key.Delete || !_viewModel.CanRemoveSelectedSequence", code, StringComparison.Ordinal);
        Assert.Contains("e.Key != Key.Delete || !_viewModel.CanRemoveSelectedProgram", code, StringComparison.Ordinal);
        Assert.Contains("ShouldCommitLostFocusText", code, StringComparison.Ordinal);
        Assert.Contains("_guidedSession == _viewModel.WorkspaceSessionId", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Switching_programs_drops_a_stale_instrument_slot()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-slot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        File.Copy(Path.Combine(src, "authoring.json"), Path.Combine(dest, "authoring.json"));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(dest);
        vm.CreateDemoProgram("one");
        vm.CreateDemoProgram("two");
        vm.SelectProgram("two");
        vm.ReplaceSelected(vm.SelectedProgram! with
        {
            Instruments = [new InstrumentRef("SCOPE", vm.SelectedProgram.Instruments[0].TypeId, "MOCK::SCOPE")],
        });
        vm.SelectProgram("one");
        vm.SelectedInstrumentSlot = "DMM";
        Assert.Equal("DMM", vm.SelectedInstrumentSlot);
        vm.SelectProgram("two");
        Assert.Equal("SCOPE", vm.SelectedInstrumentSlot);
        Assert.Equal("MOCK::SCOPE", vm.SelectedInstrumentVisa);
    }

    [Fact]
    public void Settings_view_labels_theme_and_does_not_reuse_operator_settings()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HardwareTest.Authoring", "SettingsView.axaml"));
        var csproj = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HardwareTest.Authoring", "HardwareTest.Authoring.csproj"));
        Assert.Contains("SelectedItem=\"{Binding ThemePreference}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LastWorkspacePath, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenTapHomeOverride, UpdateSourceTrigger=LostFocus", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LabeledBy", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip", xaml, StringComparison.Ordinal);
        Assert.Contains("Show raw step XML", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenTAP home override", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ISettingsStore", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HardwareTest.Core.Settings", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("HardwareTest\\\\HardwareTest.csproj", csproj, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(token, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            start = index + token.Length;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("dirs.proj").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate dirs.proj above '{AppContext.BaseDirectory}'.");
    }
}
