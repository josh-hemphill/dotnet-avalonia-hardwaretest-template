using System.Text.Json.Serialization;

namespace HardwareTest.Core.Plans;

public sealed class TestPlan
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DutSerial { get; set; }
    public string? Resource { get; set; }
    /// Role → registry instrument Id (plan-level defaults).
    public Dictionary<string, string> Instruments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PlanStep> Steps { get; set; } = [];
    /// When non-empty, overrides suite SafeShutdown for this plan.
    public List<PlanStep> SafeShutdown { get; set; } = [];
}

public enum SuiteExecutionMode
{
    Sequential = 0,
    Parallel = 1,
}

/// Suite that embeds full plans inline in one JSON document.
public sealed class TestSuite
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SuiteExecutionMode ExecutionMode { get; set; } = SuiteExecutionMode.Sequential;
    /// Role → registry instrument Id (inherited by plans unless overridden).
    public Dictionary<string, string> Instruments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<TestPlan> Plans { get; set; } = [];
    /// Default safe-state steps for all plans that do not override SafeShutdown.
    public List<PlanStep> SafeShutdown { get; set; } = [];
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OpenStep), "Open")]
[JsonDerivedType(typeof(WriteStep), "Write")]
[JsonDerivedType(typeof(QueryStep), "Query")]
[JsonDerivedType(typeof(AssertStep), "Assert")]
[JsonDerivedType(typeof(AcquireStep), "Acquire")]
[JsonDerivedType(typeof(DelayStep), "Delay")]
[JsonDerivedType(typeof(AnalyzeStep), "Analyze")]
public abstract class PlanStep
{
    public string? Id { get; set; }
}

public sealed class OpenStep : PlanStep
{
    public string Resource { get; set; } = string.Empty;
}

public sealed class WriteStep : PlanStep
{
    public string Command { get; set; } = string.Empty;
}

public sealed class QueryStep : PlanStep
{
    public string Command { get; set; } = string.Empty;
    public string? StoreAs { get; set; }
}

public sealed class AssertStep : PlanStep
{
    public string Source { get; set; } = string.Empty;
    public string Operator { get; set; } = "eq";
    public double Value { get; set; }
}

public sealed class AcquireStep : PlanStep
{
    public string Channel { get; set; } = "CH1";
    public int SampleCount { get; set; } = 32;
    public int IntervalMs { get; set; } = 10;
    public string? QueryCommand { get; set; }
}

public sealed class DelayStep : PlanStep
{
    public int Milliseconds { get; set; } = 100;
}

/// Invokes a registered IAnalyzeAlgorithm by id (C# plugins now; MATLAB host later).
public sealed class AnalyzeStep : PlanStep
{
    public string Algorithm { get; set; } = string.Empty;
    public string Channel { get; set; } = "VDC";
    public double Value { get; set; }
    public string? StoreAs { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
