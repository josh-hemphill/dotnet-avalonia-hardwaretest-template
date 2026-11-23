using HardwareTest.OpenTap.Plugins.Instruments;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public IReadOnlyList<DeviceDriverInfo> DeviceDriverChoices => DeviceDriverCatalog.All;
    public IReadOnlyList<DeviceDriverInfo> FilteredDeviceDriverChoices => DeviceDriverChoices
        .Where(info => string.IsNullOrWhiteSpace(DeviceDriverSearch) || $"{info.Summary} {info.Id}".Contains(DeviceDriverSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
    public string DeviceDriverSearch
    {
        get;
        set { if (SetField(ref field, value ?? string.Empty)) OnPropertyChanged(nameof(FilteredDeviceDriverChoices)); }
    } = string.Empty;
    public bool IsNewRegisteredDevice => NewInstrumentTypeId == DeviceDriverCatalog.RegisteredTypeId;
    public bool IsEditingRegisteredDevice => HardwareEditType?.TypeId == DeviceDriverCatalog.RegisteredTypeId;
    public DeviceDriverInfo? NewDeviceDriver
    {
        get;
        set
        {
            if (!SetField(ref field, value)) return;
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
        get;
        set
        {
            if (!SetField(ref field, value)) return;
            OnPropertyChanged(nameof(EditDeviceGuidance));
            OnPropertyChanged(nameof(EditDeviceConfigurationExample));
        }
    }
    public string NewDeviceCommissioning { get; set => SetField(ref field, value ?? string.Empty); } = string.Empty;
    public string EditDeviceCommissioning { get; set => SetField(ref field, value ?? string.Empty); } = string.Empty;
    public string EditDeviceChannels { get; set => SetField(ref field, value ?? string.Empty); } = string.Empty;
    public string EditDeviceOutputs { get; set => SetField(ref field, value ?? string.Empty); } = string.Empty;
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
            catch (Exception error) when (error is ArgumentException or FormatException or InstrumentComponents.Drivers.DriverException) { return error.Message; }
        }
    }

    private IReadOnlyDictionary<string, string> NewDeviceSettings() => IsNewRegisteredDevice
        ? new Dictionary<string, string> { ["DriverId"] = NewDeviceDriver?.Id ?? string.Empty, ["Commissioning"] = NewDeviceCommissioning.Trim() }
        : [];

    private void LoadDeviceEditorSettings(IReadOnlyDictionary<string, string> settings)
    {
        EditDeviceDriver = DeviceDriverChoices.FirstOrDefault(info => info.Id == settings.GetValueOrDefault("DriverId"));
        EditDeviceCommissioning = settings.GetValueOrDefault("Commissioning") ?? string.Empty;
        EditDeviceChannels = settings.GetValueOrDefault("Channels") ?? string.Empty;
        EditDeviceOutputs = settings.GetValueOrDefault("Outputs") ?? string.Empty;
    }
}
