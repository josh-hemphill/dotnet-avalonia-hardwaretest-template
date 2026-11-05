using System.Globalization;
using Avalonia.Media;
using Avalonia.Styling;
using HardwareTest.Core.Presentation;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Shell;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class RunNotificationContinuityTests
{
    [Fact]
    public async Task Starting_the_next_run_clears_its_previous_completion_before_operator_input()
    {
        var openTap = new FakeOpenTapSession();
        var shell = new ShellNotificationViewModel();
        var vm = RunTestViewModelTestFactory.Create(openTap, shellNotification: shell);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        vm.SessionPanel.DutSerialInput = "CONTINUITY-DUT";
        vm.SessionPanel.OperatorInput = "Continuity technician";
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Contains("Suite finished: Passed", shell.Message);
        var previousCompletion = shell.Message;
        openTap.EmitOperatorInteractionDuringRun = true;
        var nextRun = vm.Run.RunCommand.ExecuteAsync();
        try
        {
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (!vm.Interaction.IsAwaitingOperator && !nextRun.IsCompleted && DateTime.UtcNow < timeout)
                await Task.Delay(10);
            Assert.True(vm.IsRunning);
            Assert.True(vm.Interaction.IsAwaitingOperator);
            Assert.NotEqual(previousCompletion, shell.Message);
            Assert.DoesNotContain("Suite finished", shell.Message);
            await vm.ContinueOperatorCommand.ExecuteAsync();
            await nextRun.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (!nextRun.IsCompleted)
            {
                await vm.Run.CancelCommand.ExecuteAsync();
                await nextRun.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Theory]
    [InlineData(ShellNotificationViewModel.SourceStorage, ShellNotificationSeverity.Critical)]
    [InlineData(ShellNotificationViewModel.SourceHistory, ShellNotificationSeverity.Warning)]
    [InlineData(ShellNotificationViewModel.SourceRun, ShellNotificationSeverity.Error)]
    public void New_run_preserves_newer_or_other_source_notices(string source, ShellNotificationSeverity severity)
    {
        var shell = new ShellNotificationViewModel();
        var vm = RunTestViewModelTestFactory.Create(shellNotification: shell);
        vm.SetRunCompletionBanner(RunBannerSeverity.Info, "Previous run finished.");
        shell.Publish(severity, "Keep this current alert.", dismissible: false, sourceKey: source);
        vm.BeginRunNotifications();
        Assert.True(shell.HasContent);
        Assert.Equal("Keep this current alert.", shell.Message);
        Assert.Equal(severity, shell.Severity);
        Assert.False(shell.IsDismissible);
    }

    [Fact]
    public void Newly_published_start_warning_replaces_completion_and_remains_visible()
    {
        var shell = new ShellNotificationViewModel();
        var vm = RunTestViewModelTestFactory.Create(shellNotification: shell);
        vm.SetRunCompletionBanner(RunBannerSeverity.Info, "Previous run finished.");
        vm.SetBanner(RunBannerSeverity.Warning, "Station check is near expiry.");
        vm.BeginRunNotifications();
        Assert.True(shell.HasContent);
        Assert.Equal("Station check is near expiry.", shell.Message);
        Assert.Equal(ShellNotificationSeverity.Warning, shell.Severity);
    }

    [Theory]
    [InlineData("Pass")]
    [InlineData("Fail")]
    [InlineData("Running")]
    [InlineData("Pending")]
    public void Active_filter_banner_retains_text_contrast_after_a_theme_change(string filter)
    {
        IBrush Fill(ThemeVariant theme) => (IBrush)ChipBrushConverter.BannerFillForTheme.Convert(
            new object?[] { filter, theme }, typeof(IBrush), null, CultureInfo.InvariantCulture)!;
        var light = Assert.IsAssignableFrom<ISolidColorBrush>(Fill(ThemeVariant.Light)).Color;
        var dark = Assert.IsAssignableFrom<ISolidColorBrush>(Fill(ThemeVariant.Dark)).Color;
        Assert.NotEqual(light, dark);
        Assert.True(ContrastMath.Ratio(light.R, light.G, light.B, 24, 24, 24) >= 4.5);
        Assert.True(ContrastMath.Ratio(dark.R, dark.G, dark.B, 243, 243, 243) >= 4.5);
    }
}
