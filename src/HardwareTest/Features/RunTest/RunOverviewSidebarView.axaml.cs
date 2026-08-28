using Avalonia.Controls;
using Avalonia.Input;

namespace HardwareTest.Features.RunTest;

public partial class RunOverviewSidebarView : UserControl
{
    public RunOverviewSidebarView()
    {
        InitializeComponent();
    }

    private void OnStageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not RunTestViewModel vm)
        {
            return;
        }

        if (vm.StepTree.SelectedStage?.Step is { } stage)
        {
            vm.StepTree.SelectedStep = stage;
            vm.OpenSelectedStepDetail();
        }
    }
}
