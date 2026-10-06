using Avalonia.Controls;

namespace HardwareTest.Authoring;

/// Raw local form inputs only; the shared initializer owns the document and its stable IDs.
public sealed record GuidedFormState(int Stage, string?[] Text, int[] Choices, bool?[] Toggles)
{
    public InstrumentRef? ReusedInstrument { get; init; }
    public string? HardwareTypeId { get; init; }
    public string? TimeoutText { get; init; }
    public bool AutomaticDemoAddress { get; init; }
    public bool AutomaticSlot { get; init; }
}

public sealed partial class PlanInitializationWindow
{
    private readonly bool _guided;
    private int _retainedResourceIndex = -1;
    private readonly TextBlock _intervalApplicability = Text("Sampling interval applicability");
    private TextBox[] GuidedText => [_name, _id, _family, _destination, _slot, _address, _fields, _confirmation, _fixture, _channel, _unit, _samples, _interval, _threshold];
    private CheckBox[] GuidedToggles => [_serial, _identity, _shutdown, _measurement, _criterion];

    public GuidedFormState CaptureGuidedForm() => new(_stage, GuidedText.Select(box => box.Text).ToArray(),
        [_starting.SelectedIndex, _hardware.SelectedIndex], GuidedToggles.Select(box => box.IsChecked).ToArray())
    {
        ReusedInstrument = SelectedHardware?.Resource is { } resource ? CopyResource(resource) : null,
        HardwareTypeId = SelectedHardware?.Adapter?.TypeId,
        TimeoutText = _timeout.Text,
        AutomaticDemoAddress = _automaticDemoAddress,
        AutomaticSlot = _automaticSlot
    };

    private static InstrumentRef CopyResource(InstrumentRef resource) => resource with
    {
        Settings = new Dictionary<string, string>(resource.Settings, StringComparer.Ordinal)
    };

    private static bool SameResource(InstrumentRef first, InstrumentRef second) => first.SlotName == second.SlotName
        && first.TypeId == second.TypeId && first.VisaAddress == second.VisaAddress && first.OpaqueResourceXml == second.OpaqueResourceXml
        && first.Settings.Count == second.Settings.Count && first.Settings.All(pair => second.Settings.TryGetValue(pair.Key, out var value) && pair.Value == value);

    private void RestoreGuidedForm(GuidedFormState state)
    {
        _idEdited = true; // Restore the captured ID before deferred name notifications can regenerate it.
        _starting.SelectedIndex = state.Choices[0];
        if (state.ReusedInstrument is { } retained)
        {
            var index = _reusable.FindIndex(resource => SameResource(resource, retained));
            if (index < 0)
            {
                index = _reusable.Count; _reusable.Add(CopyResource(retained)); _retainedResourceIndex = index;
                ShowHardwareChoices();
            }
            _hardware.SelectedItem = _hardwareChoices.First(choice => choice.Resource is { } r && SameResource(r, _reusable[index]));
        }
        else if (state.HardwareTypeId is { } typeId) SelectHardwareType(typeId);
        else _hardware.SelectedIndex = 0;
        _timeout.Text = state.TimeoutText ?? "5000";
        for (var index = 0; index < GuidedText.Length; index++) GuidedText[index].Text = state.Text[index];
        for (var index = 0; index < GuidedToggles.Length; index++) GuidedToggles[index].IsChecked = state.Toggles[index];
        _automaticDemoAddress = state.AutomaticDemoAddress;
        _automaticSlot = state.AutomaticSlot;
        _stage = state.Stage;
    }

    private void ConfigureGuidedStages()
    {
        Title = "First test plan";
        _starting.SelectedIndex = 1;
        _identity.IsChecked = true;
        _criterion.IsChecked = true;
        _criterion.Content = "Pass when average voltage reaches the threshold";
        _interval.Text = "";
        _criterion.IsCheckedChanged += (_, _) => ShowGuidedInterval();
        _measurement.IsCheckedChanged += (_, _) => ShowGuidedInterval();
        _interval.TextChanged += (_, _) => ShowGuidedInterval();
        ShowGuidedInterval();
        _create.Content = "Save draft and continue";
        foreach (var stage in _stages)
        {
            foreach (var label in stage.Children.OfType<StackPanel>()) label.Children.Clear();
            stage.Children.Clear();
        }
        _stages[0] = Stage(Label("Display name", _name), Label("Stable plan ID", _id), Label("Device family", _family),
            Label("Draft filename", _destination), Label("Task template", _starting));
        _stages[0].Children.Add(new TextBlock { Text = "Voltage task uses your physical instrument. Demo explicitly uses a Mock DMM. Empty plan goes directly to the editor.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        _stages[1] = Stage(_hardware, Label("Instrument name", _slot), Label("Instrument address", _address), Label("I/O timeout (ms)", _timeout), _readiness, _environment,
            new TextBlock { Text = "Before measurements · instrument identity, DUT and operator requirements", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            _serial, _identity, Label("Operator fields", _fields), Label("Fixture confirmation", _confirmation), Label("Fixture field", _fixture),
            new TextBlock { Text = "After measurements · output off, then reset named instrument slots", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, _shutdown, _coverage);
        _stages[2] = Stage(_measurement, Label("Voltage result name", _channel), Label("Unit", _unit), Label("Samples", _samples), Label("Interval (ms)", _interval), _intervalApplicability);
        _stages[3] = Stage(_criterion, Label("Minimum average voltage", _threshold),
            new TextBlock { Text = "Choose a pass threshold in the measurement unit. You can edit the criterion and sampling in the normal editor later.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        _stages[4] = Stage(_review);
        _stages[5] = Stage(new TextBlock { Text = "Save the source draft, then use the shared editor preview, issues and Save/check actions. Physical instrument packages must be prepared before compilation. No bench deployment is performed.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        _starting.SelectionChanged += (_, _) =>
        {
            if (_starting.SelectedIndex == 0)
            {
                _hardware.SelectedIndex = 0; _identity.IsChecked = false; _criterion.IsChecked = false;
                _stage = 5; ShowStage();
            }
        };
    }

    private void ShowGuidedInterval()
    {
        var average = _criterion.IsChecked == true;
        _interval.IsEnabled = _measurement.IsChecked == true && (!average || !string.IsNullOrEmpty(_interval.Text));
        _intervalApplicability.Text = average
            ? "The average pass criterion does not use a sampling interval. Turn off this criterion to keep an interval, or clear a retained interval before continuing with it."
            : "Sampling interval applies to the voltage acquisition. Enter milliseconds; incomplete text is retained as a draft issue.";
    }

    private void EnsureGuidedIntervalApplicable()
    {
        if (_guided && _measurement.IsChecked == true && _criterion.IsChecked == true && !string.IsNullOrEmpty(_interval.Text))
            throw new AuthoringWorkspaceException("The average pass criterion cannot use the entered sampling interval. Turn off the criterion to keep it, or go back to Measurement and clear the interval. Your entered text is retained.");
    }

}
