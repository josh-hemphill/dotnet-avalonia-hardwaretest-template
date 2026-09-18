using HardwareTest.Features.Presentation;
using HardwareTest.Widgets.MeasurementPlot;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Operator-placed vertical readout on the live Chart workspace.
public partial class LivePresentationViewModel
{
    public const string CursorHint = "Tap the plot to place a readout line for an exact sample.";

    private double? _cursorX;

    [Reactive] private bool _hasCursor;

    /// Elapsed-seconds (or sample index) of the snapped readout, if any.
    public double? CursorX => _cursorX;

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ClearCursorCommand { get; private set; } = null!;

    /// Snaps the toolbar to the nearest published sample at <paramref name="x"/> and pauses follow-live.
    public void PlaceCursor(double x)
    {
        if (!PlotCursorReadout.TryNearestSample(
                PlotXs.AsSpan(0, PlotYsLength),
                PlotYs.AsSpan(0, PlotYsLength),
                PlotYsLength,
                x,
                out _,
                out var sampleX,
                out var sampleY))
        {
            return;
        }

        _cursorX = sampleX;
        HasCursor = true;
        FollowLive = false;
        ApplyCursorReadout(sampleX, sampleY);
    }

    /// Restores latest-sample toolbar text and drops the readout.
    public void ClearCursor()
    {
        if (!HasCursor && _cursorX is null)
        {
            return;
        }

        _cursorX = null;
        HasCursor = false;
        PublishSelectedSnapshot(_lastSelectedStep);
    }

    private void EnsureCursorCommand()
    {
        ClearCursorCommand = ReactiveCommand.Create(ClearCursor);
    }

    private void ResetCursorState()
    {
        _cursorX = null;
        HasCursor = false;
    }

    private void ResnapCursorAfterPublish()
    {
        if (_cursorX is not { } x)
        {
            return;
        }

        if (!PlotCursorReadout.TryNearestSample(
                PlotXs.AsSpan(0, PlotYsLength),
                PlotYs.AsSpan(0, PlotYsLength),
                PlotYsLength,
                x,
                out _,
                out var sampleX,
                out var sampleY))
        {
            _cursorX = null;
            HasCursor = false;
            return;
        }

        _cursorX = sampleX;
        HasCursor = true;
        ApplyCursorReadout(sampleX, sampleY);
    }

    private void ApplyCursorReadout(double sampleX, double sampleY)
    {
        ChartValueText = PlotCursorReadout.FormatValue(sampleY, PlotYLabel == "Value" ? null : PlotYLabel);
        ChartElapsedText = SeriesTimingChrome.FormatElapsed(sampleX * 1000.0);
        ChartEventLabel = SeriesTimingChrome.FormatEventLabel(
            SeriesTimingChrome.LatestEventAt(Events, sampleX * 1000.0));
        ChartBandText = PlotCursorReadout.FormatBand(sampleY, PlotLimitLow, PlotLimitHigh, PlotYLabel == "Value" ? null : PlotYLabel);
        ChartAgeText = "Readout";
    }
}
