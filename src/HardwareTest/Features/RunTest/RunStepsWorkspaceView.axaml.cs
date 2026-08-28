using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace HardwareTest.Features.RunTest;

public partial class RunStepsWorkspaceView : UserControl
{
    private RunTestViewModel? _subscribed;

    public RunStepsWorkspaceView()
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
        vm.StepTree.RequestScrollToSelectedStep += OnRequestScrollToSelectedStep;
        vm.StepTree.RequestFocusStepSearch += OnRequestFocusStepSearch;
    }

    private void Unsubscribe()
    {
        if (_subscribed is null)
        {
            return;
        }

        _subscribed.StepTree.RequestScrollToSelectedStep -= OnRequestScrollToSelectedStep;
        _subscribed.StepTree.RequestFocusStepSearch -= OnRequestFocusStepSearch;
        _subscribed = null;
    }

    private void OnRequestFocusStepSearch(object? sender, EventArgs e) => StepSearchBox?.Focus();

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

        Dispatcher.UIThread.Post(Scroll, DispatcherPriority.Loaded);
    }

    private void OnHierarchyDoubleTapped(object? sender, TappedEventArgs e)
        => _subscribed?.OpenSelectedStepDetail();
}
