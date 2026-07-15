using Avalonia.Controls;
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
            vm.RequestSuiteFilePath = PickSuiteFileAsync;
            if (Plot is not null)
            {
                Plot.UpdateData(vm.PlotYs);
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
        _subscribed.RequestSuiteFilePath = null;
        _subscribed = null;
    }

    private async Task<string?> PickSuiteFileAsync(CancellationToken cancellationToken)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return null;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open test suite",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Suite JSON") { Patterns = ["*.json"] },
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

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Plot.UpdateData(_subscribed.PlotYs));
            return;
        }

        Plot.UpdateData(_subscribed.PlotYs);
    }
}
