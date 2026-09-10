using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using HardwareTest.Features.Presentation;

namespace HardwareTest.Features.RunTest;

public partial class RunChartWorkspaceView : UserControl
{
    private readonly CompositeDisposable _subscriptions = new();
    private RunTestViewModel? _subscribed;

    public RunChartWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => Unsubscribe();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        if (DataContext is not RunTestViewModel vm)
        {
            return;
        }

        _subscribed = vm;
        vm.Live.PlotDataChanged += OnPlotDataChanged;
        vm.Live.PropertyChanged += OnLivePropertyChanged;
        vm.Live.Events.CollectionChanged += OnEventsChanged;
        _subscriptions.Add(vm.Live.ResetViewCommand.Subscribe(_ => ApplyPlot(vm, force: true)));
        ApplyPlot(vm, force: true);
    }

    private void Unsubscribe()
    {
        _subscriptions.Clear();
        if (_subscribed is null)
        {
            return;
        }

        _subscribed.Live.PlotDataChanged -= OnPlotDataChanged;
        _subscribed.Live.PropertyChanged -= OnLivePropertyChanged;
        _subscribed.Live.Events.CollectionChanged -= OnEventsChanged;
        _subscribed = null;
    }

    private void OnPlotDataChanged(object? sender, EventArgs e)
    {
        if (_subscribed is null)
        {
            return;
        }

        ApplyPlot(_subscribed, force: false);
    }

    private void OnEventsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_subscribed is null)
        {
            return;
        }

        ApplyMarks(_subscribed);
    }

    private void OnLivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscribed is null)
        {
            return;
        }

        if (e.PropertyName == nameof(LivePresentationViewModel.FollowLive))
        {
            ApplyFollowLive(_subscribed);
            return;
        }

        if (e.PropertyName == nameof(LivePresentationViewModel.PlotOutOfBandSpans))
        {
            ApplyMarks(_subscribed);
        }
    }

    private void ApplyFollowLive(RunTestViewModel vm)
    {
        void Push()
        {
            Plot.SetFollowLive(vm.Live.FollowLive);
            if (vm.Live.FollowLive)
            {
                Plot.ResetView();
            }
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyFollowLive(vm));
            return;
        }

        Push();
    }

    private void ApplyMarks(RunTestViewModel vm)
    {
        void Push()
        {
            Plot.SetEvents(SeriesTimingChrome.ToPlotTicks(vm.Live.Events));
            Plot.SetOutOfBandSpans(vm.Live.PlotOutOfBandSpans);
            Plot.RefreshOverlays();
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyMarks(vm));
            return;
        }

        Push();
    }

    private void ApplyPlot(RunTestViewModel vm, bool force)
    {
        var live = vm.Live;
        void Push()
        {
            Plot.SetLabels(live.PlotTitle, live.PlotYLabel, live.PlotLegendText);
            Plot.SetLimits(live.PlotLimitLow, live.PlotLimitHigh);
            Plot.SetEvents(SeriesTimingChrome.ToPlotTicks(live.Events));
            Plot.SetOutOfBandSpans(live.PlotOutOfBandSpans);
            Plot.SetFollowLive(live.FollowLive);
            Plot.UpdateTimeSeries(live.PlotXs, live.PlotYs, live.PlotYsLength, live.FollowLive, force);
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyPlot(vm, force));
            return;
        }

        Push();
    }
}
