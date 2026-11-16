using HardwareTest.OpenTap.Plugins.Instruments;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private DeviceDriverInfo? _newDeviceDriver;
    private DeviceDriverInfo? _editDeviceDriver;
    private string _deviceDriverSearch = string.Empty;
    private string _newDeviceCommissioning = string.Empty;
    private string _editDeviceCommissioning = string.Empty;
    private string _editDeviceChannels = string.Empty;
    private string _editDeviceOutputs = string.Empty;

    public IReadOnlyList<DeviceDriverInfo> DeviceDriverChoices => DeviceDriverCatalog.All;
    public IReadOnlyList<DeviceDriverInfo> FilteredDeviceDriverChoices => DeviceDriverChoices
        .Where(info => string.IsNullOrWhiteSpace(DeviceDriverSearch) || $"{info.Summary} {info.Id}".Contains(DeviceDriverSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
    public string DeviceDriverSearch
    {
        get => _deviceDriverSearch;
        set { if (SetField(ref _deviceDriverSearch, value ?? string.Empty)) OnPropertyChanged(nameof(FilteredDeviceDriverChoices)); }
    }
    public bool IsNewRegisteredDevice => NewInstrumentTypeId == DeviceDriverCatalog.RegisteredTypeId;
    public bool IsEditingRegisteredDevice => HardwareEditType?.TypeId == DeviceDriverCatalog.RegisteredTypeId;
    public DeviceDriverInfo? NewDeviceDriver
    {
        get => _newDeviceDriver;
        set
        {
            if (!SetField(ref _newDeviceDriver, value)) return;
            if (value is not null) NewInstrumentTypeId = DeviceDriverCatalog.RegisteredTypeId;
            NewDeviceCommissioning = string.Empty;
            OnPropertyChanged(nameof(NewDeviceGuidance));
            OnPropertyChanged(nameof(NewDeviceConfigurationExample));
            OnPropertyChanged(nameof(NewInstrumentAvailabilityText));
            OnPropertyChanged(nameof(CanAddInstrumentSlot));
        }
    }
    public DeviceDriverInfo? EditDeviceDriver
    {
        get => _editDeviceDriver;
        set
        {
            if (!SetField(ref _editDeviceDriver, value)) return;
            OnPropertyChanged(nameof(EditDeviceGuidance));
            OnPropertyChanged(nameof(EditDeviceConfigurationExample));
        }
    }
    public string NewDeviceCommissioning { get => _newDeviceCommissioning; set => SetField(ref _newDeviceCommissioning, value ?? string.Empty); }
    public string EditDeviceCommissioning { get => _editDeviceCommissioning; set => SetField(ref _editDeviceCommissioning, value ?? string.Empty); }
    public string EditDeviceChannels { get => _editDeviceChannels; set => SetField(ref _editDeviceChannels, value ?? string.Empty); }
    public string EditDeviceOutputs { get => _editDeviceOutputs; set => SetField(ref _editDeviceOutputs, value ?? string.Empty); }
    public string NewDeviceGuidance => DeviceGuidance(NewDeviceDriver);
    public string EditDeviceGuidance => DeviceGuidance(EditDeviceDriver);
    public string NewDeviceConfigurationExample => NewDeviceDriver?.ConfigurationExample ?? string.Empty;
    public string EditDeviceConfigurationExample => EditDeviceDriver?.ConfigurationExample ?? string.Empty;
    private static string DeviceGuidance(DeviceDriverInfo? info) => info is null ? "Choose a vendor and model. The core library has no bundled device drivers."
        : $"{info.Summary}. Package: {info.Package} {DeviceDriverCatalog.Version}. {info.Operations.Count} published operations. "
            + (info.Commissioning.Length == 0 ? "No specialized commissioning assertion. " : $"Before running, confirm: {info.Commissioning}. ")
            + "Software preview; physical bench qualification is pending. Selecting a model does not probe the device.";

    public string SelectedDeviceArgumentHelp
    {
        get
        {
            if (!MetricFunctionId.StartsWith("Device.", StringComparison.Ordinal)) return string.Empty;
            var instrument = Instruments.FirstOrDefault(item => item.SlotName == MetricInstrumentSlot);
            if (instrument?.TypeId != DeviceDriverCatalog.RegisteredTypeId) return "Choose a registered device slot.";
            try { return DeviceDriverCatalog.ArgumentHelp(AuthoringInstrumentCatalog.DeviceBindingFor(instrument), MetricFunctionId[7..]); }
            catch (Exception error) when (error is ArgumentException or FormatException or global::InstrumentComponents.Drivers.DriverException) { return error.Message; }
        }
    }

    private IReadOnlyDictionary<string, string> NewDeviceSettings() => IsNewRegisteredDevice
        ? new Dictionary<string, string> { ["DriverId"] = NewDeviceDriver?.Id ?? string.Empty, ["Commissioning"] = NewDeviceCommissioning.Trim() }
        : new Dictionary<string, string>();

    private void LoadDeviceEditorSettings(IReadOnlyDictionary<string, string> settings)
    {
        EditDeviceDriver = DeviceDriverChoices.FirstOrDefault(info => info.Id == settings.GetValueOrDefault("DriverId"));
        EditDeviceCommissioning = settings.GetValueOrDefault("Commissioning") ?? string.Empty;
        EditDeviceChannels = settings.GetValueOrDefault("Channels") ?? string.Empty;
        EditDeviceOutputs = settings.GetValueOrDefault("Outputs") ?? string.Empty;
    }
}
