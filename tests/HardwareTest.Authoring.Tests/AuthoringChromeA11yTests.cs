using System.Xml.Linq;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringChromeA11yTests
{
    [Fact]
    public void Program_window_has_sequence_inspector_preview_and_list_tab_once()
    {
        var sourceRoot = Path.Combine(FindRepoRoot(), "src", "HardwareTest.Authoring");
        var shell = File.ReadAllText(Path.Combine(sourceRoot, "MainWindow.axaml"));
        string[] viewNames = ["ProgramsRailView", "SequenceEditorView", "SelectedStepInspectorView",
            "WorkspaceIssuesView", "WorkspaceEnvironmentView", "WorkspaceBuildView", "WorkspacePreviewView", "HardwareView", "WorkspaceDefinitionsView"];
        var views = viewNames.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(sourceRoot, name + ".axaml")));
        // Inspect the actual composed surface, including the single preview constructed and moved by the shell.
        var xaml = string.Join(Environment.NewLine, new[] { shell }.Concat(views.Values));
        var shellDocument = XDocument.Parse(shell);
        foreach (var view in viewNames.Where(name => name is not "WorkspacePreviewView" and not "HardwareView"))
            Assert.Single(shellDocument.Descendants(), element => element.Name.LocalName == view);
        Assert.Single(shellDocument.Descendants(), element => element.Name.LocalName == "ProgramSettingsView");
        Assert.Single(XDocument.Parse(views["WorkspacePreviewView"]).Descendants(), element => element.Name.LocalName == "OperatorPreviewPane");
        Assert.DoesNotContain(shellDocument.Descendants(), element => element.Name.LocalName == "OperatorPreviewPane");
        Assert.Equal(new[] { "Program", "Hardware", "Issues", "Environment", "Build", "Preview", "Definitions" },
            shellDocument.Descendants().Where(element => element.Name.LocalName == "TabItem").Select(element => (string?)element.Attribute("Header")));
        var shellCode = File.ReadAllText(Path.Combine(sourceRoot, "MainWindow.Shell.cs"));
        Assert.Equal(1, CountOccurrences(shellCode, "WorkspacePreviewView _previewView = new()"));
        Assert.Contains("_previewView.DataContext = _viewModel", shellCode, StringComparison.Ordinal);
        Assert.Contains("previous.Content = null", shellCode, StringComparison.Ordinal);
        Assert.Contains("destination.Content = _previewView", shellCode, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(this, \"Operator preview chrome\")",
            File.ReadAllText(Path.Combine(sourceRoot, "OperatorPreviewPane.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("TreeView", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SequenceTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InspectorTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PreviewTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<vm:OperatorPreviewPane", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"Preview samples\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Hardware\" AutomationProperties.Name=\"Program settings tab\"", shell, StringComparison.Ordinal);
        Assert.Contains("<vm:ProgramSettingsView", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"Settings tab\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<vm:SettingsView", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnOpenSettings\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding SettingsTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ShowCatalogFormulaCompletions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedProgramRow}\"", xaml, StringComparison.Ordinal);
        var programSettingsSource = File.ReadAllText(Path.Combine(sourceRoot, "ProgramSettingsView.axaml"));
        Assert.Single(XDocument.Parse(programSettingsSource).Descendants(), element => element.Name.LocalName == "HardwareView");
        var programSettings = programSettingsSource.Replace("<vm:HardwareView/>", views["HardwareView"])
            .Replace("</ScrollViewer>", views["WorkspaceDefinitionsView"] + "</ScrollViewer>");
        var hardwareHeading = Assert.Single(XDocument.Parse(programSettings).Descendants(),
            element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{Binding ProgramSettingsTitle}");
        Assert.Equal("2", (string?)hardwareHeading.Attribute("AutomationProperties.HeadingLevel"));
        Assert.Contains("SelectedItem=\"{Binding SelectedInstrument}\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("Identity &amp; DUT", programSettings, StringComparison.Ordinal);
        Assert.Contains("Operator session", programSettings, StringComparison.Ordinal);
        Assert.Contains("Text=\"Reports\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("Program type &amp; station health", programSettings, StringComparison.Ordinal);
        Assert.Contains("Text=\"Instruments\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding RequireStationHealth}\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText=\"fixtureId\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText=\"traceability\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText=\"incomingInspect\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText=\"SCOPE\"", programSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding CatalogsPurpose}\"", programSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBlock Text=\"{Binding SidecarHelp}\"", programSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("Wrap\" Text=\"{Binding SidecarHelp}\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding SidecarHelp}\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding SidecarHelp}\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("Selection includes cleanup", programSettings, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(programSettings, "IsVisible=\"{Binding CanRemove}\""));
        Assert.Contains("Click=\"OnRemoveRequiredField\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnRemoveReportKind\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnRemoveProgramKind\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ProgramKindChoices}\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("StringFormat='Remove required field {0} from workspace'", programSettings, StringComparison.Ordinal);
        Assert.Contains("StringFormat='Remove report kind {0} from workspace'", programSettings, StringComparison.Ordinal);
        Assert.Contains("StringFormat='Remove program kind {0} from workspace'", programSettings, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanRemoveSelectedInstrumentSlot}\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("InstrumentRemovalGuardText", programSettings, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnRemoveInstrumentSlot\"", programSettings, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InputStringFieldId}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InputNumberFieldId}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding FormulaPrefixCompletions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding AddRecipeToolTip}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAddRecipe\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding RecipeAddHint}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding RecipeAdvancedHint}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RawStepsBanner}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasRawStepEditor}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Raw step XML\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Last shipped bake-time files", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ShipPurpose}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Declared shell-app projects\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Ship shell-app projects\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Authoring OpenTAP home packages\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OnOpenLastWorkspace", xaml, StringComparison.Ordinal);
        var code = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HardwareTest.Authoring", "MainWindow.axaml.cs"));
        Assert.Contains("ApplyFormulaCompletion", code, StringComparison.Ordinal);
        Assert.Contains("OnOpenSettings", code, StringComparison.Ordinal);
        Assert.Contains("OnRemoveSequence", code, StringComparison.Ordinal);
        Assert.Contains("OnRemoveProgram", code, StringComparison.Ordinal);
        Assert.Contains("OnMetricSettingLostFocus", code, StringComparison.Ordinal);
        Assert.Contains("ShouldCommitLostFocusText", code, StringComparison.Ordinal);
        Assert.Contains("OnMetricSettingBoolChanged", code, StringComparison.Ordinal);
        Assert.Contains("OnMetricSettingChoiceChanged", code, StringComparison.Ordinal);
        Assert.Contains("OnMetricSettingNumberChanged", code, StringComparison.Ordinal);
        Assert.Contains("e.Key != Key.Delete || !_viewModel.CanRemoveSelectedSequence", code, StringComparison.Ordinal);
        Assert.Contains("e.Key != Key.Delete || !_viewModel.CanRemoveSelectedProgram", code, StringComparison.Ordinal);
        Assert.Contains(".Show(this)", code, StringComparison.Ordinal);
        var settingsWindow = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HardwareTest.Authoring", "SettingsWindow.axaml"));
        Assert.Contains("Title=\"{Binding SettingsTitle}\"", settingsWindow, StringComparison.Ordinal);
        Assert.Contains("<vm:SettingsView", settingsWindow, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SequenceItems}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex=\"{Binding SelectedSequenceIndex}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsEnabled\" Value=\"{Binding IsSelectable}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LeftIndentConverter.Instance", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasFormula}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasTransferFunction}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasMetricPresentation}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasStepSettings}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding MetricFunctionChoices}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedMetricFunction}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AuthoringInspectorCopy.OperatorYUnitLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("AuthoringInspectorCopy.MeasureRecipeLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("AuthoringInspectorCopy.HistoryWatchPlaceholder", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding MetricSettingRows}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Label}\"", xaml, StringComparison.Ordinal);
        var settingLabel = Assert.Single(XDocument.Parse(views["SelectedStepInspectorView"]).Descendants(),
            element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{Binding Label}");
        Assert.Equal("0", (string?)settingLabel.Attribute("Grid.Row"));
        Assert.Equal("Auto,Auto,Auto", (string?)settingLabel.Parent!.Attribute("RowDefinitions"));
        var settingError = Assert.Single(settingLabel.Parent.Elements(), element => (string?)element.Attribute("Grid.Row") == "2");
        Assert.Equal("{Binding Error}", (string?)settingError.Attribute("Text"));
        Assert.Equal("Polite", (string?)settingError.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.LiveSetting").Value);
        Assert.Single(settingLabel.Parent.Elements(), element => (string?)element.Attribute("Grid.Row") == "1");
        Assert.Contains("Text=\"{Binding Summary}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Value, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsBoolean}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsChoice}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding UsesTextInput}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RepeatCount}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Selected step errors\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Configure selected step\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Operator display selected step\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Advanced selected step\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding HistoryEnabled}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OnMetricSettingLostFocus", xaml, StringComparison.Ordinal);
        Assert.Contains("OnMetricSettingBoolChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasRepeatEditor}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Code, StringFormat='{}{0}'}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Message}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Severity}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OnImportTransferFunction", xaml, StringComparison.Ordinal);
        Assert.Contains("OnFormulaChip", xaml, StringComparison.Ordinal);
        Assert.Contains("OnRemoveSequence", xaml, StringComparison.Ordinal);
        Assert.Contains("OnRemoveProgram", xaml, StringComparison.Ordinal);
        Assert.Contains("OnSequenceKeyDown", xaml, StringComparison.Ordinal);
        Assert.Contains("OnProgramsKeyDown", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanRemoveSelectedSequence}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanRemoveSelectedProgram}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding RemoveSelectedPurpose}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding RemoveProgramPurpose}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Remove selected\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Remove program\"", xaml, StringComparison.Ordinal);
        var gettingStarted = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "getting-started.md"));
        Assert.Contains("**Remove program** (or Delete on the programs list)", gettingStarted, StringComparison.Ordinal);
        Assert.Contains("**Remove selected** (or Delete on the sequence list)", gettingStarted, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LabeledBy", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "ItemsSource=\"{Binding SequenceItems}\"",
            xaml,
            StringComparison.Ordinal);
        var documents = new[] { shell, programSettingsSource }.Concat(views.Values).Select(XDocument.Parse).ToArray();
        var lists = documents.SelectMany(document => document.Descendants()).Where(element => element.Name.LocalName == "ListBox").ToArray();
        string[] listSources = ["{Binding ProgramRows}", "{Binding SequenceItems}", "{Binding FindingRows}",
            "{Binding DatasetItems}", "{Binding Instruments}", "{Binding HardwareDefinitions}"];
        Assert.Equal(listSources.Length, lists.Length);
        foreach (var source in listSources)
        {
            var list = Assert.Single(lists, element => (string?)element.Attribute("ItemsSource") == source);
            Assert.Equal("Once", (string?)list.Attribute("KeyboardNavigation.TabNavigation"));
            Assert.False(string.IsNullOrWhiteSpace((string?)list.Attribute("AutomationProperties.Name")));
        }
        Assert.Equal(lists.Length, lists.Select(element => (string?)element.Attribute("AutomationProperties.Name")).Distinct().Count());
        foreach (var document in documents)
        {
            var names = document.Descendants().Attributes(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")).Select(attribute => attribute.Value).ToArray();
            foreach (var label in document.Descendants().Attributes("AutomationProperties.LabeledBy"))
            {
                Assert.StartsWith("{Binding #", label.Value, StringComparison.Ordinal);
                var name = label.Value["{Binding #".Length..^1];
                Assert.Single(names, candidate => candidate == name);
            }
        }
        foreach (var view in viewNames.Where(name => name != "WorkspacePreviewView"))
        {
            var viewCode = File.ReadAllText(Path.Combine(sourceRoot, view + ".axaml.cs"));
            foreach (var handler in XDocument.Parse(views[view]).Descendants().Attributes()
                .Where(attribute => attribute.Name.LocalName is "Click" or "KeyDown" or "LostFocus" or "SelectionChanged" or "ValueChanged" or "TextChanged" or "KeyUp" or "PointerReleased" or "GotFocus")
                .Select(attribute => attribute.Value).Distinct())
            {
                Assert.True(viewCode.Contains("private void " + handler, StringComparison.Ordinal)
                    || viewCode.Contains("private async void " + handler, StringComparison.Ordinal));
                if (view == "SequenceEditorView" && handler is "OnRenameSequence" or "OnDuplicateSequence" or "OnMoveSequenceUp" or "OnMoveSequenceDown")
                {
                    var actions = new Dictionary<string, string>
                    {
                        ["OnRenameSequence"] = "Vm?.RenameSelectedSequence()",
                        ["OnDuplicateSequence"] = "Vm?.DuplicateSelectedSequence()",
                        ["OnMoveSequenceUp"] = "Vm?.MoveSelectedSequence(-1)",
                        ["OnMoveSequenceDown"] = "Vm?.MoveSelectedSequence(1)"
                    };
                    Assert.Contains(actions[handler], viewCode, StringComparison.Ordinal);
                    continue;
                }
                if (view is "HardwareView" or "WorkspaceDefinitionsView")
                {
                    var localActions = new Dictionary<string, string>
                    {
                        ["OnLoadBinding"] = "Vm?.LoadHardwareEditor()",
                        ["OnReviewBinding"] = "owner.ConfirmHardwareEditAsync()",
                        ["OnAddInstrumentSlot"] = "Vm?.AddInstrumentSlot()",
                        ["OnRemoveInstrumentSlot"] = "owner.ConfirmInstrumentRemovalAsync()",
                        ["OnAddDefinition"] = "Vm?.AddHardwareDefinition()",
                        ["OnLoadDefinition"] = "Vm?.LoadHardwareDefinitionEditor()",
                        ["OnUpdateDefinition"] = "Vm?.UpdateHardwareDefinition()",
                        ["OnIncludeDefinition"] = "Vm?.IncludeHardwareDefinition()",
                        ["OnRemoveDefinition"] = "owner.ConfirmHardwareDefinitionRemovalAsync()",
                        ["OnAddRequiredField"] = "Vm?.AddWorkspaceRequiredField()",
                        ["OnAddReportKind"] = "Vm?.AddWorkspaceReportKind()",
                        ["OnAddProgramKind"] = "Vm?.AddWorkspaceProgramKind()",
                        ["OnRemoveRequiredField"] = "RemoveCatalogAsync(CatalogDeletionKind.RequiredField, row.Id)",
                        ["OnRemoveReportKind"] = "RemoveCatalogAsync(CatalogDeletionKind.ReportKind, row.Id)",
                        ["OnRemoveProgramKind"] = "RemoveCatalogAsync(CatalogDeletionKind.ProgramKind, row.Id)",
                    };
                    Assert.Contains(localActions[handler], viewCode, StringComparison.Ordinal);
                    continue;
                }
                if (view == "SelectedStepInspectorView" && handler == "OnMetricSettingTextChanged")
                {
                    Assert.Contains("row.IsNumber && SettingWindow(sender) is not null", viewCode, StringComparison.Ordinal);
                    Assert.Contains("row.NodeId == vm.SelectedSequence?.NodeId", viewCode, StringComparison.Ordinal);
                    Assert.Contains("vm.SetMetricSetting(row.Key, box.Text ?? string.Empty)", viewCode, StringComparison.Ordinal);
                    continue;
                }
                if (view == "SelectedStepInspectorView" && handler == "OnFormulaThreshold")
                {
                    Assert.Contains("ConfigureExpander.IsExpanded = true", viewCode, StringComparison.Ordinal);
                    Assert.Contains("ThresholdBox.Focus()", viewCode, StringComparison.Ordinal);
                    Assert.Contains("ThresholdBox.BringIntoView()", viewCode, StringComparison.Ordinal);
                    continue;
                }
                Assert.Contains("?." + handler + "(sender, e)", viewCode, StringComparison.Ordinal);
                Assert.Contains(handler, code, StringComparison.Ordinal);
            }
        }
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
        vm.CreateProgram("one");
        vm.CreateProgram("two");
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
