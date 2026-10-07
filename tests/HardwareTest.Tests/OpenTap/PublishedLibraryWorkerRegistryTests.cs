using System.Text.Json;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Host.Worker;
using HardwareTest.Tests.Fixtures;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class PublishedLibraryWorkerRegistryTests
{
    [Fact]
    public async Task Production_worker_opens_genuine_library_registry_and_execution_host_preserves_generated_persistence()
    {
        Assert.True(JsonSerializer.IsReflectionEnabledByDefault);
        using var temporary = new TempDataDirectory();
        var library = PublishedLibraryMetadataFixture.Assembly;
        var instrument = (Instrument)Activator.CreateInstance(library.GetType("InstrumentComponents.OpenTap.DmmInstrument", throwOnError: true)!)!;
        Assert.True(InstrumentResourceAccess.TrySetResource(instrument, "MOCK::INSTR0"));
        var identity = (TestStep)Activator.CreateInstance(library.GetType("InstrumentComponents.OpenTap.IdentityQueryStep", throwOnError: true)!)!;
        identity.GetType().GetProperty("Instrument")!.SetValue(identity, instrument);
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(identity);
        var path = Path.Combine(temporary.Path, "published-registry-identity.TapPlan");
        plan.Save(path);
        Assert.Contains("InstrumentComponents.OpenTap.IdentityQueryStep", File.ReadAllText(path), StringComparison.Ordinal);

        using var worker = new OpenTapWorkerClient(new AppSettings
        {
            UseMockVisa = true,
            CrashEnabled = false,
            DataDirectory = temporary.Path,
        });
        await worker.LoadPlanAsync(path);
        var summary = await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(summary.Result == RunResult.Passed, $"{summary.Result}: {summary.ErrorMessage}");
        Assert.True(JsonSerializer.IsReflectionEnabledByDefault);

        // This host also executes genuine instruments directly. Reflection JSON
        // supports their embedded registry; application persistence stays explicit.
        var settings = new AppSettings
        {
            DataDirectory = temporary.Path,
            UseMockVisa = true,
            PlanSlotOverrides = [new() { PlanId = "fixture", SlotName = instrument.Name, Resource = "MOCK::generated-roundtrip" }],
        };
        var settingsJson = JsonSerializer.Serialize(settings, AppJsonContext.Default.AppSettings);
        var loadedSettings = JsonSerializer.Deserialize(settingsJson, AppJsonContext.Default.AppSettings);
        Assert.NotNull(loadedSettings);
        Assert.Equal(settings.DataDirectory, loadedSettings.DataDirectory);
        var binding = Assert.Single(loadedSettings.PlanSlotOverrides);
        Assert.Equal(instrument.Name, binding.SlotName);
        Assert.Equal("MOCK::generated-roundtrip", binding.Resource);

        var runtimePath = Path.Combine(Path.GetDirectoryName(OpenTapWorkerProcess.ResolveExecutablePath())!, "HardwareTest.OpenTap.Worker.runtimeconfig.json");
        using var runtime = JsonDocument.Parse(File.ReadAllText(runtimePath));
        Assert.True(runtime.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties")
            .GetProperty("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault").GetBoolean());
    }

}
