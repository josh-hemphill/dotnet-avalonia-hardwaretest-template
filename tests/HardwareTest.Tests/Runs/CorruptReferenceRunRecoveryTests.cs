using System.Text.Json;
using System.Text.Json.Nodes;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Runs;

public sealed class CorruptReferenceRunRecoveryTests
{
    [Theory]
    [InlineData("referenceRules", "null")]
    [InlineData("referenceUses", "null")]
    [InlineData("referenceEvaluations", "null")]
    [InlineData("referenceRules", "[null]")]
    [InlineData("referenceUses", "[null]")]
    [InlineData("referenceEvaluations", "[null]")]
    [InlineData("referenceUses", "[{\"rule\":null}]")]
    [InlineData("referenceUses", "[{\"referenceValue\":null}]")]
    [InlineData("referenceUses", "[{\"context\":null}]")]
    public async Task Malformed_reference_evidence_preserves_source_and_does_not_prevent_reading_another_run(string property, string value)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var valid = new TestRunRecord { RunId = "intact", PlanName = "Intact result", Result = RunResult.Passed };
        await store.SaveAsync(valid);
        var malformed = JsonSerializer.SerializeToNode(new TestRunRecord { RunId = "damaged" }, AppJsonContext.Default.TestRunRecord)!.AsObject();
        malformed[property] = JsonNode.Parse(value);
        var path = Path.Combine(store.GetRunDirectory("damaged"), "run.json");
        await File.WriteAllTextAsync(path, malformed.ToJsonString());
        var original = await File.ReadAllBytesAsync(path);

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync("damaged"));
        Assert.Contains("Preserve the original", failure.Message, StringComparison.Ordinal);
        Assert.Equal("intact", Assert.Single(await store.ListAsync()).RunId);
        Assert.Equal(RunResult.Passed, (await store.LoadAsync("intact"))!.Result);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }
}
