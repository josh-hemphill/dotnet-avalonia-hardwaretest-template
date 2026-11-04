using HardwareTest.Features.RunTest;
using HardwareTest.Features.Shell;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class SessionGateNotificationTests
{
    [Fact]
    public async Task Actual_blocked_run_notice_remains_for_failed_confirmation_then_clears_after_success_without_starting()
    {
        var tap = new FakeOpenTapSession();
        var shell = new ShellNotificationViewModel();
        var vm = RunTestViewModelTestFactory.Create(tap, shellNotification: shell);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsAsync();
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Contains("Confirm DUT", shell.Message, StringComparison.Ordinal);
        var blocked = shell.Message;
        vm.SessionPanel.DutSerialInput = "SESSION-GATE-DUT";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.True(vm.SessionPanel.SessionBlocked);
        Assert.Equal(blocked, shell.Message);
        vm.SessionPanel.OperatorInput = "Confirmed technician";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(vm.SessionPanel.SessionBlocked);
        Assert.True(vm.CanStartRun);
        Assert.False(shell.HasContent);
        Assert.False(vm.HasBanner);
        Assert.Empty(vm.BannerMessage);
        Assert.Equal(0, tap.RunCount);
    }

    [Theory]
    [InlineData(ShellNotificationViewModel.SourceStorage, ShellNotificationSeverity.Critical)]
    [InlineData(ShellNotificationViewModel.SourceHistory, ShellNotificationSeverity.Warning)]
    [InlineData(ShellNotificationViewModel.SourceRun, ShellNotificationSeverity.Error)]
    public async Task Confirmation_preserves_a_newer_or_other_source_shell_notice(string source, ShellNotificationSeverity severity)
    {
        var shell = new ShellNotificationViewModel();
        var vm = RunTestViewModelTestFactory.Create(shellNotification: shell);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsAsync();
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Contains("Confirm DUT", shell.Message, StringComparison.Ordinal);
        shell.Publish(severity, "Keep this current alert.", dismissible: false, sourceKey: source);
        vm.SessionPanel.DutSerialInput = "PRESERVE-ALERT-DUT";
        vm.SessionPanel.OperatorInput = "Technician";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.True(vm.CanStartRun);
        Assert.True(shell.HasContent);
        Assert.Equal("Keep this current alert.", shell.Message);
        Assert.Equal(severity, shell.Severity);
        Assert.False(shell.IsDismissible);
        Assert.False(vm.HasBanner);
    }

    [Fact]
    public async Task Confirmation_preserves_a_new_run_warning_in_both_local_and_shell_state()
    {
        var shell = new ShellNotificationViewModel();
        var vm = RunTestViewModelTestFactory.Create(shellNotification: shell);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsAsync();
        await vm.Run.RunCommand.ExecuteAsync();
        vm.SetBanner(RunBannerSeverity.Warning, "Station check is near expiry.");
        vm.SessionPanel.DutSerialInput = "NEW-WARNING-DUT";
        vm.SessionPanel.OperatorInput = "Technician";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.True(vm.CanStartRun);
        Assert.True(vm.HasBanner);
        Assert.Equal("Station check is near expiry.", vm.BannerMessage);
        Assert.Equal(vm.BannerMessage, shell.Message);
        Assert.Equal(ShellNotificationSeverity.Warning, shell.Severity);
    }
}
