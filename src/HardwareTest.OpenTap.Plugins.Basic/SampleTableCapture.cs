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
        RequireKnownExecutionBoundary(consumerStep);
        lock (_sync)
        {
            return _rows
                .Where(row => string.Equals(row.Channel, channel, StringComparison.OrdinalIgnoreCase)
                    && (producerStepId is null || row.Run.TestStepId == producerStepId)
                    && SameExecutionScope(row.Run, consumer))
                .Select(row => (row.Channel, row.Value, row.ElapsedMs))
                .ToArray();
        }
    }

    private static void RequireKnownExecutionBoundary(ITestStep consumer)
    {
        var parent = consumer.Parent;
        while (parent is ITestStep step)
        {
            if (step is not TestGroupStep and not RepeatLoopStep)
                throw new InvalidOperationException(
                    "SAMPLE_SCOPE: unsupported execution boundary; place the producer and check in a Basic Test Group or Repeat Loop.");
            parent = step.Parent;
        }
        if (parent is not TestPlan)
            throw new InvalidOperationException("SAMPLE_SCOPE: capture requires an attached TestPlan execution boundary.");
    }

    private static bool SameExecutionScope(TestStepRun producer, TestStepRun consumer)
    {
        // Require the same parent execution, not merely the same plan definition. This
        // also excludes old rows when a group containing the producer runs again.
        return producer.Parent == consumer.Parent;
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
