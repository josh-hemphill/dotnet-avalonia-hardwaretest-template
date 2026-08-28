using System.Collections.ObjectModel;
using System.Globalization;
using HardwareTest.Features.Presentation;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Band (KPI strip) vs Focus (legacy earned trend). Chart now lives in a Run workspace.
public enum PresentationChromeMode
{
    Band,
    Focus,
}

/// Live measurement feed: per-metric series buffers and per-step gauge tiles.
public partial class LivePresentationViewModel : ReactiveObject
{
    public const int MaximumLiveSeries = 8;

    private readonly Dictionary<LiveSeriesKey, LiveSeriesBuffer> _series = [];
    private readonly List<LiveSeriesKey> _seriesOrder = [];
    private readonly HashSet<string> _stepsWithSamples = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PresentationTileViewModel> _gaugeTiles = [];
    private readonly HashSet<LiveSeriesKey> _announcedOutOfBand = [];
    private HierarchyStepViewModel? _lastSelectedStep;
    private LiveSeriesKey? _manualSeries;

    public LivePresentationViewModel()
    {
        PlotXs = [];
        PlotYs = [];
        ToggleFocusTrendCommand = ReactiveCommand.Create(ToggleFocusTrend);
        ResetViewCommand = ReactiveCommand.Create(ResetView);
        SelectSeriesCommand = ReactiveCommand.Create<LiveSeriesItemViewModel?>(SelectSeries);
        SelectTimeWindowCommand = ReactiveCommand.Create<ChartTimeWindow>(SelectTimeWindow);
    }

    public ObservableCollection<PresentationTileViewModel> PresentationTiles { get; } = [];
    public ObservableCollection<LiveSeriesItemViewModel> AvailableSeries { get; } = [];
    public ObservableCollection<ChartTimeWindow> TimeWindows { get; } = new(ChartTimeWindow.AllWindows);

    /// Raised after the publish buffer changes so the plot widget can pull without binding to an array.
    public event EventHandler? PlotDataChanged;

    public int PlotYsLength { get; private set; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleFocusTrendCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ResetViewCommand { get; }
    public ReactiveCommand<LiveSeriesItemViewModel?, System.Reactive.Unit> SelectSeriesCommand { get; }
    public ReactiveCommand<ChartTimeWindow, System.Reactive.Unit> SelectTimeWindowCommand { get; }

    [Reactive] private double[] _plotXs = [];
    [Reactive] private double[] _plotYs = [];
    [Reactive] private bool _hasPlotData;
    [Reactive] private bool _hasChartData;
    [Reactive] private bool _hasChartAttention;
    [Reactive] private bool _showPlotForSelection;
    [Reactive] private bool _showFocusTrend;
    [Reactive] private bool _offerShowTrend;
    [Reactive] private bool _offerOpenChart;
    [Reactive] private bool _hasPresentationTiles;
    [Reactive] private bool _userWantsFocus;
    [Reactive] private bool _followLive = true;
    [Reactive] private PresentationChromeMode _chromeMode = PresentationChromeMode.Band;
    [Reactive] private string _plotLegendText = "Channel";
    [Reactive] private string _plotYLabel = "Value";
    [Reactive] private string _plotTitle = "Live measurements";
    [Reactive] private string _focusTrendTip = string.Empty;
    [Reactive] private string _chartValueText = "—";
    [Reactive] private string _chartBandText = "No limits";
    [Reactive] private string _chartAgeText = string.Empty;
    [Reactive] private string _chartEmptyText = "No live measurements yet.";
    [Reactive] private LiveSeriesItemViewModel? _selectedSeries;
    [Reactive] private ChartTimeWindow _selectedTimeWindow = ChartTimeWindow.ThirtySeconds;
    [Reactive] private double? _plotLimitLow;
    [Reactive] private double? _plotLimitHigh;

    /// Clears every live artifact so a new run does not inherit the previous run's feed.
    public void ResetForRun()
    {
        HasPlotData = false;
        HasChartData = false;
        HasChartAttention = false;
        ShowPlotForSelection = false;
        ShowFocusTrend = false;
        OfferShowTrend = false;
        OfferOpenChart = false;
        UserWantsFocus = false;
        FollowLive = true;
        ChromeMode = PresentationChromeMode.Band;
        FocusTrendTip = string.Empty;
        ChartValueText = "—";
        ChartBandText = "No limits";
        ChartAgeText = string.Empty;
        ChartEmptyText = "No live measurements yet.";
        _lastSelectedStep = null;
        _manualSeries = null;
        _stepsWithSamples.Clear();
        _gaugeTiles.Clear();
        _series.Clear();
        _seriesOrder.Clear();
        _announcedOutOfBand.Clear();
        PresentationTiles.Clear();
        AvailableSeries.Clear();
        HasPresentationTiles = false;
        SelectedSeries = null;
        SelectedTimeWindow = ChartTimeWindow.ThirtySeconds;
        PlotLegendText = "Channel";
        PlotYLabel = "Value";
        PlotTitle = "Live measurements";
        PlotLimitLow = null;
        PlotLimitHigh = null;
        PlotXs = [];
        PlotYs = [];
        PlotYsLength = 0;
        PlotDataChanged?.Invoke(this, EventArgs.Empty);
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
            var path = sampleStepPath ?? fallbackStepPath ?? string.Empty;
            var key = new LiveSeriesKey(path, sample.EffectiveMetricKey);
            var buffer = GetOrCreateSeries(key);
            buffer.Append(sample.Value, sample.Timestamp, sample.LimitLow, sample.LimitHigh, sample.Unit);
            if (!string.IsNullOrWhiteSpace(path))
            {
                _stepsWithSamples.Add(path);
            }

            HasPlotData = true;
            HasChartData = true;
            plotted = true;
            RefreshSeriesList();
            PublishSelectedSnapshot(selectedStep);
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

    /// Recomputes chart availability and attention from selection and out-of-band gauges.
    public void RefreshChrome(HierarchyStepViewModel? selectedStep)
    {
        _lastSelectedStep = selectedStep;
        HasChartData = _series.Values.Any(s => s.Count > 0);
        var outOfBand = PresentationTiles.Any(t => t.IsOutOfBand)
            || _series.Values.Any(s => s.IsOutOfBand);
        HasChartAttention = outOfBand && HasChartData;
        OfferOpenChart = HasChartData;
        OfferShowTrend = HasChartData;
        if (HasChartAttention)
        {
            FocusTrendTip = "Out of band — open Chart for waveform detail.";
        }
        else if (HasChartData)
        {
            FocusTrendTip = "Open Chart for waveform detail.";
        }
        else
        {
            FocusTrendTip = string.Empty;
        }

        ChromeMode = PresentationChromeMode.Band;
        ShowFocusTrend = false;
        ShowPlotForSelection = false;
        if (HasChartData)
        {
            PublishSelectedSnapshot(selectedStep);
        }
    }

    private void ToggleFocusTrend()
    {
        UserWantsFocus = !UserWantsFocus;
        RefreshChrome(_lastSelectedStep);
    }

    private void SelectSeries(LiveSeriesItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _manualSeries = item.Key;
        SelectedSeries = item;
        PublishSelectedSnapshot(_lastSelectedStep);
    }

    private void SelectTimeWindow(ChartTimeWindow window)
    {
        SelectedTimeWindow = window;
        PublishSelectedSnapshot(_lastSelectedStep);
    }

    private void ResetView()
    {
        FollowLive = true;
        PublishSelectedSnapshot(_lastSelectedStep);
        PlotDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private LiveSeriesBuffer GetOrCreateSeries(LiveSeriesKey key)
    {
        if (_series.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (_series.Count >= MaximumLiveSeries)
        {
            LiveSeriesKey? drop = null;
            foreach (var candidate in _seriesOrder)
            {
                if (_manualSeries is { } keep && keep.Equals(candidate))
                {
                    continue;
                }

                drop = candidate;
                break;
            }

            if (drop is { } dropped && _series.ContainsKey(dropped))
            {
                _series.Remove(dropped);
                _seriesOrder.Remove(dropped);
            }
        }

        var created = new LiveSeriesBuffer(key);
        _series[key] = created;
        _seriesOrder.Add(key);
        return created;
    }

    private void RefreshSeriesList()
    {
        AvailableSeries.Clear();
        foreach (var key in _seriesOrder)
        {
            if (!_series.TryGetValue(key, out var buffer) || buffer.Count == 0)
            {
                continue;
            }

            AvailableSeries.Add(new LiveSeriesItemViewModel(key, buffer.Unit, buffer.IsOutOfBand));
        }
    }

    private void PublishSelectedSnapshot(HierarchyStepViewModel? selectedStep)
    {
        var key = ResolveSelectedKey(selectedStep);
        if (key is null || !_series.TryGetValue(key.Value, out var buffer))
        {
            PlotYsLength = 0;
            PlotXs = [];
            PlotYs = [];
            ChartValueText = "—";
            ChartBandText = "No limits";
            ChartAgeText = string.Empty;
            PlotLimitLow = null;
            PlotLimitHigh = null;
            PlotDataChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var snapshot = buffer.Snapshot(SelectedTimeWindow.Duration);
        PlotXs = snapshot.Xs;
        PlotYs = snapshot.Ys;
        PlotYsLength = snapshot.Length;
        PlotLegendText = key.Value.MetricKey;
        PlotTitle = key.Value.DisplayName;
        PlotYLabel = string.IsNullOrWhiteSpace(snapshot.Unit) ? "Value" : snapshot.Unit!;
        PlotLimitLow = snapshot.LimitLow;
        PlotLimitHigh = snapshot.LimitHigh;
        ChartValueText = snapshot.ValueText;
        ChartBandText = snapshot.BandText;
        ChartAgeText = FormatAge(snapshot.LatestTimestamp);
        ChartEmptyText = snapshot.Length == 0 ? "No samples in this window." : string.Empty;
        if (SelectedSeries is null || !SelectedSeries.Key.Equals(key.Value))
        {
            SelectedSeries = AvailableSeries.FirstOrDefault(s => s.Key.Equals(key.Value))
                ?? new LiveSeriesItemViewModel(key.Value, snapshot.Unit, snapshot.IsOutOfBand);
        }

        PlotDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private LiveSeriesKey? ResolveSelectedKey(HierarchyStepViewModel? selectedStep)
    {
        if (_manualSeries is { } manual && _series.ContainsKey(manual))
        {
            return manual;
        }

        var path = selectedStep?.Path;
        if (!string.IsNullOrWhiteSpace(path))
        {
            for (var i = _seriesOrder.Count - 1; i >= 0; i--)
            {
                var key = _seriesOrder[i];
                if (string.Equals(key.StepPath, path, StringComparison.OrdinalIgnoreCase)
                    && _series.TryGetValue(key, out var buf)
                    && buf.Count > 0)
                {
                    return key;
                }
            }
        }

        for (var i = _seriesOrder.Count - 1; i >= 0; i--)
        {
            var key = _seriesOrder[i];
            if (_series.TryGetValue(key, out var buf) && buf.Count > 0)
            {
                return key;
            }
        }

        return null;
    }

    private static string FormatAge(DateTimeOffset? timestamp)
    {
        if (timestamp is null)
        {
            return string.Empty;
        }

        var age = DateTimeOffset.UtcNow - timestamp.Value;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalSeconds < 1)
        {
            return "Sample age <1 s";
        }

        return $"Sample age {age.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s";
    }
}
