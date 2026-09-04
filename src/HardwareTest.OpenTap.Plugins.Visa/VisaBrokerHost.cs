using HardwareTest.Core.Hardware;
using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// Host-owned IVisaBroker slot, assigned before PluginManager.Search.
/// Lives in assembly HardwareTest.OpenTap.Plugins.Visa so the Basic authoring pack
/// does not reference Core. CLR namespace stays Plugins.Basic for TapPlan XML.
/// OpenTAP session-local (not a second process-wide service locator).
public static class VisaBrokerHost
{
    private static readonly SessionLocal<IVisaBroker?> Binding = new(null, autoDispose: false);

    public static void Register(IVisaBroker broker)
        => Binding.Value = broker ?? throw new ArgumentNullException(nameof(broker));

    public static IVisaBroker Require()
        => Binding.Value ?? throw new InvalidOperationException(
            "Host did not register IVisaBroker before PluginManager.Search.");
}
