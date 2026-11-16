using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class OperatorContextTests
{
    [Fact]
    public void Displayed_visa_mode_uses_effective_controller_and_updates_after_a_swap()
    {
        var requested = new AppSettings { UseMockVisa = false };
        var controller = new ContextVisaModeController { EffectiveUseMockVisa = true };
        var tap = new FakeOpenTapSession();
        var vm = new RunTestViewModel(tap, tap, tap, new OperatorSession(), new FakeRunControl(),
            new FakeReportService(), new FakeRunStore(), requested, visaModeController: controller)
        { UiScheduler = action => action() };
        Assert.Equal("VISA: simulated", vm.VisaModeText);
        Assert.Contains("other equipment", vm.VisaModeHelpText, StringComparison.Ordinal);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        controller.Apply(false);
        Assert.Equal("VISA: physical", vm.VisaModeText);
        Assert.Contains(nameof(vm.VisaModeText), changes);
        Assert.Contains(nameof(vm.VisaModeHelpText), changes);
    }

    [Fact]
    public void Saved_result_action_requires_a_persisted_record_and_is_hidden_during_execution_or_after_clear()
    {
        var vm = RunTestViewModelTestFactory.Create();
        Assert.False(vm.ShowSavedResult);
        vm.LastRunId = "not-yet-saved";
        Assert.False(vm.ShowSavedResult);
        vm.MarkLastRunSaved("not-yet-saved");
        Assert.True(vm.ShowSavedResult);
        vm.IsRunning = true;
        Assert.False(vm.ShowSavedResult);
        vm.IsRunning = false;
        vm.LastRunId = "another-unsaved-record";
        Assert.False(vm.ShowSavedResult);
        vm.LastRunId = null;
        Assert.False(vm.ShowSavedResult);
    }

    [Fact]
    public void Program_identity_distinguishes_equal_names_and_exposes_full_source_without_abbreviating_the_id()
    {
        var first = new ProgramItemViewModel { Id = "production-A", DisplayName = "Production test", Path = "/plans/A.TapPlan" };
        var second = new ProgramItemViewModel { Id = "production-B", DisplayName = first.DisplayName, Path = "/plans/B.TapPlan" };
        Assert.NotEqual(first.SelectionIdentity, second.SelectionIdentity);
        Assert.Contains(second.Id, second.SelectionIdentity, StringComparison.Ordinal);
        Assert.Contains(second.Id, second.DetailsText, StringComparison.Ordinal);
        Assert.Contains(second.Path, second.DetailsText, StringComparison.Ordinal);
    }

    private sealed class ContextVisaModeController : IVisaModeController
    {
        public bool EffectiveUseMockVisa { get; set; }
        public event EventHandler? ModeApplied;
        public void Apply(bool mock)
        {
            EffectiveUseMockVisa = mock;
            ModeApplied?.Invoke(this, EventArgs.Empty);
        }

        public bool TryApply(bool wantMock, out string statusMessage)
        {
            Apply(wantMock);
            statusMessage = "Applied.";
            return true;
        }
    }
}
