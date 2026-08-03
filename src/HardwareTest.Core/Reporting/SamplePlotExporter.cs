using HardwareTest.Core.Runs;
using ScottPlot;

namespace HardwareTest.Core.Reporting;

/// Builds report PNGs from stored samples using ScottPlot (Avalonia-free, CI-safe).
public static class SamplePlotExporter
{
    private const int PlotWidth = 900;
    private const int PlotHeight = 360;

    /// Writes a channel line chart under the run plots folder. Returns path or null.
    public static string? ExportChannelPng(TestRunRecord run, string channel, string outputDirectory)
    {
        var samples = run.Samples
            .Where(s => string.Equals(s.EffectiveMetricKey, channel, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.Channel, channel, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Timestamp)
            .ToArray();
        if (samples.Length == 0)
        {
            return null;
        }

        Directory.CreateDirectory(outputDirectory);
        var safePlan = Sanitize(string.IsNullOrWhiteSpace(run.PlanId) ? run.PlanName : run.PlanId);
        var key = samples[0].EffectiveMetricKey;
        var path = Path.Combine(outputDirectory, $"{safePlan}-{Sanitize(key)}.png");
        WriteChannelPlotPng(samples, key, path);
        return path;
    }

    public static IReadOnlyList<string> ExportAllChannels(TestRunRecord run, string outputDirectory)
    {
        var channels = run.Samples
            .Select(s => s.EffectiveMetricKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var paths = new List<string>();
        foreach (var channel in channels)
        {
            var path = ExportChannelPng(run, channel, outputDirectory);
            if (path is not null)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static void WriteChannelPlotPng(StoredSample[] samples, string channel, string path)
    {
        var plot = new Plot();
        plot.YLabel(channel);

        if (samples.Any(s => s.IterationIndex is > 0))
        {
            WriteIterationPlot(plot, samples, channel);
        }
        else
        {
            WriteTimeOrSignalPlot(plot, samples, channel);
        }

        plot.Axes.AutoScale();
        plot.ShowLegend();
        plot.SavePng(path, PlotWidth, PlotHeight);
    }

    /// Last value per iteration on X = iteration index.
    private static void WriteIterationPlot(Plot plot, StoredSample[] samples, string channel)
    {
        var lastPerIter = samples
            .Where(s => s.IterationIndex is > 0)
            .GroupBy(s => s.IterationIndex!.Value)
            .OrderBy(g => g.Key)
            .Select(g => (Iteration: (double)g.Key, Value: g.Last().Value))
            .ToArray();

        var xs = lastPerIter.Select(p => p.Iteration).ToArray();
        var ys = lastPerIter.Select(p => p.Value).ToArray();
        var loopHint = samples.Select(s => s.LoopPath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        plot.Title(string.IsNullOrWhiteSpace(loopHint) ? $"{channel} (by iteration)" : $"{channel} ({loopHint})");
        plot.XLabel("Iteration");

        if (ys.Length == 1)
        {
            var signal = plot.Add.Signal(ys);
            signal.LegendText = channel;
            signal.LineWidth = 2;
            signal.MarkerSize = 5;
            return;
        }

        var scatter = plot.Add.Scatter(xs, ys);
        scatter.LegendText = channel;
        scatter.LineWidth = 2;
        scatter.MarkerSize = ys.Length <= 64 ? 5 : 0;
        scatter.Smooth = false;
    }

    private static void WriteTimeOrSignalPlot(Plot plot, StoredSample[] samples, string channel)
    {
        var ys = samples.Select(s => s.Value).ToArray();
        var t0 = samples[0].Timestamp;
        var xs = samples
            .Select(s => (s.Timestamp - t0).TotalSeconds)
            .ToArray();

        plot.Title($"{channel}");
        plot.XLabel(samples.Length <= 1 || xs[^1] <= 0 ? "Sample" : "Time (s)");

        if (samples.Length == 1 || xs[^1] <= 0)
        {
            var signal = plot.Add.Signal(ys);
            signal.LegendText = channel;
            signal.LineWidth = 2;
            signal.MarkerSize = samples.Length <= 64 ? 5 : 0;
        }
        else
        {
            var scatter = plot.Add.Scatter(xs, ys);
            scatter.LegendText = channel;
            scatter.LineWidth = 2;
            scatter.MarkerSize = samples.Length <= 64 ? 5 : 0;
            scatter.Smooth = false;
        }
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "plot" : value;
    }
}
