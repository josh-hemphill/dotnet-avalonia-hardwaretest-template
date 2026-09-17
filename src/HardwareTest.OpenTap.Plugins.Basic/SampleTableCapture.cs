using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// Captures Sample result tables for ApplyTransferFunctionStep (same-plan sibling rows).
internal sealed class SampleTableCapture : IResultSink
{
    private readonly object _sync = new();
    private readonly List<(string Channel, double Value, double ElapsedMs)> _rows = [];

    public void OnTestPlanRunStart(TestPlanRun planRun)
    {
        lock (_sync)
        {
            _rows.Clear();
        }
    }

    public void OnTestPlanRunCompleted(TestPlanRun planRun)
    {
    }

    public void OnTestStepRunStart(TestStepRun stepRun)
    {
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
                _rows.Add((channel, value, elapsed));
            }
        }
    }

    public IReadOnlyList<(string Channel, double Value, double ElapsedMs)> RowsFor(string channel)
    {
        lock (_sync)
        {
            return _rows
                .Where(row => string.Equals(row.Channel, channel, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
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
