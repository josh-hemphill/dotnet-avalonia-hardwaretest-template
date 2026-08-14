using System.Collections.Specialized;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class RunTestUiThreadTests
{
    [Fact]
    public async Task LoadSelectedProgram_rebuilds_hierarchy_only_via_UiScheduler()
    {
        var openTap = new FakeOpenTapSession(preloadSample: false);
        var vm = RunTestViewModelTestFactory.Create(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        Assert.NotNull(vm.ProgramSelection.SelectedProgram);

        openTap.YieldOnLoad = true;
        openTap.LoadedPlanPath = "not-the-selected-plan";

        var inScheduler = false;
        var offScheduler = 0;
        vm.UiScheduler = action =>
        {
            inScheduler = true;
            try
            {
                action();
            }
            finally
            {
                inScheduler = false;
            }
        };
        ((INotifyCollectionChanged)vm.StepTree.Hierarchy).CollectionChanged +=
            (_, _) =>
            {
                if (!inScheduler)
                {
                    Interlocked.Increment(ref offScheduler);
                }
            };

        await ((IRunBoardHost)vm).LoadSelectedProgramAsync();

        Assert.NotEmpty(vm.StepTree.Hierarchy);
        Assert.Equal(0, offScheduler);
        Assert.Equal(SampleProgramFactory.EmbeddedName, openTap.LoadedPlanPath);
    }
}
