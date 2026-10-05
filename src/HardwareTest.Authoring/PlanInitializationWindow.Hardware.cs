using Avalonia.Controls;

namespace HardwareTest.Authoring;

public sealed partial class PlanInitializationWindow
{
    private sealed record HardwareChoice(string Label, AuthoringInstrumentAdapter? Adapter = null, InstrumentRef? Resource = null)
    {
        public override string ToString() => Label;
    }
    private readonly List<HardwareChoice> _hardwareChoices = [];
    private HardwareChoice? SelectedHardware => _hardware.SelectedItem as HardwareChoice;

    private void ShowHardwareChoices()
    {
        _hardwareChoices.Clear();
        _hardwareChoices.Add(new("No hardware yet"));
        foreach (var adapter in _vm.InstrumentTypeChoices.Where(a => AuthoringInstrumentCatalog.IsLibrary(a.TypeId)))
            _hardwareChoices.Add(new($"Create {adapter.DisplayName} — Instrument Components", adapter));
        var demo = AuthoringInstrumentCatalog.All.Single(a => a.DisplayName == "Mock DMM");
        _hardwareChoices.Add(new("Create Mock DMM — demo", demo));
        _hardwareChoices.Add(new("Create VISA DMM — legacy binding", AuthoringInstrumentCatalog.All.Single(a => a.DisplayName == "VISA DMM")));
        for (var index = 0; index < _reusable.Count; index++)
        {
            var resource = _reusable[index];
            AuthoringInstrumentCatalog.TryGet(resource.TypeId, out var adapter);
            _hardwareChoices.Add(new($"{(index == _retainedResourceIndex ? "Retained original" : "Reuse")} {resource.SlotName} — {adapter?.DisplayName ?? resource.TypeId} ({resource.VisaAddress})"
                + (index == _retainedResourceIndex ? " — changed or removed from catalog" : " — " + _resourceOrigins[index])
                + " — settings: " + (resource.Settings.Count == 0 ? "defaults" : string.Join(", ", resource.Settings.Select(p => p.Key + "=" + p.Value))), adapter, resource));
        }
        _hardware.ItemsSource = _hardwareChoices.ToArray();
        _hardware.SelectedIndex = 0;
    }

    private void SelectHardwareType(string id)
    {
        var choice = _hardwareChoices.FirstOrDefault(c => c.Resource is null && c.Adapter?.TypeId == id);
        if (choice is null && AuthoringInstrumentCatalog.TryGet(id, out var adapter))
        {
            choice = new($"Retained {adapter.DisplayName} — unavailable in selected home", adapter);
            _hardwareChoices.Add(choice); _hardware.ItemsSource = _hardwareChoices.ToArray();
        }
        _hardware.SelectedItem = choice ?? _hardwareChoices[0];
    }

    private IReadOnlyList<InstrumentRef> SelectedResources()
    {
        if (SelectedHardware?.Resource is { } resource) return [CopyResource(resource) with { SlotName = _slot.Text ?? "" }];
        if (SelectedHardware?.Adapter is not { } adapter) return [];
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (adapter.ConfigurationFields.Contains("IoTimeoutMilliseconds") && !string.IsNullOrEmpty(_timeout.Text)) settings["IoTimeoutMilliseconds"] = _timeout.Text;
        return [new InstrumentRef(_slot.Text ?? "", adapter.TypeId, _address.Text ?? "") { Settings = settings }];
    }

    private void ShowHardware()
    {
        var choice = SelectedHardware;
        var library = choice?.Adapter is { } selectedAdapter && AuthoringInstrumentCatalog.IsLibrary(selectedAdapter.TypeId);
        if (library && choice?.Resource is null && _automaticDemoAddress) { _address.Text = ""; _automaticDemoAddress = false; }
        _address.PlaceholderText = library ? "For example TCPIP0::192.0.2.1::inst0::INSTR" : "Instrument resource address";
        var resources = SelectedResources();
        if (choice?.Resource is { } reused) { _address.Text = reused.VisaAddress; _timeout.Text = reused.Settings.GetValueOrDefault("IoTimeoutMilliseconds") ?? "5000"; }
        if (_guided) Title = library ? "First test plan — library lifecycle" : "First voltage test";
        _measurement.Content = new TextBlock { Text = library ? "Voltage measurement unavailable for this device — turn off to save a hardware scaffold" : "Include first measurement", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _criterion.Content = new TextBlock { Text = library ? "Average-voltage criterion unavailable for this device" : _guided ? "Pass when average voltage reaches the threshold" : "Mean greater than or equal criterion", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _address.IsReadOnly = choice?.Resource is not null;
        _timeout.IsReadOnly = choice?.Resource is not null;
        _address.IsEnabled = choice?.Adapter is not null;
        _timeout.IsEnabled = choice?.Adapter?.ConfigurationFields.Contains("IoTimeoutMilliseconds") == true;
        if (resources.Any(resource => !AuthoringInstrumentCatalog.TryGet(resource.TypeId, out _)))
        {
            _readiness.Text = string.Join("\n", resources.Select(resource => $"{resource.SlotName} · {resource.TypeId}\nThis retained resource type is unavailable. Its original address, configuration and source are preserved; review its package in Environment before creating supported actions."));
            _coverage.Text = "Cleanup capability is unknown for this retained resource; no supported action can be claimed.";
            return;
        }
        try
        {
            var result = _vm.ReviewPlanInitialization(Request());
            _readiness.Text = resources.Count == 0
                ? _vm.LibraryEnvironmentReadinessText + "\nChoose an installed library device, reuse a binding, or save without hardware. If Instrument Components choices are missing, open Environment, declare the library dependency, then prepare/import its package. Your inputs are retained."
                : string.Join("\n", resources.Select(resource =>
                {
                    if (!AuthoringInstrumentCatalog.TryGet(resource.TypeId, out var adapter))
                        return $"{resource.SlotName} · {resource.TypeId}\nThis retained resource type is unavailable. Its original address, configuration and source are preserved; review its package in Environment before creating supported actions.";
                    var declared = _workspace.Manifest.Dependencies.Any(d => d.Package.Equals(adapter.RequiredPackage, StringComparison.OrdinalIgnoreCase));
                    return $"{adapter.DisplayName} · {adapter.Description}\n{adapter.RequiredPackage} · {(declared ? "dependency declared" : "dependency missing — declare in Environment")}\n"
                        + (AuthoringInstrumentCatalog.IsLibrary(resource.TypeId) ? (_vm.InstrumentTypeChoices.Any(a => a.TypeId == resource.TypeId) ? "Ready in selected home — compatible installed package reused.\n" : "Unavailable in selected home — open Environment.\n") + "Library identity and cleanup steps can be generated; review output-off/reset for your device. Voltage and average-voltage measurements are unavailable for this device in this form. Turn off the first measurement to save a hardware scaffold. Library measurement recipes are not supported by this authoring form." : "Demo or legacy voltage tasks.");
                }));
            _readiness.Text += "\n" + string.Join("\n", result.Issues.Where(i => i.Code.StartsWith("INSTRUMENT_", StringComparison.Ordinal) || i.Code == "INVALID_OPENTAP_HOME").Select(i => i.Message));
            if (choice?.Resource is { } retained && _retainedResourceIndex >= 0 && SameResource(retained, _reusable[_retainedResourceIndex]))
                _readiness.Text += "\nPreviously selected reusable instrument changed or was removed. Its original address and configuration are retained; review this retained choice or explicitly choose another instrument.";
            _coverage.Text = "Generated cleanup slots: " + (_shutdown.IsChecked == true ? string.Join(", ", result.Draft.Cleanup.InstrumentSlots) : "disabled")
                + ". These actions run on the bench after measurements; review model-specific safety. Package preparation belongs in Environment.";
        }
        catch (Exception error) { _readiness.Text = error.Message; }
    }
}
