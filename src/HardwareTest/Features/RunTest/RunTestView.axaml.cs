using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public partial class RunTestView : UserControl
{
    private const int StepListRowIndex = 4;
    private const int DetailDrawerRowIndex = 6;

    private RunTestViewModel? _subscribed;
    private readonly SerialDisposable _chromeLayout = new();

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
            vm.Live.PlotDataChanged += OnPlotDataChanged;
            vm.StepTree.RequestScrollToSelectedStep += OnRequestScrollToSelectedStep;
            vm.StepTree.RequestFocusStepSearch += OnRequestFocusStepSearch;
            vm.ProgramSelection.RequestPlanFilePath = PickPlanFileAsync;
            // Detail open/close (and Focus toggle) must reset list+drawer star rows: GridSplitter
            // converts * to Absolute, and leaving Absolute after Hide/Show breaks the drawer.
            _chromeLayout.Disposable = Observable.CombineLatest(
                    vm.Live.WhenAnyValue(x => x.ShowFocusTrend),
                    vm.StepDetail.WhenAnyValue(x => x.ShowDetailRegion),
                    (_, showDetails) => showDetails)
                .Subscribe(showDetails =>
                {
                    if (Dispatcher.UIThread.CheckAccess())
                    {
                        ApplyDetailDrawerRows(showDetails);
                        return;
                    }

                    Dispatcher.UIThread.Post(() => ApplyDetailDrawerRows(showDetails));
                });
            ApplyDetailDrawerRows(vm.StepDetail.ShowDetailRegion);
            if (Plot is not null)
            {
                Plot.UpdateData(vm.Live.PlotYs, vm.Live.PlotYsLength, force: true);
            }
        }
    }

    private void Unsubscribe()
    {
        _chromeLayout.Disposable = null;
        if (_subscribed is null)
        {
            return;
        }

        _subscribed.Live.PlotDataChanged -= OnPlotDataChanged;
        _subscribed.StepTree.RequestScrollToSelectedStep -= OnRequestScrollToSelectedStep;
        _subscribed.StepTree.RequestFocusStepSearch -= OnRequestFocusStepSearch;
        _subscribed.ProgramSelection.RequestPlanFilePath = null;
        _subscribed = null;
    }

    /// Restores step-list and detail-drawer star shares after Details/Focus chrome changes.
    private void ApplyDetailDrawerRows(bool showDetails)
    {
        if (BoardGrid is null || BoardGrid.RowDefinitions.Count <= DetailDrawerRowIndex)
        {
            return;
        }

        BoardGrid.RowDefinitions[StepListRowIndex].Height = new GridLength(1, GridUnitType.Star);
        BoardGrid.RowDefinitions[DetailDrawerRowIndex].Height = showDetails
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
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

        var live = _subscribed.Live;
        var ys = live.PlotYs;
        var length = live.PlotYsLength;
        var title = live.PlotTitle;
        var yLabel = live.PlotYLabel;
        var legend = live.PlotLegendText;
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
            ((ICommand)_subscribed.StepTree.FocusStepSearchCommand).Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.F)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ((ICommand)_subscribed.StepTree.PrevFailCommand).Execute(null);
        }
        else
        {
            ((ICommand)_subscribed.StepTree.NextFailCommand).Execute(null);
        }

        e.Handled = true;
    }

    private void OnStageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_subscribed?.StepTree.SelectedStage?.Step is { } stage)
        {
            _subscribed.StepTree.SelectedStep = stage;
            _subscribed.OpenSelectedStepDetail();
        }
    }

    private void OnHierarchyDoubleTapped(object? sender, TappedEventArgs e)
        => _subscribed?.OpenSelectedStepDetail();
}
