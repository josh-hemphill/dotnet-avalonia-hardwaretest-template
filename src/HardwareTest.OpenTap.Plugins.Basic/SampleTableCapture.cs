using System.Runtime.CompilerServices;
using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// Captures only the latest execution of each producer within the current plan/iteration.
internal sealed class SampleTableCapture : IResultSink
{
    private readonly object _sync = new();
    // Trusted Basic repeat boundaries clear descendant captures even when a producer
    // is skipped. Unknown composite execution boundaries are rejected during retrieval.
    private static readonly ConditionalWeakTable<TestPlanRun, List<SampleTableCapture>> Captures = new();
    private readonly List<(TestStepRun Run, string Channel, double Value, double ElapsedMs)> _rows = [];
    private readonly Dictionary<Guid, (Guid TestStepId, Guid Parent)> _runs = [];

    // RepeatLoop drains queued results before clearing its descendants. Clearing even when a
    // producer is skipped prevents a later iteration from consuming that producer's old rows.
    internal static void BeginIteration(TestPlanRun planRun, ITestStepParent loop)
    {
        if (!Captures.TryGetValue(planRun, out var captures))
        {
            return;
        }

        var ids = DescendantIds(loop).ToHashSet();
        lock (captures)
        {
            foreach (var capture in captures)
            {
                lock (capture._sync)
                {
                    capture._rows.RemoveAll(row => ids.Contains(row.Run.TestStepId));
                    // Results have drained and descendant rows are gone. Their old
                    // execution ancestry is no longer needed by any live sample.
                    foreach (var runId in capture._runs.Where(pair => ids.Contains(pair.Value.TestStepId))
                                 .Select(pair => pair.Key).ToArray())
                        capture._runs.Remove(runId);
                }
            }
        }
    }

    private static IEnumerable<Guid> DescendantIds(ITestStepParent parent)
    {
        foreach (var child in parent.ChildTestSteps)
        {
            yield return child.Id;
            foreach (var id in DescendantIds(child))
            {
                yield return id;
            }
        }
    }

    public void OnTestPlanRunStart(TestPlanRun planRun)
    {
        lock (_sync)
        {
            _rows.Clear();
            _runs.Clear();
        }

        var captures = Captures.GetOrCreateValue(planRun);
        lock (captures)
        {
            if (!captures.Contains(this))
            {
                captures.Add(this);
            }
        }
    }

    public void OnTestPlanRunCompleted(TestPlanRun planRun)
    {
    }

    public void OnTestStepRunStart(TestStepRun stepRun)
    {
        lock (_sync)
        {
            _rows.RemoveAll(row => row.Run.TestStepId == stepRun.TestStepId);
            _runs[stepRun.Id] = (stepRun.TestStepId, stepRun.Parent);
        }
    }

    public void OnTestStepRunCompleted(TestStepRun stepRun)
    {
    }

    public void OnResultPublished(TestStepRun run, ResultTable result)
    {
        if (result is null || !string.Equals(result.Name, "Sample", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var channelCol = result.Columns.FirstOrDefault(c => c.Name == "Channel");
        var valueCol = result.Columns.FirstOrDefault(c => c.Name == "Value");
        var elapsedCol = result.Columns.FirstOrDefault(c => c.Name == "ElapsedMs");
        if (valueCol is null)
        {
            return;
        }

        lock (_sync)
        {
            for (var i = 0; i < valueCol.Data.Length; i++)
            {
                var value = Convert.ToDouble(valueCol.Data.GetValue(i));
                var channel = channelCol is null
                    ? string.Empty
                    : Convert.ToString(channelCol.Data.GetValue(i)) ?? string.Empty;
                var elapsed = elapsedCol is null
                    ? double.NaN
                    : ToElapsed(elapsedCol.Data.GetValue(i));
                _rows.Add((run, channel, value, elapsed));
            }
        }
    }

    public IReadOnlyList<(string Channel, double Value, double ElapsedMs)> RowsFor(string channel, TestStepRun consumer, ITestStep consumerStep, Guid? producerStepId = null)
    {
        var plan = RequireKnownExecutionBoundary(consumerStep);
        var sectionIds = SectionIds(plan).ToHashSet();
        lock (_sync)
        {
            return _rows
                .Where(row => string.Equals(row.Channel, channel, StringComparison.OrdinalIgnoreCase)
                    && (producerStepId is null || row.Run.TestStepId == producerStepId)
                    && SameExecutionScope(row.Run, consumer, sectionIds))
                .Select(row => (row.Channel, row.Value, row.ElapsedMs))
                .ToArray();
        }
    }

    private static TestPlan RequireKnownExecutionBoundary(ITestStep consumer)
    {
        var parent = consumer.Parent;
        while (parent is ITestStep step)
        {
            if (step is not TestGroupStep and not RepeatLoopStep)
                throw new InvalidOperationException(
                    "SAMPLE_SCOPE: unsupported execution boundary; place the producer and check in a Basic Test Group or Repeat Loop.");
            parent = step.Parent;
        }
        if (parent is not TestPlan plan)
            throw new InvalidOperationException("SAMPLE_SCOPE: capture requires an attached TestPlan execution boundary.");
        return plan;
    }

    private static IEnumerable<Guid> SectionIds(ITestStepParent parent)
    {
        foreach (var child in parent.ChildTestSteps)
        {
            if (child is TestGroupStep { AuthoringSection: true }) yield return child.Id;
            foreach (var id in SectionIds(child)) yield return id;
        }
    }

    private bool SameExecutionScope(TestStepRun producer, TestStepRun consumer, HashSet<Guid> sectionIds)
        => ExecutionScope(producer, sectionIds) == ExecutionScope(consumer, sectionIds);

    private Guid ExecutionScope(TestStepRun run, HashSet<Guid> sectionIds)
    {
        // Only named authoring sections are transparent. Keep the actual enclosing
        // execution identity, so repeats and imported groups remain isolated.
        var parent = run.Parent;
        while (_runs.TryGetValue(parent, out var group) && sectionIds.Contains(group.TestStepId))
            parent = group.Parent;
        return parent;
    }

    private static double ToElapsed(object? raw)
    {
        if (raw is null)
        {
            return double.NaN;
        }

        var value = Convert.ToDouble(raw);
        return double.IsNaN(value) ? double.NaN : value;
    }
}
