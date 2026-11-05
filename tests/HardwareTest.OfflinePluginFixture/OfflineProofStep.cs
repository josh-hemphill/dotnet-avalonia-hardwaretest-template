using HardwareTest.OfflineDependencyFixture;
using OpenTap;

namespace HardwareTest.OfflinePluginFixture;

[Display("Offline delivery proof", "Exercises a separately shipped transitive package without hardware.")]
public sealed class OfflineProofStep : TestStep
{
    public string ProofPath { get; set; } = string.Empty;

    public override void Run()
    {
        var value = OfflineValue.Read();
        Results.Publish("Offline voltage", ["Value"], value);
        if (string.IsNullOrWhiteSpace(ProofPath)) throw new InvalidOperationException("The test requires an owned proof path.");
        File.WriteAllLines(ProofPath, [typeof(OfflineProofStep).Assembly.Location, OfflineValue.LoadedFrom,
            value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture),
            typeof(TestPlan).Assembly.Location,
            $"{typeof(TestPlan).Assembly.GetName().Version}/{typeof(TestPlan).Assembly.ManifestModule.ModuleVersionId:D}"]);
        UpgradeVerdict(value == 1.25 ? Verdict.Pass : Verdict.Fail);
    }
}
