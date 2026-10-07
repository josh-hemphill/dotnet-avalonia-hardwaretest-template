using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace HardwareTest.Authoring;

public sealed partial class PlanInitializationWindow
{
    private void ArrangeStageSections()
    {
        // Group the existing controls after direct/guided mode has assigned them to stages.
        // No request or retained-state model is created by this presentation layer.
        string[][] titles = _guided
            ? [["Plan identity and destination", "Starting task"], ["Instrument configuration", "Package readiness and recovery", "Before measurements", "After measurements"],
                ["Measurement output", "Sampling"], ["Pass decision"], ["Generated draft review"], ["Save an editable source draft"]]
            : [["Plan identity", "Workspace destination"], ["Starting task"], ["Instrument configuration", "Package readiness and recovery"],
                ["Before measurements", "After measurements"], ["Measurement output", "Sampling", "Pass decision"], ["Generated draft review"]];
        int[][] lengths = _guided ? [[4, 2], [4, 2, 6, 3], [3, 4], [3], [1], [1]] : [[3, 2], [2], [4, 2], [6, 3], [3, 2, 2], [1]];
        for (var stageIndex = 0; stageIndex < _stages.Length; stageIndex++)
        {
            var stage = _stages[stageIndex];
            var controls = stage.Children.ToArray(); stage.Children.Clear();
            stage.Spacing = 16;
            var offset = 0;
            for (var section = 0; section < titles[stageIndex].Length; section++)
            {
                var count = lengths[stageIndex][section];
                stage.Children.Add(AuthoringFormLayout.Section(titles[stageIndex][section], controls.Skip(offset).Take(count).ToArray()));
                offset += count;
            }
        }
    }

    private Grid BuildFormLayout(Control content, Control buttons)
    {
        var footer = new StackPanel { Spacing = 8 };
        // Error details can grow without pushing Back/Next/Cancel outside the dialog.
        footer.Children.Add(new ScrollViewer { Content = _error, MaxHeight = 80, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        footer.Children.Add(buttons);
        return AuthoringFormLayout.Frame(_guided ? "Create your first test plan" : "New test plan",
            "Choose a task and its hardware, review the generated actions, then save an editable draft.", content, footer, _heading);
    }

    private void SuggestSlot(HardwareChoice? choice)
    {
        if (!_automaticSlot || choice?.Adapter is null) return;
        var suggestion = choice.Resource?.SlotName ?? (AuthoringInstrumentCatalog.IsLibrary(choice.Adapter.TypeId) ? "INSTR1" : "DMM");
        if (_slot.Text == suggestion) return;
        _updatingSlot = true;
        try { _slot.Text = suggestion; }
        finally { _updatingSlot = false; }
    }
}
