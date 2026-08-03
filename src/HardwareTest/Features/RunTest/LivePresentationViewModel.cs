using System;
using System.Collections.ObjectModel;
using System.Linq;
using HardwareTest.Features.Presentation;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Band (KPI strip) vs Focus (earned trend pane) for live Presentation chrome.
public enum PresentationChromeMode
{
    Band,
    Focus,
}

/// Live measurement feed: the plot ring buffer and the per-step gauge tiles.
public partial class LivePresentationViewModel : ReactiveObject
{
    private const int PlotCapacity = 2048;

    private readonly double[] _plotRing = new double[PlotCapacity];
    private readonly double[] _plotPublish = new double[PlotCapacity];
    private readonly HashSet<string> _stepsWithSamples = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PresentationTileViewModel> _gaugeTiles = [];
    private HierarchyStepViewModel? _lastSelectedStep;
    private bool _suppressAutoFocus;

    private int _plotCount;
    private int _plotWrite;
    private int _plotPublishLength;

    public LivePresentationViewModel()
    {
        PlotYs = _plotPublish;
        ToggleFocusTrendCommand = ReactiveCommand.Create(ToggleFocusTrend);
    }

    public ObservableCollection<PresentationTileViewModel> PresentationTiles { get; } = [];

    /// Raised after the publish buffer changes so the plot widget can pull without binding to an array.
    public event EventHandler? PlotDataChanged;

    public int PlotYsLength => _plotPublishLength;

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleFocusTrendCommand { get; }

    [Reactive] private double[] _plotYs = Array.Empty<double>();
    [Reactive] private bool _hasPlotData;
    [Reactive] private bool _showPlotForSelection;
    [Reactive] private bool _showFocusTrend;
    /// True when Band chrome can offer an explicit "Show trend" control (hidden while Focus is open).
    [Reactive] private bool _offerShowTrend;
    [Reactive] private bool _hasPresentationTiles;
    [Reactive] private bool _userWantsFocus;
    [Reactive] private PresentationChromeMode _chromeMode = PresentationChromeMode.Band;
    [Reactive] private string _plotLegendText = "Channel";
    [Reactive] private string _plotYLabel = "Value";
    [Reactive] private string _plotTitle = "Live measurements";
    [Reactive] private string _focusTrendTip = string.Empty;

    /// Clears every live artifact so a new run does not inherit the previous run's feed.
    public void ResetForRun()
    {
        HasPlotData = false;
        ShowPlotForSelection = false;
        ShowFocusTrend = false;
        OfferShowTrend = false;
        UserWantsFocus = false;
        ChromeMode = PresentationChromeMode.Band;
        FocusTrendTip = string.Empty;
        _suppressAutoFocus = false;
        _lastSelectedStep = null;
        _stepsWithSamples.Clear();
        _gaugeTiles.Clear();
        PresentationTiles.Clear();
        HasPresentationTiles = false;
        PlotLegendText = "Channel";
        PlotYLabel = "Value";
        PlotTitle = "Live measurements";
        ResetPlotBuffer();
    }

    /// Routes one sample to the plot and/or gauge tiles. Returns true when the plot published a frame.
    public bool ApplySample(
        MeasurementSampleEvent sample,
        string? sampleStepPath,
        string? fallbackStepPath,
        HierarchyStepViewModel? selectedStep)
    {
        var plotted = false;
        if (PresentationRoleMap.IsTimeseriesPlotSample(sample))
        {
            HasPlotData = true;
            if (!string.IsNullOrWhiteSpace(sampleStepPath))
            {
                _stepsWithSamples.Add(sampleStepPath);
            }

            var key = sample.EffectiveMetricKey;
            PlotLegendText = key;
            PlotTitle = key;
            PlotYLabel = string.IsNullOrWhiteSpace(sample.Unit) ? "Value" : sample.Unit!;
            AppendSampleToPlot(sample.Value);
            PlotYs = _plotPublish;
            PlotDataChanged?.Invoke(this, EventArgs.Empty);
            plotted = true;
        }

        if (PresentationRoleMap.IsRunGaugeSample(sample))
        {
            PresentationRoleMap.UpsertRunGauge(_gaugeTiles, sample, sampleStepPath ?? fallbackStepPath);
            RefreshPresentationTiles(selectedStep?.Path);
        }

        RefreshChrome(selectedStep);
        return plotted;
    }

    public void RefreshPlotVisibility(HierarchyStepViewModel? selectedStep)
        => RefreshChrome(selectedStep);

    public void RefreshPresentationTiles(string? stepPath)
    {
        PresentationTiles.Clear();
        if (string.IsNullOrWhiteSpace(stepPath))
        {
            HasPresentationTiles = false;
            return;
        }

        foreach (var tile in _gaugeTiles
                     .Where(t => string.Equals(t.StepPath, stepPath, StringComparison.OrdinalIgnoreCase))
                     .Take(PresentationRoleMap.MaxRunGaugeTiles))
        {
            PresentationTiles.Add(tile);
        }

        HasPresentationTiles = PresentationTiles.Count > 0;
    }

    /// Recomputes Band vs Focus from selection, out-of-band gauges, and explicit expand.
    public void RefreshChrome(HierarchyStepViewModel? selectedStep)
    {
        if (!ReferenceEquals(selectedStep, _lastSelectedStep)
            && !string.Equals(selectedStep?.Path, _lastSelectedStep?.Path, StringComparison.OrdinalIgnoreCase))
        {
            _suppressAutoFocus = false;
        }

        _lastSelectedStep = selectedStep;
        var path = selectedStep?.Path;
        var stepHasSeries = !string.IsNullOrWhiteSpace(path)
            && _stepsWithSamples.Contains(path);
        var outOfBand = PresentationTiles.Any(t => t.IsOutOfBand);
        var autoFocus = !_suppressAutoFocus && (stepHasSeries || (outOfBand && HasPlotData));
        var wantFocus = UserWantsFocus || autoFocus;

        if (wantFocus && !HasPlotData)
        {
            ChromeMode = PresentationChromeMode.Band;
            ShowFocusTrend = false;
            OfferShowTrend = false;
            ShowPlotForSelection = false;
            FocusTrendTip = outOfBand
                ? "Out of band — no timeseries buffered for Focus trend."
                : string.Empty;
            return;
        }

        if (wantFocus)
        {
            ChromeMode = PresentationChromeMode.Focus;
            ShowFocusTrend = true;
            OfferShowTrend = false;
            ShowPlotForSelection = true;
            FocusTrendTip = string.Empty;
            return;
        }

        ChromeMode = PresentationChromeMode.Band;
        ShowFocusTrend = false;
        OfferShowTrend = HasPlotData;
        ShowPlotForSelection = false;
        FocusTrendTip = HasPlotData
            ? "Show trend for waveform detail."
            : string.Empty;
    }

    private void ToggleFocusTrend()
    {
        if (ShowFocusTrend)
        {
            UserWantsFocus = false;
            _suppressAutoFocus = true;
        }
        else
        {
            UserWantsFocus = true;
            _suppressAutoFocus = false;
        }

        RefreshChrome(_lastSelectedStep);
    }

    private void ResetPlotBuffer()
    {
        Array.Clear(_plotRing);
        Array.Clear(_plotPublish);
        _plotCount = 0;
        _plotWrite = 0;
        _plotPublishLength = 0;
        PlotYs = _plotPublish;
    }

    private void AppendSampleToPlot(double value)
    {
        _plotRing[_plotWrite] = value;
        _plotWrite = (_plotWrite + 1) % PlotCapacity;
        if (_plotCount < PlotCapacity)
        {
            _plotCount++;
        }

        var start = _plotCount == PlotCapacity ? _plotWrite : 0;
        for (var i = 0; i < _plotCount; i++)
        {
            _plotPublish[i] = _plotRing[(start + i) % PlotCapacity];
        }

        _plotPublishLength = _plotCount;
    }
}
