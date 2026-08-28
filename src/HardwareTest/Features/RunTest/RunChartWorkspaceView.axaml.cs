using System;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Threading;

namespace HardwareTest.Features.RunTest;

public partial class RunChartWorkspaceView : UserControl
{
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
        ApplyPlot(vm, force: true);
    }

    private void Unsubscribe()
    {
        if (_subscribed is null)
        {
            return;
        }

        _subscribed.Live.PlotDataChanged -= OnPlotDataChanged;
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

    private void OnSeriesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_subscribed is null || sender is not ComboBox { SelectedItem: LiveSeriesItemViewModel item })
        {
            return;
        }

        _subscribed.Live.SelectSeriesCommand.Execute(item).Subscribe();
    }

    private void OnTimeWindowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_subscribed is null || sender is not ComboBox { SelectedItem: ChartTimeWindow window })
        {
            return;
        }

        _subscribed.Live.SelectTimeWindowCommand.Execute(window).Subscribe();
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
