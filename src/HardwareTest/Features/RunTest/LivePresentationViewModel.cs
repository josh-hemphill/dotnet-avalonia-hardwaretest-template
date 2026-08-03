using System;
using System.Collections.ObjectModel;
using System.Linq;
using HardwareTest.Features.Presentation;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Live measurement feed: the plot ring buffer and the per-step gauge tiles.
public partial class LivePresentationViewModel : ReactiveObject
{
    private const int PlotCapacity = 2048;

    private readonly double[] _plotRing = new double[PlotCapacity];
    private readonly double[] _plotPublish = new double[PlotCapacity];
    private readonly HashSet<string> _stepsWithSamples = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PresentationTileViewModel> _gaugeTiles = [];

    private int _plotCount;
    private int _plotWrite;
    private int _plotPublishLength;

    public LivePresentationViewModel() => PlotYs = _plotPublish;

    public ObservableCollection<PresentationTileViewModel> PresentationTiles { get; } = [];

    /// Raised after the publish buffer changes so the plot widget can pull without binding to an array.
    public event EventHandler? PlotDataChanged;

    public int PlotYsLength => _plotPublishLength;

    [Reactive] private double[] _plotYs = Array.Empty<double>();
    [Reactive] private bool _hasPlotData;
    [Reactive] private bool _showPlotForSelection;
    [Reactive] private bool _hasPresentationTiles;
    [Reactive] private string _plotLegendText = "Channel";
    [Reactive] private string _plotYLabel = "Value";
    [Reactive] private string _plotTitle = "Live measurements";

    /// Clears every live artifact so a new run does not inherit the previous run's feed.
    public void ResetForRun()
    {
        HasPlotData = false;
        ShowPlotForSelection = false;
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
            RefreshPlotVisibility(selectedStep);
        }

        if (PresentationRoleMap.IsRunGaugeSample(sample))
        {
            PresentationRoleMap.UpsertRunGauge(_gaugeTiles, sample, sampleStepPath ?? fallbackStepPath);
            RefreshPresentationTiles(selectedStep?.Path);
        }

        return plotted;
    }

    public void RefreshPlotVisibility(HierarchyStepViewModel? selectedStep)
        => ShowPlotForSelection = selectedStep is not null && _stepsWithSamples.Contains(selectedStep.Path);

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
