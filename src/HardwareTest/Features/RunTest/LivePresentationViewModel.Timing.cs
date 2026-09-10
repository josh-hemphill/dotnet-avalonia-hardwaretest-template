using System.Collections.ObjectModel;
using HardwareTest.Features.Presentation;
using HardwareTest.OpenTap.Host;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Event marks, elapsed toolbar labels, and out-of-band spans for Chart + TimingStripView.
public partial class LivePresentationViewModel
{
    public ObservableCollection<MeasurementEventMark> Events { get; } = [];

    [Reactive] private string _chartElapsedText = string.Empty;
    [Reactive] private string _chartEventLabel = string.Empty;
    [Reactive] private bool _hasTimingStrip;
    [Reactive] private double _plotDurationSec;

    public IReadOnlyList<(double T0, double T1)> PlotOutOfBandSpans { get; private set; } = [];

    /// Stores a plan-owned Event mark and refreshes strip / toolbar labels.
    public void ApplyEvent(MeasurementEventMark mark)
    {
        Events.Add(mark);
        HasTimingStrip = true;
        if (PlotYsLength > 0 && PlotXs.Length > 0)
        {
            var elapsedMs = PlotXs[Math.Max(0, PlotYsLength - 1)] * 1000.0;
            ChartEventLabel = SeriesTimingChrome.FormatEventLabel(
                SeriesTimingChrome.LatestEventAt(Events, elapsedMs));
        }
        else
        {
            ChartElapsedText = SeriesTimingChrome.FormatElapsed(mark.ElapsedMs);
            ChartEventLabel = SeriesTimingChrome.FormatEventLabel(mark);
        }

        PlotDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearTimingChrome()
    {
        Events.Clear();
        HasTimingStrip = false;
        ChartElapsedText = string.Empty;
        ChartEventLabel = string.Empty;
        PlotOutOfBandSpans = [];
        PlotDurationSec = 0;
    }

    private void RefreshTimingChrome(LiveSeriesSnapshot snapshot)
    {
        var elapsedMs = snapshot.Length > 0 ? snapshot.Xs[snapshot.Length - 1] * 1000.0 : (double?)null;
        ChartElapsedText = SeriesTimingChrome.FormatElapsed(elapsedMs);
        ChartEventLabel = elapsedMs is { } ms
            ? SeriesTimingChrome.FormatEventLabel(SeriesTimingChrome.LatestEventAt(Events, ms))
            : string.Empty;
        PlotOutOfBandSpans = SeriesTimingChrome.OutOfBandSpans(
            snapshot.Xs,
            snapshot.Ys,
            snapshot.Length,
            snapshot.LimitLow,
            snapshot.LimitHigh);
        PlotDurationSec = snapshot.Length > 0 ? snapshot.Xs[snapshot.Length - 1] : 0;
        HasTimingStrip = Events.Count > 0 || PlotOutOfBandSpans.Count > 0;
        if (PlotOutOfBandSpans.Count > 0)
        {
            HasChartAttention = true;
        }
    }
}
