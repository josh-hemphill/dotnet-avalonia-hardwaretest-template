using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class PlanInitializationEnvironmentRefreshTests
{
    [AvaloniaTheory]
    [InlineData("current")]
    [InlineData("stale-owner")]
    [InlineData("closed")]
    public async Task Pending_environment_refresh_updates_open_hardware_choices_without_replacing_form_inputs(string state)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        await vm.WaitForEnvironmentInspectionAsync();
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new AuthoringInstrumentAdapter("InstrumentComponents.OpenTap.DelayedDevice", "Delayed device",
            "InstrumentComponents.OpenTap", "InstrumentComponents.OpenTap.dll", [], [], [], true, true,
            _ => throw new NotSupportedException(), _ => throw new NotSupportedException());
        var mock = AuthoringInstrumentCatalog.All.Single(item => item.DisplayName == "Mock DMM");
        vm.EnvironmentInspector = (_, _) =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Environment test did not release worker.");
            return AuthoringEnvironmentInspection.Empty("ready") with { Choices = [adapter, mock] };
        };
        var refresh = vm.RefreshEnvironmentInspectionAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var ownerCurrent = true;
        var dialog = new PlanInitializationWindow(vm, ownerIsCurrent: () => ownerCurrent && window.IsVisible);
        dialog.Show(window);
        try
        {
            fixture.Control<TextBox>("Plan display name", dialog).Text = "Retained plan name";
            fixture.Control<TextBox>("Stable plan ID", dialog).Text = "retained-plan";
            AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
            AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
            var picker = fixture.Control<ComboBox>("Hardware choice", dialog);
            picker.SelectedItem = picker.Items.Single(item => item?.ToString()?.StartsWith("Create Mock DMM", StringComparison.Ordinal) == true);
            fixture.Control<TextBox>("Instrument slot", dialog).Text = "Typed slot";
            fixture.Control<TextBox>("Instrument address", dialog).Text = "MOCK::TYPED";
            fixture.Control<TextBox>("Instrument I/O timeout ms", dialog).Text = "8173";
            var before = dialog.CaptureGuidedForm();
            Assert.DoesNotContain(picker.Items, item => item?.ToString()?.Contains("Delayed device", StringComparison.Ordinal) == true);
            if (state == "stale-owner") ownerCurrent = false;
            if (state == "closed") dialog.Close();
            release.Set();
            await refresh.WaitAsync(TimeSpan.FromSeconds(15));
            AuthoringUiFixture.Drain();
            Assert.Equal(state == "current", picker.Items.Any(item => item?.ToString()?.Contains("Delayed device", StringComparison.Ordinal) == true));
            var after = dialog.CaptureGuidedForm();
            Assert.Equal(before.Text, after.Text);
            Assert.Equal(before.Toggles, after.Toggles);
            Assert.Equal(before.TimeoutText, after.TimeoutText);
            Assert.Equal(before.HardwareTypeId, after.HardwareTypeId);
            Assert.Equal(before.AutomaticSlot, after.AutomaticSlot);
            Assert.Equal(before.AutomaticDemoAddress, after.AutomaticDemoAddress);
        }
        finally
        {
            release.Set();
            await refresh.WaitAsync(TimeSpan.FromSeconds(15));
            if (dialog.IsVisible) dialog.Close();
        }
    }
}
