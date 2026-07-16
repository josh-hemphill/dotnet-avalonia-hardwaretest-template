using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class RunTestViewModelTests
{
    private static RunTestViewModel CreateVm(
        FakeOpenTapSession? openTap = null,
        FakeReportService? reports = null,
        AppSettings? settings = null,
        OperatorSession? session = null,
        FakeRunStore? store = null)
    {
        return new RunTestViewModel(
            openTap ?? new FakeOpenTapSession(),
            session ?? new OperatorSession(),
            new FakeRunControl(),
            reports ?? new FakeReportService(),
            store ?? new FakeRunStore(),
            settings ?? new AppSettings());
    }

    [Fact]
    public async Task Run_selected_uses_selection_path()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        vm.DutSerialInput = "SN-SEL";
        await vm.ConfirmDutCommand.ExecuteAsync();
        vm.SelectedStep = Flatten(vm.Hierarchy).FirstOrDefault();
        Assert.NotNull(vm.SelectedStep);
        await vm.RunSelectedCommand.ExecuteAsync();
        Assert.Equal(1, openTap.SelectionRunCount);
        Assert.Equal(vm.SelectedStep!.Path, openTap.LastSelectionPath);
    }

    [Fact]
    public async Task Continue_operator_resumes_host()
    {
        var openTap = new FakeOpenTapSession { IsAwaitingOperator = true, OperatorPromptMessage = "Install fixture" };
        var vm = CreateVm(openTap);
        vm.IsAwaitingOperator = true;
        vm.OperatorPromptMessage = "Install fixture";
        await vm.ContinueOperatorCommand.ExecuteAsync();
        Assert.False(openTap.IsAwaitingOperator);
        Assert.False(vm.IsAwaitingOperator);
    }

    [Fact]
    public async Task Session_stamps_part_and_session_id_on_record()
    {
        var openTap = new FakeOpenTapSession();
        var store = new FakeRunStore();
        var session = new OperatorSession();
        var vm = CreateVm(openTap, store: store, session: session);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        vm.DutSerialInput = "SN-PART";
        vm.DutPartInput = "PN-1";
        vm.DutRevisionInput = "A";
        vm.OperatorInput = "Tech";
        // Sample requirements do not require part/operator; stamp via ConfirmDut fields through TryConfirm
        Assert.True(session.TryConfirm(
            new ProgramRequirements { RequireSerial = true, RequirePartNumber = true, RequireOperator = true },
            "SN-PART",
            "PN-1",
            "A",
            "Tech",
            "demo",
            out _));
        vm.ShowSessionForm = false;
        await vm.RunCommand.ExecuteAsync();
        var saved = await store.LoadAsync(vm.LastRunId!);
        Assert.Equal("SN-PART", saved!.DutSerial);
        Assert.Equal("PN-1", saved.DutPartNumber);
        Assert.Equal("A", saved.DutRevision);
        Assert.Equal("Tech", saved.OperatorName);
        Assert.False(string.IsNullOrWhiteSpace(saved.SessionId));
        Assert.NotEmpty(saved.Steps);
    }

    [Fact]
    public async Task RefreshPrograms_loads_sample()
    {
        var vm = CreateVm();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        Assert.NotEmpty(vm.Programs);
        Assert.Contains(vm.Programs, p => p.IsSample);
    }

    [Fact]
    public async Task Run_blocked_without_dut()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal(0, openTap.RunCount);
        Assert.Contains("Confirm DUT", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_after_confirm_stamps_dut_and_generates_report()
    {
        var openTap = new FakeOpenTapSession();
        var reports = new FakeReportService();
        var store = new FakeRunStore();
        var vm = CreateVm(openTap, reports, store: store);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        vm.DutSerialInput = "SN-100";
        await vm.ConfirmDutCommand.ExecuteAsync();
        await vm.RunCommand.ExecuteAsync();

        Assert.Equal(1, openTap.RunCount);
        Assert.Equal("SN-100", openTap.LastDut?.Serial);
        Assert.False(string.IsNullOrWhiteSpace(vm.LastRunId));
        Assert.Equal(1, reports.GenerateCount);
        var saved = await store.LoadAsync(vm.LastRunId!);
        Assert.Equal("SN-100", saved!.DutSerial);
    }

    [Fact]
    public async Task ChangeDut_clears_and_blocks_run()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        vm.DutSerialInput = "SN-1";
        await vm.ConfirmDutCommand.ExecuteAsync();
        await vm.ChangeDutCommand.ExecuteAsync();
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal(0, openTap.RunCount);
        Assert.True(vm.NeedsDutConfirm);
    }

    [Fact]
    public async Task Second_run_same_dut_does_not_require_reconfirm()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        vm.DutSerialInput = "SN-22";
        await vm.ConfirmDutCommand.ExecuteAsync();
        await vm.RunCommand.ExecuteAsync();
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal(2, openTap.RunCount);
    }

    [Fact]
    public async Task Cancel_during_run_marks_cancelled()
    {
        var openTap = new FakeOpenTapSession { Delay = TimeSpan.FromSeconds(5) };
        var vm = CreateVm(openTap);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        vm.DutSerialInput = "SN-C";
        await vm.ConfirmDutCommand.ExecuteAsync();
        var runTask = vm.RunCommand.ExecuteAsync();
        await Task.Delay(50);
        await vm.CancelCommand.ExecuteAsync();
        await runTask;
        Assert.Contains("Cancelled", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failed_result_still_attempts_report()
    {
        var openTap = new FakeOpenTapSession { CompletionResult = RunResult.Failed };
        var reports = new FakeReportService();
        var vm = CreateVm(openTap, reports);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        vm.DutSerialInput = "SN-F";
        await vm.ConfirmDutCommand.ExecuteAsync();
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal(1, reports.GenerateCount);
        Assert.Contains("Failed", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stale_session_blocks_until_same_dut_confirmed()
    {
        var session = new OperatorSession();
        session.ConfirmDut("SN-STALE");
        session.MarkStale();
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap, session: session);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal(0, openTap.RunCount);
        await vm.ConfirmSameDutCommand.ExecuteAsync();
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal(1, openTap.RunCount);
    }

    [Fact]
    public async Task Burst_progress_is_throttled_and_details_capped()
    {
        var settings = new AppSettings { PlotRefreshHz = 20 };
        var vm = CreateVm(settings: settings);
        vm.UiScheduler = action => action();
        vm.ShowDetails = true;
        await vm.RefreshProgramsCommand.ExecuteAsync();

        var publishRef = vm.PlotYs;
        const int burst = 500;
        for (var i = 0; i < burst; i++)
        {
            vm.IngestProgress(new OpenTapProgress
            {
                Message = $"Sample {i}",
                StepName = "Acquire",
                Sample = new MeasurementSampleEvent("VDC", i, i * 0.01, DateTimeOffset.UtcNow),
                OverallPercent = i * 100.0 / burst,
            });
        }

        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Done",
            OverallPercent = 100,
            IsCompleted = true,
        });

        await Task.Delay(250);

        Assert.True(vm.PlotUiFlushCount < burst / 4, $"PlotUiFlushCount={vm.PlotUiFlushCount} for burst={burst}");
        Assert.True(vm.DetailLines.Count <= 200);
        Assert.True(vm.PlotYsLength <= 2048);
        Assert.Same(publishRef, vm.PlotYs);
    }

    [Fact]
    public async Task ApplyDebugPatch_clamps_sample_count_and_interval()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap, settings: new AppSettings { IsEngineerDebugMode = true });
        vm.IsEngineerDebugMode = true;
        await vm.RefreshProgramsCommand.ExecuteAsync();
        vm.SelectedStep = vm.Hierarchy.Count > 0
            ? Flatten(vm.Hierarchy).FirstOrDefault()
            : null;
        if (vm.SelectedStep is null)
        {
            // Sample tree should exist after load.
            Assert.Fail("Expected hierarchy after loading sample program.");
        }

        vm.DebugSampleCount = 50_000;
        vm.DebugIntervalMs = 0;
        await vm.ApplyDebugPatchCommand.ExecuteAsync();
        Assert.Equal(4096, vm.DebugSampleCount);
        Assert.Equal(1, vm.DebugIntervalMs);
    }

    private static IEnumerable<HierarchyStepViewModel> Flatten(IEnumerable<HierarchyStepViewModel> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Flatten(root.Children))
            {
                yield return child;
            }
        }
    }
}
