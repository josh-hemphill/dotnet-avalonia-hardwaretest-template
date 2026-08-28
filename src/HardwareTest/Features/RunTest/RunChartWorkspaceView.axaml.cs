using System;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Threading;

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

    private void OnLivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscribed is null || e.PropertyName != nameof(LivePresentationViewModel.FollowLive))
        {
            return;
        }

        ApplyFollowLive(_subscribed);
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

    private void ApplyPlot(RunTestViewModel vm, bool force)
    {
        var live = vm.Live;
        void Push()
        {
            Plot.SetLabels(live.PlotTitle, live.PlotYLabel, live.PlotLegendText);
            Plot.SetLimits(live.PlotLimitLow, live.PlotLimitHigh);
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
