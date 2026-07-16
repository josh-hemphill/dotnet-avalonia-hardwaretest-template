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
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Plot.UpdateData(ys, length));
            return;
        }

        Plot.UpdateData(ys, length);
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
