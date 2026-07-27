using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace HardwareTest.Features.RunTest;

public partial class RunTestView : UserControl
{
    private RunTestViewModel? _subscribed;

    public RunTestView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => Unsubscribe();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        if (DataContext is RunTestViewModel vm)
        {
            _subscribed = vm;
            vm.PlotDataChanged += OnPlotDataChanged;
            vm.RequestScrollToSelectedStep += OnRequestScrollToSelectedStep;
            vm.RequestFocusStepSearch += OnRequestFocusStepSearch;
            vm.RequestPlanFilePath = PickPlanFileAsync;
            if (Plot is not null)
            {
                Plot.UpdateData(vm.PlotYs, vm.PlotYsLength, force: true);
            }
        }
    }

    private void Unsubscribe()
    {
        if (_subscribed is null)
        {
            return;
        }

        _subscribed.PlotDataChanged -= OnPlotDataChanged;
        _subscribed.RequestScrollToSelectedStep -= OnRequestScrollToSelectedStep;
        _subscribed.RequestFocusStepSearch -= OnRequestFocusStepSearch;
        _subscribed.RequestPlanFilePath = null;
        _subscribed = null;
    }

    private async Task<string?> PickPlanFileAsync(CancellationToken cancellationToken)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return null;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open OpenTAP plan",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("OpenTAP Plan") { Patterns = ["*.TapPlan"] },
                FilePickerFileTypes.All,
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private void OnPlotDataChanged(object? sender, EventArgs e)
    {
        if (_subscribed is null || Plot is null)
        {
            return;
        }

        var ys = _subscribed.PlotYs;
        var length = _subscribed.PlotYsLength;
        var title = _subscribed.PlotTitle;
        var yLabel = _subscribed.PlotYLabel;
        var legend = _subscribed.PlotLegendText;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                Plot.SetLabels(title, yLabel, legend);
                Plot.UpdateData(ys, length);
            });
            return;
        }

        Plot.SetLabels(title, yLabel, legend);
        Plot.UpdateData(ys, length);
    }

    private void OnRequestScrollToSelectedStep(object? sender, EventArgs e)
    {
        void Scroll()
        {
            if (StepList?.SelectedItem is null)
            {
                return;
            }

            StepList.ScrollIntoView(StepList.SelectedItem);
        }

        // Defer until after layout so Continue (card collapse) does not leave the step off-screen.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Scroll, DispatcherPriority.Loaded);
            return;
        }

        Dispatcher.UIThread.Post(Scroll, DispatcherPriority.Loaded);
    }

    private void OnRequestFocusStepSearch(object? sender, EventArgs e)
        => StepSearchBox?.Focus();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_subscribed is null)
        {
            return;
        }

        if (e.Key is Key.Oem2 or Key.Divide)
        {
            ((ICommand)_subscribed.FocusStepSearchCommand).Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.F)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ((ICommand)_subscribed.PrevFailCommand).Execute(null);
        }
        else
        {
            ((ICommand)_subscribed.NextFailCommand).Execute(null);
        }

        e.Handled = true;
    }

    private void OnStageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_subscribed?.SelectedStage?.Step is { } stage)
        {
            _subscribed.SelectedStep = stage;
            _subscribed.OpenSelectedStepDetail();
        }
    }

    private void OnHierarchyDoubleTapped(object? sender, TappedEventArgs e)
        => _subscribed?.OpenSelectedStepDetail();
}
