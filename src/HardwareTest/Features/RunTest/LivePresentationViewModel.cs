using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using HardwareTest.Features.Presentation;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Live measurement feed: per-metric series buffers and per-step gauge tiles.
public partial class LivePresentationViewModel : ReactiveObject
{
    public const int MaximumLiveSeries = 8;

    private readonly Dictionary<LiveSeriesKey, LiveSeriesBuffer> _series = [];
    private readonly List<LiveSeriesKey> _seriesOrder = [];
    private readonly List<PresentationTileViewModel> _gaugeTiles = [];
    private readonly HashSet<LiveSeriesKey> _announcedOutOfBand = [];
    private HierarchyStepViewModel? _lastSelectedStep;
    private LiveSeriesKey? _manualSeries;
    private int _seriesSyncDepth;

    public LivePresentationViewModel()
    {
        ResetViewCommand = ReactiveCommand.Create(ResetView);
        SelectSeriesCommand = ReactiveCommand.Create<LiveSeriesItemViewModel?>(SelectSeries);
        SelectTimeWindowCommand = ReactiveCommand.Create<ChartTimeWindow>(SelectTimeWindow);
        EnsureCursorCommand();
        PropertyChanged += OnLivePropertyChanged;
    }

    public ObservableCollection<PresentationTileViewModel> PresentationTiles { get; } = [];
    public ObservableCollection<LiveSeriesItemViewModel> AvailableSeries { get; } = [];
    public ObservableCollection<ChartTimeWindow> TimeWindows { get; } = new(ChartTimeWindow.AllWindows);

    /// Raised after the publish buffer changes so the plot widget can pull without binding to an array.
    public event EventHandler? PlotDataChanged;

    public int PlotYsLength { get; private set; }

    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> ResetViewCommand { get; }
    public ReactiveCommand<LiveSeriesItemViewModel?, ReactiveUI.Primitives.RxVoid> SelectSeriesCommand { get; }
    public ReactiveCommand<ChartTimeWindow, ReactiveUI.Primitives.RxVoid> SelectTimeWindowCommand { get; }

    [Reactive] private double[] _plotXs = new double[LiveSeriesBuffer.Capacity];
    [Reactive] private double[] _plotYs = new double[LiveSeriesBuffer.Capacity];
    [Reactive] private bool _hasPlotData;
    [Reactive] private bool _hasChartData;
    [Reactive] private bool _hasChartAttention;
    [Reactive] private bool _compareMetrics;
    [Reactive] private bool _offerOpenChart;
    [Reactive] private bool _hasPresentationTiles;
    [Reactive] private bool _followLive = true;
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

    public bool CanCompareMetrics => AvailableSeries.Count > 1;
    public bool ShowComparisonCharts => CompareMetrics && CanCompareMetrics;

    /// Clears every live artifact so a new run does not inherit the previous run's feed.
    public void ResetForRun()
    {
        HasPlotData = false;
        HasChartData = false;
        HasChartAttention = false;
        OfferOpenChart = false;
        FollowLive = true;
        FocusTrendTip = string.Empty;
        ChartValueText = "—";
        ChartBandText = "No limits";
        ChartAgeText = string.Empty;
        ChartElapsedText = string.Empty;
        ChartEventLabel = string.Empty;
        ChartEmptyText = "No live measurements yet.";
        ResetCursorState();
        _lastSelectedStep = null;
        _manualSeries = null;
        CompareMetrics = false;
        _gaugeTiles.Clear();
        _series.Clear();
        _seriesOrder.Clear();
        _announcedOutOfBand.Clear();
        ClearTimingChrome();
        BeginSeriesSync();
        try
        {
            PresentationTiles.Clear();
            AvailableSeries.Clear();
            RaiseComparisonAvailability();
            HasPresentationTiles = false;
            SelectedSeries = null;
        }
        finally
        {
            EndSeriesSync();
        }
        SelectedTimeWindow = ChartTimeWindow.ThirtySeconds;
        PlotLegendText = "Channel";
        PlotYLabel = "Value";
        PlotTitle = "Live measurements";
        PlotLimitLow = null;
        PlotLimitHigh = null;
        PlotYsLength = 0;
        PlotDataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// Routes one sample to the plot buffer or gauge tiles. Returns true when it buffered a plot sample.
    public bool ApplySample(
        MeasurementSampleEvent sample,
        string? sampleStepPath,
        string? fallbackStepPath,
        HierarchyStepViewModel? selectedStep)
    {
        var plotted = false;
        if (PresentationRoleMap.IsTimeseriesPlotSample(sample) && double.IsFinite(sample.Value))
        {
            var path = sampleStepPath ?? fallbackStepPath ?? string.Empty;
            var key = new LiveSeriesKey(path, sample.EffectiveMetricKey);
            var buffer = GetOrCreateSeries(key);
            var elapsed = sample.ElapsedMs is { } ms && double.IsFinite(ms) ? ms : (double?)null;
            buffer.Append(sample.Value, sample.Timestamp, sample.LimitLow, sample.LimitHigh, sample.Unit, elapsed);
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

        HasPresentationTiles = PresentationTiles.Any(t => !t.IsStrip);
    }

    /// Recomputes chart availability and attention from selection and out-of-band gauges.
    public void RefreshChrome(HierarchyStepViewModel? selectedStep)
    {
        if (!string.Equals(_lastSelectedStep?.Path, selectedStep?.Path, StringComparison.OrdinalIgnoreCase))
        {
            _manualSeries = null;
        }

        _lastSelectedStep = selectedStep;
        BeginSeriesSync();
        try
        {
            RefreshSeriesList();
            PublishSelectedSnapshot();
        }
        finally
        {
            EndSeriesSync();
        }

        HasChartData = AvailableSeries.Count > 0;
        OfferOpenChart = HasChartData;

        var attention = HasChartData
            && (PresentationTiles.Any(t => t.IsOutOfBand)
                || AvailableSeries.Any(s => _series[s.Key].IsOutOfBand)
                || ChartBandText.Contains("Out of band", StringComparison.OrdinalIgnoreCase));
        if (attention)
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

        HasChartAttention = attention;
    }

    private void SelectSeries(LiveSeriesItemViewModel? item)
    {
        if (item is null || !AvailableSeries.Contains(item))
        {
            return;
        }

        _manualSeries = item.Key;
        SelectedSeries = item;
        PublishSelectedSnapshot();
    }

    private void SelectTimeWindow(ChartTimeWindow window) => SelectedTimeWindow = window;

    private void OnLivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedSeries))
        {
            OnUserSelectedSeries(SelectedSeries);
            return;
        }

        if (e.PropertyName == nameof(SelectedTimeWindow))
        {
            PublishSelectedSnapshot();
            return;
        }

        if (e.PropertyName == nameof(CompareMetrics))
        {
            RaiseComparisonAvailability();
            RefreshComparisonCharts(SelectedSeries?.Key);
            return;
        }

        if (e.PropertyName == nameof(FollowLive) && FollowLive && HasCursor)
        {
            _cursorX = null;
            HasCursor = false;
            PublishSelectedSnapshot();
        }
    }

    private void OnUserSelectedSeries(LiveSeriesItemViewModel? item)
    {
        if (IsSyncingSeries || item is null || !AvailableSeries.Contains(item))
        {
            return;
        }

        var alreadyLocked = _manualSeries is { } existing && existing.Equals(item.Key);
        _manualSeries = item.Key;
        if (!alreadyLocked)
        {
            PublishSelectedSnapshot();
        }
    }

    private void ResetView()
    {
        FollowLive = true;
        ClearCursor();
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
        var desired = new List<LiveSeriesKey>();
        foreach (var key in _seriesOrder)
        {
            if (_lastSelectedStep is { } selected
                && !string.Equals(selected.Path, key.StepPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            desired.Add(key);
        }

        if (SeriesKeysMatch(AvailableSeries, desired))
        {
            return;
        }

        AvailableSeries.Clear();
        foreach (var key in desired)
        {
            var buffer = _series[key];
            AvailableSeries.Add(new LiveSeriesItemViewModel(key, buffer.Unit, buffer.IsOutOfBand));
        }
        RaiseComparisonAvailability();
    }

    private void RaiseComparisonAvailability()
    {
        this.RaisePropertyChanged(nameof(CanCompareMetrics));
        this.RaisePropertyChanged(nameof(ShowComparisonCharts));
    }

    private void RefreshComparisonCharts(LiveSeriesKey? primary)
    {
        foreach (var series in AvailableSeries)
        {
            series.Comparison = ShowComparisonCharts && series.Key != primary
                ? _series[series.Key].Snapshot(SelectedTimeWindow.Duration)
                : null;
        }
    }

    private static bool SeriesKeysMatch(
        ObservableCollection<LiveSeriesItemViewModel> current,
        IReadOnlyList<LiveSeriesKey> desired)
    {
        if (current.Count != desired.Count)
        {
            return false;
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (!current[i].Key.Equals(desired[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void PublishSelectedSnapshot()
    {
        var item = _manualSeries is { } manual
            ? AvailableSeries.FirstOrDefault(series => series.Key.Equals(manual))
            : null;
        item ??= AvailableSeries.LastOrDefault();
        if (item is null)
        {
            if (!HasPlotData)
            {
                return;
            }

            HasPlotData = false;
            SelectedSeries = null;
            PlotYsLength = 0;
            ChartValueText = "—";
            ChartBandText = "No limits";
            ChartAgeText = string.Empty;
            ChartElapsedText = string.Empty;
            ChartEventLabel = string.Empty;
            PlotOutOfBandSpans = [];
            PlotDurationSec = 0;
            PlotLimitLow = null;
            PlotLimitHigh = null;
            ResetCursorState();
            PlotDataChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var key = item.Key;
        var buffer = _series[key];
        var snapshot = buffer.Snapshot(SelectedTimeWindow.Duration);
        CopyPlotBuffer(PlotXs, snapshot.Xs, snapshot.Length);
        CopyPlotBuffer(PlotYs, snapshot.Ys, snapshot.Length);
        PlotYsLength = snapshot.Length;
        PlotLegendText = key.MetricKey;
        PlotTitle = key.DisplayName;
        PlotYLabel = string.IsNullOrWhiteSpace(snapshot.Unit) ? "Value" : snapshot.Unit!;
        PlotLimitLow = snapshot.LimitLow;
        PlotLimitHigh = snapshot.LimitHigh;
        ChartValueText = snapshot.ValueText;
        ChartBandText = snapshot.BandText;
        ChartAgeText = FormatAge(snapshot.LatestTimestamp);
        RefreshTimingChrome(snapshot);
        ChartEmptyText = snapshot.Length == 0 ? "No samples in this window." : string.Empty;
        HasPlotData = snapshot.Length > 0;
        ResnapCursorAfterPublish();
        if (!ReferenceEquals(SelectedSeries, item))
        {
            BeginSeriesSync();
            try
            {
                SelectedSeries = item;
            }
            finally
            {
                EndSeriesSync();
            }
        }

        RefreshComparisonCharts(key);
        PlotDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsSyncingSeries => _seriesSyncDepth > 0;

    private void BeginSeriesSync() => _seriesSyncDepth++;

    private void EndSeriesSync() => _seriesSyncDepth--;

    private static void CopyPlotBuffer(double[] target, double[] source, int length)
    {
        if (length > 0)
        {
            Array.Copy(source, 0, target, 0, Math.Min(length, target.Length));
        }
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
