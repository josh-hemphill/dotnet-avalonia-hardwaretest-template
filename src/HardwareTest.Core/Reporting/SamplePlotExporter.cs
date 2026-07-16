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
            .Where(s => string.Equals(s.Channel, channel, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Timestamp)
            .ToArray();
        if (samples.Length == 0)
        {
            return null;
        }

        Directory.CreateDirectory(outputDirectory);
        var safePlan = Sanitize(string.IsNullOrWhiteSpace(run.PlanId) ? run.PlanName : run.PlanId);
        var path = Path.Combine(outputDirectory, $"{safePlan}-{Sanitize(channel)}.png");
        WriteChannelPlotPng(samples, channel, path);
        return path;
    }

    public static IReadOnlyList<string> ExportAllChannels(TestRunRecord run, string outputDirectory)
    {
        var channels = run.Samples.Select(s => s.Channel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
        var ys = samples.Select(s => s.Value).ToArray();
        var t0 = samples[0].Timestamp;
        var xs = samples
            .Select(s => (s.Timestamp - t0).TotalSeconds)
            .ToArray();

        var plot = new Plot();
        plot.Title($"{channel}");
        plot.XLabel(samples.Length <= 1 || xs[^1] <= 0 ? "Sample" : "Time (s)");
        plot.YLabel(channel);

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

        plot.Axes.AutoScale();
        plot.ShowLegend();
        plot.SavePng(path, PlotWidth, PlotHeight);
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
