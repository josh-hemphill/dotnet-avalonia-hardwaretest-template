using System.Text.Json;
using HardwareTest.Core.Serialization;

namespace HardwareTest.Core.Plans;

public interface IPlanLoader
{
    Task<TestPlan> LoadFromFileAsync(string path, CancellationToken cancellationToken = default);
    Task<TestPlan> LoadSampleAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<string> ListEmbeddedPlanNames();
}

public interface ISuiteLoader
{
    Task<TestSuite> LoadSuiteFromFileAsync(string path, CancellationToken cancellationToken = default);
    Task<TestSuite> LoadSampleSuiteAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<string> ListEmbeddedSuiteNames();
}

public sealed class PlanLoader : IPlanLoader, ISuiteLoader
{
    private const string SamplePlanSuffix = "sample-voltage-sweep.json";
    private const string SampleSuiteSuffix = "sample-suite.json";

    public async Task<TestPlan> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var plan = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.TestPlan, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to deserialize plan from '{path}'.");
        EnsureValidPlan(plan);
        return plan;
    }

    public async Task<TestPlan> LoadSampleAsync(CancellationToken cancellationToken = default)
    {
        var plan = await LoadEmbeddedAsync(SamplePlanSuffix, AppJsonContext.Default.TestPlan, cancellationToken);
        EnsureValidPlan(plan);
        return plan;
    }

    public IReadOnlyList<string> ListEmbeddedPlanNames()
        => ListEmbedded("Templates.Plans", excludeSuite: true);

    public async Task<TestSuite> LoadSuiteFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var suite = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.TestSuite, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to deserialize suite from '{path}'.");
        EnsureValidSuite(suite);
        return suite;
    }

    public async Task<TestSuite> LoadSampleSuiteAsync(CancellationToken cancellationToken = default)
    {
        var suite = await LoadEmbeddedAsync(SampleSuiteSuffix, AppJsonContext.Default.TestSuite, cancellationToken);
        EnsureValidSuite(suite);
        return suite;
    }

    public IReadOnlyList<string> ListEmbeddedSuiteNames()
        => ListEmbedded("Templates.Plans", suiteOnly: true);

    private static async Task<T> LoadEmbeddedAsync<T>(
        string suffix,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var assembly = typeof(PlanLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource ending with '{suffix}' was not found.");

        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Unable to open embedded resource '{resourceName}'.");
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to deserialize embedded resource '{resourceName}'.");
    }

    private static IReadOnlyList<string> ListEmbedded(string folderHint, bool excludeSuite = false, bool suiteOnly = false)
    {
        return typeof(PlanLoader).Assembly.GetManifestResourceNames()
            .Where(n => n.Contains(folderHint, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Cast<string>()
            .Where(n =>
            {
                var isSuite = n.Contains("suite", StringComparison.OrdinalIgnoreCase);
                if (suiteOnly)
                {
                    return isSuite;
                }

                return !excludeSuite || !isSuite;
            })
            .ToArray();
    }

    internal static void EnsureValidPlan(TestPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Name))
        {
            throw new InvalidOperationException("Test plan must have a Name.");
        }

        if (plan.Steps.Count == 0)
        {
            throw new InvalidOperationException("Test plan must contain at least one step.");
        }
    }

    internal static void EnsureValidSuite(TestSuite suite)
    {
        if (string.IsNullOrWhiteSpace(suite.Name))
        {
            throw new InvalidOperationException("Test suite must have a Name.");
        }

        if (suite.Plans.Count == 0)
        {
            throw new InvalidOperationException("Test suite must contain at least one plan.");
        }

        foreach (var plan in suite.Plans)
        {
            EnsureValidPlan(plan);
        }
    }
}
