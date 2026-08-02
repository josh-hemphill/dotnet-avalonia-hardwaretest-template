using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Settings;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Phase 13: SettingsViewModel + IVisaModeController live-semantics tests.
public sealed class VisaModeSettingsViewModelTests
{
    // ── Successful flip: mock → real ───────────────────────────────────────

    [Fact]
    public async Task Save_with_UseMockVisa_flip_applies_when_idle()
    {
        var store = new FakeSettingsStore();
        store.AppSettings.UseMockVisa = true;
        var controller = new FakeVisaModeController(initialMock: true);
        var vm = new SettingsViewModel(
            store,
            new FakeOpenTapSession(),
            visaModeController: controller)
        {
            UseMockVisa = false,
        };

        await vm.SaveCommand.ExecuteAsync();

        Assert.False(controller.EffectiveUseMockVisa, "controller must reflect new mode");
        Assert.False(store.AppSettings.UseMockVisa, "settings persisted as real");
        Assert.False(vm.UseMockVisa, "checkbox stays at requested value after apply");
        Assert.Contains("Applied", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    // ── Refused flip: run active ───────────────────────────────────────────

    [Fact]
    public async Task Save_with_UseMockVisa_flip_refused_while_run_active()
    {
        var store = new FakeSettingsStore();
        store.AppSettings.UseMockVisa = true;
        var controller = new FakeVisaModeController(
            initialMock: true,
            refuseNextApply: true,
            refuseMessage: "Cannot switch VISA mode while a run is active. Finish or safety-stop the run, then save again.");
        var vm = new SettingsViewModel(
            store,
            new FakeOpenTapSession(),
            visaModeController: controller)
        {
            UseMockVisa = false,
        };

        await vm.SaveCommand.ExecuteAsync();

        Assert.True(controller.EffectiveUseMockVisa, "controller must NOT change effective mode");
        Assert.True(store.AppSettings.UseMockVisa, "settings file reverted to effective");
        Assert.True(vm.UseMockVisa, "checkbox reverted to effective mode");
        Assert.Contains("run", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Saved at", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    // ── No controller: legacy fallback ─────────────────────────────────────

    [Fact]
    public async Task Save_without_controller_still_writes_UseMockVisa()
    {
        var store = new FakeSettingsStore();
        store.AppSettings.UseMockVisa = true;
        var vm = new SettingsViewModel(store, new FakeOpenTapSession())
        {
            UseMockVisa = false,
        };

        await vm.SaveCommand.ExecuteAsync();

        Assert.False(store.AppSettings.UseMockVisa);
        Assert.Contains("Saved", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    // ── Status: no "may be required" when VISA applied ─────────────────────

    [Fact]
    public async Task Status_does_not_say_restart_required_when_visa_applied()
    {
        var store = new FakeSettingsStore();
        store.AppSettings.UseMockVisa = false;
        var controller = new FakeVisaModeController(initialMock: false);
        var vm = new SettingsViewModel(
            store,
            new FakeOpenTapSession(),
            visaModeController: controller)
        {
            UseMockVisa = true,
        };

        await vm.SaveCommand.ExecuteAsync();

        Assert.DoesNotContain("restart", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    // ── Env override: TryApply not called ─────────────────────────────────

    [Fact]
    public async Task Save_with_env_override_does_not_call_TryApply()
    {
        var store = new FakeSettingsStore
        {
            Provenance =
            [
                new SettingProvenance
                {
                    Key = "UseMockVisa",
                    EffectiveValue = "true",
                    Source = SettingSource.Environment,
                    SourceDetail = "HARDWARETEST_USE_MOCK_VISA",
                },
            ],
        };
        store.AppSettings.UseMockVisa = true;
        var controller = new FakeVisaModeController(initialMock: true);
        var vm = new SettingsViewModel(
            store,
            new FakeOpenTapSession(),
            visaModeController: controller)
        {
            UseMockVisa = false,
        };

        Assert.True(vm.UseMockVisaReadOnly);
        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(0, controller.TryApplyCallCount);
        Assert.True(controller.EffectiveUseMockVisa, "env override must not be touched");
    }

    // ── Regression: factory and VM checkbox agree after refuse ─────────────

    [Fact]
    public async Task After_refuse_checkbox_reflects_effective_not_desired()
    {
        var store = new FakeSettingsStore();
        store.AppSettings.UseMockVisa = true;
        var controller = new FakeVisaModeController(initialMock: true, refuseNextApply: true);
        var vm = new SettingsViewModel(
            store,
            new FakeOpenTapSession(),
            visaModeController: controller)
        {
            UseMockVisa = false,
        };

        await vm.SaveCommand.ExecuteAsync();

        // Checkbox and effective mode must agree — no split brain
        Assert.Equal(controller.EffectiveUseMockVisa, vm.UseMockVisa);
        Assert.Equal(controller.EffectiveUseMockVisa, store.AppSettings.UseMockVisa);
    }
}

/// Controllable fake for IVisaModeController used in ViewModel tests.
file sealed class FakeVisaModeController : IVisaModeController
{
    private bool _effective;
    private readonly bool _refuseNextApply;
    private readonly string _refuseMessage;

    public FakeVisaModeController(
        bool initialMock,
        bool refuseNextApply = false,
        string refuseMessage = "Refused.")
    {
        _effective = initialMock;
        _refuseNextApply = refuseNextApply;
        _refuseMessage = refuseMessage;
    }

    public bool EffectiveUseMockVisa => _effective;
    public int TryApplyCallCount { get; private set; }

    public event EventHandler? ModeApplied;

    public bool TryApply(bool wantMock, out string statusMessage)
    {
        TryApplyCallCount++;
        if (_refuseNextApply)
        {
            statusMessage = _refuseMessage;
            return false;
        }

        _effective = wantMock;
        statusMessage = wantMock
            ? "Mock VISA applied — Instruments will reflect mock resources."
            : "Real VISA applied — Instruments will discover hardware resources.";
        ModeApplied?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
