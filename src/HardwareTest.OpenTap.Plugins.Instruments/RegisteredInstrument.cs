using System.ComponentModel;
using System.Xml.Serialization;
using InstrumentComponents.Drivers;
using InstrumentComponents.Drivers.Catalog;
using InstrumentComponents.Runtime;
using InstrumentComponents.Scpi;
using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Instruments;

/// Session-local host connector. Metadata and constructors do not initialize transport.
public static class RegisteredDevices
{
    private static readonly SessionLocal<Func<TimeSpan, IScpiConnector>?> Binding = new(null, autoDispose: false);
    public static Func<TimeSpan, IScpiConnector>? Connector { get => Binding.Value; set => Binding.Value = value; }
    private static readonly SessionLocal<Action<string, Exception>?> Failure = new(null, autoDispose: false);
    public static Action<string, Exception>? OperationFailure { get => Failure.Value; set => Failure.Value = value; }
    private static readonly SessionLocal<Action?> Gate = new(null, autoDispose: false);
    /// The owning run gates new operations; closing a resource must never wait for Resume.
    public static Action? WaitForExecution { get => Gate.Value; set => Gate.Value = value; }

    /// Embedded OpenTAP resources deserialize per property. A named slot represents one
    /// physical device, so bind its capability steps to one instance before execution.
    public static void SharePlanResources(ITestStepParent plan)
    {
        var slots = new Dictionary<string, RegisteredInstrument>(StringComparer.OrdinalIgnoreCase);
        Visit(plan);
        void Visit(ITestStepParent parent)
        {
            foreach (var step in parent.ChildTestSteps)
            {
                if (step is DeviceOperationStep { Instrument: { } resource } operation)
                {
                    if (slots.TryGetValue(resource.Name, out var existing))
                    {
                        if (existing.Binding != resource.Binding)
                            throw new InvalidOperationException($"Instrument slot '{resource.Name}' has conflicting device bindings. Repair its configuration in Authoring before running.");
                        operation.Instrument = existing;
                    }
                    else
                    {
                        slots.Add(resource.Name, resource);
                    }
                }
                Visit(step);
            }
        }
    }
}

[Display("Registered device", Groups: ["HardwareTest", "Instrument Components"], Description: "One physical device with explicitly selected vendor driver and shared capability views.")]
public sealed class RegisteredInstrument : Instrument
{
    private DeviceHandle? _handle;
    [VisaAddress, Display("Resource address", Group: "Connection", Order: 1)]
    public string VisaAddress { get; set; } = string.Empty;
    [Display("Device model / driver", Group: "Connection", Order: 2)]
    public string DriverId { get; set; } = string.Empty;
    [Display("I/O timeout (ms)", Group: "Connection", Order: 3)]
    public int IoTimeoutMilliseconds { get; set; } = 5000;
    [Display("Confirmed channels", Group: "Inventory", Description: "Optional comma-separated positive channels. Empty uses the published profile inventory.")]
    public string Channels { get; set; } = string.Empty;
    [Display("Confirmed output inventory", Group: "Inventory", Description: "Empty retains unknown inventory. Use none, fgen or psu:1,psu:2 only after confirming all outputs.")]
    public string Outputs { get; set; } = string.Empty;
    [Display("Commissioning assertion", Group: "Inventory", Description: "Required only by specialized factories. A saved assertion is not physical qualification.")]
    public string Commissioning { get; set; } = string.Empty;
    [Browsable(false), XmlIgnore]
    public DeviceBinding Binding => new(DriverId, VisaAddress, Channels, Outputs, Commissioning, IoTimeoutMilliseconds);
    [Browsable(false), XmlIgnore]
    public IReadOnlyList<string> AvailableOperations => DeviceDriverCatalog.AvailableOperations(Binding);

    public override void Open()
    {
        if (IsConnected) return;
        var connector = RegisteredDevices.Connector ?? throw new InvalidOperationException("No HardwareTest device connector is registered. Prepare the application environment before running.");
        var binding = Binding;
        _ = DeviceDriverCatalog.AvailableOperations(binding); // Validate before transport allocation.
        var handle = DeviceDriverCatalog.Open(binding, connector(TimeSpan.FromMilliseconds(IoTimeoutMilliseconds)));
        try { _handle = handle; base.Open(); }
        catch { _handle = null; handle.Close(); throw; }
    }

    public CatalogReply Invoke(CatalogRequest request)
    {
        try { return (_handle ?? throw new InvalidOperationException("Device is not open.")).Invoke(request); }
        catch (Exception error) { RegisteredDevices.OperationFailure?.Invoke($"{Name} [{VisaAddress}] / {request.Operation}", error); throw; }
    }

    public override void Close()
    {
        var handle = _handle;
        _handle = null;
        try { handle?.Close(); }
        finally { if (IsConnected) base.Close(); }
    }
}
