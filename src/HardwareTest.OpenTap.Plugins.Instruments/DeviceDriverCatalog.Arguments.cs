using System.Globalization;
using InstrumentComponents.Drivers.Catalog;
using InstrumentComponents.Scpi;

namespace HardwareTest.OpenTap.Plugins.Instruments;

public static partial class DeviceDriverCatalog
{
    private static ScpiProfile BoundProfile(DeviceBinding binding)
    {
        var profile = Profiles().Single(profile => profile.Id == binding.DriverId);
        var channels = ParseChannels(binding.Channels);
        var outputs = ParseOutputs(binding.Outputs);
        if (channels is null && outputs is null) return profile;
        var inventory = new InventoryBinding(channels ?? profile.ChannelIds, outputs ?? profile.OutputChannelIds);
        var shutdown = profile.OperationPlans.FirstOrDefault(plan => plan.Operation == CatalogOperation.Shutdown)?.Steps ?? [];
        return binding.DriverId.Split('.')[0] switch
        {
            "keysight" => global::InstrumentComponents.Drivers.Keysight.Drivers.BindProfile(profile, inventory, shutdown),
            "keithley" => global::InstrumentComponents.Drivers.Keithley.Drivers.BindProfile(profile, inventory, shutdown),
            "rigol" => global::InstrumentComponents.Drivers.Rigol.Drivers.BindProfile(profile, inventory, shutdown),
            "siglent" => global::InstrumentComponents.Drivers.Siglent.Drivers.BindProfile(profile, inventory, shutdown),
            _ => global::InstrumentComponents.Drivers.Tektronix.Drivers.BindProfile(profile, inventory, shutdown)
        };
    }

    private static ScpiProfile? GenericProfile(DeviceBinding binding)
        => Find(binding.DriverId).Commissioning.Length == 0 ? BoundProfile(binding) : null;

    public static void ValidateRequest(DeviceBinding binding, CatalogRequest request)
    {
        request.Validate();
        if (!AvailableOperations(binding).Contains(request.Operation.ToString(), StringComparer.Ordinal))
            throw new ArgumentException($"{Find(binding.DriverId).DisplayName} does not advertise {request.Operation} for this inventory.");
        if (GenericProfile(binding) is not { } profile) return;
        var plan = profile.OperationPlans.Single(plan => plan.Operation == request.Operation);
        var tokens = plan.Tokens;
        if (plan.Choices is { Count: > 0 })
        {
            var choice = plan.Choices.SingleOrDefault(choice => choice.Presence.All(pair =>
                request.Arguments.TryGetValue(pair.Key, out var argument) && (argument.Number is not null) == pair.Value));
            if (choice is null) throw new ArgumentException("This model does not support the selected combination of optional arguments. Review its range/resolution choices.");
            tokens = choice.Tokens;
        }
        foreach (var (name, argument) in request.Arguments)
        {
            if (argument.Kind == ArgumentKind.Channel && profile.ChannelIds?.Contains(argument.ChannelValue) != true)
                throw new ArgumentException($"{name}: choose a confirmed channel ({string.Join(", ", profile.ChannelIds ?? [])}).");
            if (tokens is null || !tokens.TryGetValue(name, out var choices)) continue;
            var value = argument.Kind == ArgumentKind.Boolean ? argument.Bool ? "true" : "false" : argument.Token;
            if (value is null || !choices.ContainsKey(value))
                throw new ArgumentException($"{name}: choose {string.Join(", ", choices.Keys)}.");
        }
    }

    public static string ArgumentTemplate(DeviceBinding binding, string operation)
    {
        var template = DeviceOperationRequests.Template(operation);
        if (GenericProfile(binding) is not { } profile) return template;
        var plan = profile.OperationPlans.FirstOrDefault(plan => plan.Operation.ToString() == operation);
        if (plan?.Tokens is null) return template;
        return string.Join("; ", template.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(pair =>
        {
            var key = pair.Split('=', 2)[0];
            return plan.Tokens.TryGetValue(key, out var tokens) && pair.EndsWith("=EDIT", StringComparison.Ordinal)
                ? key + "=" + tokens.Keys.First() : pair;
        }));
    }

    public static string ArgumentHelp(DeviceBinding binding, string operation)
    {
        var metadata = Enum.Parse<CatalogOperation>(operation).Describe();
        var profile = GenericProfile(binding);
        var plan = profile?.OperationPlans.FirstOrDefault(plan => plan.Operation.ToString() == operation);
        return string.Join("; ", metadata.Parameters.Select(parameter =>
        {
            var choices = plan?.Tokens?.GetValueOrDefault(parameter.Name)?.Keys;
            var detail = choices is not null ? string.Join(", ", choices) : parameter.Kind == ParameterKind.Channel && profile?.ChannelIds is { } channels
                ? string.Join(", ", channels.Select(channel => channel.ToString(CultureInfo.InvariantCulture))) : parameter.Constraint;
            return parameter.Name + (parameter.Kind == ParameterKind.OptionalNumber ? " (optional" : " (required")
                + (string.IsNullOrWhiteSpace(detail) ? ")" : ": " + detail + ")");
        }));
    }
}
