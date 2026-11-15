using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using InstrumentComponents.Drivers;
using InstrumentComponents.Drivers.Keysight;
using InstrumentComponents.Scpi;

namespace HardwareTest.OpenTap.Plugins.Instruments;

public static partial class DeviceDriverCatalog
{
    private static IEnumerable<DeviceDriverInfo> SpecializedCatalog()
    {
        var connector = new PassiveConnector();
        var endpoint = new Endpoint("hpib", "catalog-only");
        foreach (var model in Enum.GetValues<Hp5334Model>())
            yield return Info(new Hp5334Factory(model, Hp5334Commissioning.NativeInternalGatingConfirmed, connector).Descriptor)
                with
            { Commissioning = nameof(Hp5334Commissioning.NativeInternalGatingConfirmed), EndpointScheme = "hpib" };
        yield return Info(new Hp3585Factory([new(endpoint, Hp3585Commissioning.QualifiedNativeScalarConfirmed)], connector).Descriptor)
            with
        { Commissioning = nameof(Hp3585Commissioning.QualifiedNativeScalarConfirmed), EndpointScheme = "hpib" };
        var acquisition = new Hp3497Configuration(true, true, [new(1, 0, 0, Hp3497Module.Option010, Hp3497Compensation.None)]);
        yield return Info(new Hp3497Factory([new(endpoint, acquisition)], connector).Descriptor)
            with
        { Commissioning = "Confirmed Option001 and normal DVM routing; explicit module input inventory (JSON)", EndpointScheme = "hpib", ConfigurationExample = "{\"option001\":false,\"normalDvmRoutingConfirmed\":false,\"inputs\":[{\"channel\":1,\"slot\":0,\"local\":0,\"module\":\"Unknown\",\"compensation\":\"Unknown\"}]}" };
        var mainframe = new Hp34980Configuration(Hp34980Commissioning.NoAutonomousActionsConfirmed,
            [new(1, 1, 1, Hp34980Module.Hp34937A)], [new(1, 2, 101, Hp34980Direction.Input), new(2, 2, 102, Hp34980Direction.Output)]);
        yield return Info(new Hp34980Factory([new(CatalogEndpoint, mainframe)], connector).Descriptor)
            with
        { Commissioning = "No autonomous actions; explicit 34937A/34922A switch and 34950A digital port inventory (JSON)", ConfigurationExample = "{\"commissioning\":\"Unknown\",\"switches\":[{\"channel\":1,\"slot\":1,\"local\":1,\"module\":\"Unknown\"}],\"ports\":[]}" };
        foreach (var model in Enum.GetValues<HpRadioModel>())
        {
            var firmware = model == HpRadioModel.Hp8920B ? "B.06.00" : model == HpRadioModel.Hp8920A ? "A.18.00" : "A.15.00";
            var config = new HpRadioConfiguration(model, firmware, HpRadioAttenuator.Electronic, HpRadioInputPower.Standard,
                HpRadioSourcePort.RfInOut, HpRadioReceiverInput.RfInOut, HpRadioCommissioning.Confirmed);
            yield return Info(new HpRadioFactory(model, [new(endpoint, config)], connector).Descriptor)
                with
            { Commissioning = "Confirm native GPIB, initial Manual RX/TX, independent frequency display, inhibited external PTT, exclusive access and qualified carrier; exact firmware and RF inventory (JSON)", EndpointScheme = "hpib", ConfigurationExample = "{\"model\":\"" + model + "\",\"firmware\":\"ENTER ACTUAL FIRMWARE\",\"attenuator\":\"Unknown\",\"inputPower\":\"Unknown\",\"sourcePort\":\"Unknown\",\"receiverInput\":\"Unknown\",\"commissioning\":\"Unknown\"}" };
        }
    }

    private static IDriverFactory? SpecializedFactory(DeviceBinding binding, IScpiConnector connector)
    {
        var endpoint = new Endpoint(Find(binding.DriverId).EndpointScheme, binding.Address);
        switch (binding.DriverId)
        {
            case "keysight.hp5334a":
            case "keysight.hp5334b":
                if (binding.Commissioning != nameof(Hp5334Commissioning.NativeInternalGatingConfirmed))
                    throw new ArgumentException("Confirm native language, internal gating and exclusive REMOTE commissioning: NativeInternalGatingConfirmed.");
                return new Hp5334Factory(binding.DriverId.EndsWith('a') ? Hp5334Model.Hp5334A : Hp5334Model.Hp5334B, Hp5334Commissioning.NativeInternalGatingConfirmed, connector);
            case "keysight.hp3585b":
                if (binding.Commissioning != nameof(Hp3585Commissioning.QualifiedNativeScalarConfirmed))
                    throw new ArgumentException("Confirm native scalar commissioning: QualifiedNativeScalarConfirmed.");
                return new Hp3585Factory([new(endpoint, Hp3585Commissioning.QualifiedNativeScalarConfirmed)], connector);
            case "keysight.hp3497a":
                return new Hp3497Factory([new(endpoint, Configuration(binding.Commissioning, DeviceConfigurationJsonContext.Default.Hp3497Configuration))], connector);
            case "keysight.34980a":
                return new Hp34980Factory([new(endpoint, Configuration(binding.Commissioning, DeviceConfigurationJsonContext.Default.Hp34980Configuration))], connector);
            case "keysight.hp8920a":
            case "keysight.hp8920b":
            case "keysight.hp8921a":
                var config = Configuration(binding.Commissioning, DeviceConfigurationJsonContext.Default.HpRadioConfiguration);
                var model = binding.DriverId switch { "keysight.hp8920a" => HpRadioModel.Hp8920A, "keysight.hp8920b" => HpRadioModel.Hp8920B, _ => HpRadioModel.Hp8921A };
                return new HpRadioFactory(model, [new(endpoint, config)], connector);
            default: return null;
        }
    }

    private static T Configuration<T>(string text, JsonTypeInfo<T> type)
    {
        try { return JsonSerializer.Deserialize(text, type) ?? throw new ArgumentException("Enter the complete confirmed device inventory JSON."); }
        catch (JsonException error) { throw new ArgumentException("Invalid device inventory JSON: " + error.Message, error); }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(Hp3497Configuration))]
[JsonSerializable(typeof(Hp34980Configuration))]
[JsonSerializable(typeof(HpRadioConfiguration))]
internal partial class DeviceConfigurationJsonContext : JsonSerializerContext;
