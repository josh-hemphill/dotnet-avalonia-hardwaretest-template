using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringChromeA11yTests
{
    [Fact]
    public void Program_window_has_sequence_inspector_preview_and_list_tab_once()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HardwareTest.Authoring", "MainWindow.axaml"));
        Assert.DoesNotContain("TreeView", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SequenceTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InspectorTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PreviewTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<vm:OperatorPreviewPane", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"Preview samples\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding ProgramSettingsTitle}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"Settings tab\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<vm:SettingsView", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnOpenSettings\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding SettingsTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ShowCatalogFormulaCompletions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedProgram}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedInstrument}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InputStringFieldId}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InputNumberFieldId}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding FormulaPrefixCompletions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RecipeAddHint}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RecipeAdvancedHint}\"", xaml, StringComparison.Ordinal);
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
        Assert.Contains("SelectedItem=\"{Binding MetricFunctionId}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding MetricSettingRows}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Value, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding HistoryEnabled}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OnMetricSettingLostFocus", xaml, StringComparison.Ordinal);
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
        var sequenceBlock = SliceAfter(xaml, "ItemsSource=\"{Binding SequenceItems}\"");
        Assert.Contains("KeyboardNavigation.TabNavigation=\"Once\"", sequenceBlock, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(xaml, "KeyboardNavigation.TabNavigation=\"Once\"") >= 4,
            "Sequence, recordings, findings, and instruments lists should leave on Tab.");
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

    private static string SliceAfter(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, marker);
        return text[index..Math.Min(text.Length, index + 600)];
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
