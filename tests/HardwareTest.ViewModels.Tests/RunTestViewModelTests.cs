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

    private static async Task ConfirmReadyAsync(RunTestViewModel vm, string serial = "SN-1", string tech = "Tech")
    {
        vm.DutSerialInput = serial;
        vm.OperatorInput = tech;
        await vm.ConfirmDutCommand.ExecuteAsync();
    }

    [Fact]
    public async Task Run_selected_uses_selection_path()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-SEL");
        var leaf = Flatten(vm.Hierarchy).First(s => s.Children.Count == 0);
        vm.SelectedStep = leaf;
        await vm.RunSelectedCommand.ExecuteAsync();
        Assert.Equal(1, openTap.SelectionRunCount);
        Assert.Equal(leaf.Path, openTap.LastSelectionPath);
        Assert.Contains("Attempt #", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Results", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_selected_refuses_entire_program_root()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-ROOT");
        vm.SelectedStep = vm.Hierarchy[0];
        await vm.RunSelectedCommand.ExecuteAsync();
        Assert.Equal(0, openTap.SelectionRunCount);
        Assert.Contains("entire program", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Full_run_updates_hierarchy_status_from_summary()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-STATUS");
        await vm.RunCommand.ExecuteAsync();

        var leaf = Flatten(vm.Hierarchy).First(s => s.Children.Count == 0);
        Assert.NotEqual("Pending", leaf.StatusText);
        Assert.Equal("Pass", leaf.StatusText);
        Assert.Contains(vm.DetailKeyValues, line => line.Contains("Status:", StringComparison.Ordinal)
            && !line.Contains("Pending", StringComparison.Ordinal));
        var stageWithStep = vm.Stages.FirstOrDefault(s => s.Step is not null);
        Assert.NotNull(stageWithStep);
        Assert.NotEqual("Pending", stageWithStep!.StatusText);
    }

    [Fact]
    public async Task Run_selected_twice_keeps_hierarchy_instances_and_count()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-STABLE");
        var leaf = Flatten(vm.Hierarchy).First(s => s.Children.Count == 0);
        vm.SelectedStep = leaf;
        var rootBefore = vm.Hierarchy[0];
        var hierarchyCount = vm.Hierarchy.Count;
        var fullCount = Flatten(vm.Hierarchy).Count();

        await vm.RunSelectedCommand.ExecuteAsync();
        await vm.RunSelectedCommand.ExecuteAsync();

        Assert.Equal(2, openTap.SelectionRunCount);
        Assert.Equal(leaf.Path, openTap.LastSelectionPath);
        Assert.Equal(hierarchyCount, vm.Hierarchy.Count);
        Assert.Equal(fullCount, Flatten(vm.Hierarchy).Count());
        Assert.Same(rootBefore, vm.Hierarchy[0]);
        Assert.Equal(leaf.Path, vm.SelectedStep?.Path);
    }

    [Fact]
    public async Task Continue_clears_pending_await_so_flush_does_not_revive()
    {
        var vm = CreateVm();
        vm.UiScheduler = action => action();
        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Install fixture",
            AwaitingOperator = true,
            OperatorPromptMessage = "Install fixture",
            OverallPercent = 10,
        });
        await Task.Delay(50);
        Assert.True(vm.IsAwaitingOperator);

        await vm.ContinueOperatorCommand.ExecuteAsync();
        Assert.False(vm.IsAwaitingOperator);

        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Still running",
            OverallPercent = 20,
        });
        await Task.Delay(50);
        Assert.False(vm.IsAwaitingOperator);
    }

    [Fact]
    public void AppendDetailLines_respects_max_per_flush()
    {
        var vm = CreateVm();
        var lines = Enumerable.Range(0, 80).Select(i => $"line {i}").ToList();
        var added = vm.AppendDetailLines(lines, 16);
        Assert.Equal(16, added);
        Assert.Equal(16, vm.DetailLines.Count);
        Assert.Equal("line 0", vm.DetailLines[0]);
        Assert.Equal("line 15", vm.DetailLines[^1]);
    }

    [Fact]
    public async Task Plot_visibility_is_exact_step_path_only()
    {
        var vm = CreateVm(settings: new AppSettings { PlotRefreshHz = 60 });
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        const string acquirePath = "Sample Hardware Suite/Acquire VDC";
        for (var i = 0; i < 5; i++)
        {
            vm.IngestProgress(new OpenTapProgress
            {
                Message = $"Sample {i}",
                StepName = "Acquire VDC",
                StepPath = acquirePath,
                Sample = new MeasurementSampleEvent("VDC", i, i * 0.01, DateTimeOffset.UtcNow),
                OverallPercent = i * 10,
            });
        }

        await Task.Delay(50);
        Assert.True(vm.HasPlotData);

        vm.SelectedStep = vm.Hierarchy[0];
        Assert.False(vm.ShowPlotForSelection);

        var identity = Flatten(vm.Hierarchy).First(s =>
            s.Children.Count == 0 && s.Path.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        vm.SelectedStep = identity;
        Assert.False(vm.ShowPlotForSelection);

        var acquire = Flatten(vm.Hierarchy).First(s =>
            string.Equals(s.Path, acquirePath, StringComparison.OrdinalIgnoreCase));
        vm.SelectedStep = acquire;
        Assert.True(vm.ShowPlotForSelection);
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
        vm.SessionBlocked = false;
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
        await ConfirmReadyAsync(vm, "SN-100");
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
        await ConfirmReadyAsync(vm, "SN-1");
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
        await ConfirmReadyAsync(vm, "SN-22");
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
        await ConfirmReadyAsync(vm, "SN-C");
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
        await ConfirmReadyAsync(vm, "SN-F");
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal(1, reports.GenerateCount);
        Assert.Contains("Failed", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stale_session_blocks_until_same_dut_confirmed()
    {
        var session = new OperatorSession();
        session.ConfirmDut("SN-STALE");
        session.OperatorName = "Tech";
        session.MarkStale();
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap, session: session);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        vm.OperatorInput = "Tech";
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal(0, openTap.RunCount);
        await vm.ConfirmSameDutCommand.ExecuteAsync();
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal(1, openTap.RunCount);
    }

    [Fact]
    public async Task Run_selected_records_attempt_counts()
    {
        var openTap = new FakeOpenTapSession();
        var store = new FakeRunStore();
        var vm = CreateVm(openTap, store: store);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-ATT");
        vm.SelectedStep = Flatten(vm.Hierarchy).First(s => s.Children.Count == 0);
        Assert.NotNull(vm.SelectedStep);
        await vm.RunSelectedCommand.ExecuteAsync();
        await vm.RunSelectedCommand.ExecuteAsync();
        var saved = await store.LoadAsync(vm.LastRunId!);
        Assert.NotNull(saved);
        Assert.NotEmpty(saved!.StepAttempts);
        Assert.Contains(saved.StepAttempts, a => a.AttemptCount >= 1);
    }

    [Fact]
    public async Task Session_blocked_until_confirmed()
    {
        var vm = CreateVm();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        Assert.True(vm.SessionBlocked);
        await ConfirmReadyAsync(vm);
        Assert.False(vm.SessionBlocked);
    }

    [Fact]
    public async Task Burst_progress_is_throttled_and_details_capped()
    {
        var settings = new AppSettings { PlotRefreshHz = 20 };
        var vm = CreateVm(settings: settings);
        vm.UiScheduler = action => action();
        vm.ShowLiveLog = true;
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

    [Fact]
    public void Rollup_pass_pending_sibling_is_pending()
    {
        var parent = new HierarchyStepViewModel(new OpenTapStepNode
        {
            Id = "p",
            Name = "Parent",
            Path = "Parent",
            IsStage = true,
            Children =
            [
                new() { Id = "a", Name = "A", Path = "Parent/A" },
                new() { Id = "b", Name = "B", Path = "Parent/B" },
            ],
        });
        parent.Children[0].StatusText = "Pass";
        parent.Children[0].Verdict = "Pass";
        parent.Children[1].StatusText = "Pending";
        parent.Children[1].Verdict = "NotSet";
        HierarchyRollup.Apply([parent]);
        Assert.Equal("Pending", parent.StatusText);
        Assert.Equal("Pending", parent.ChipText);
        Assert.Equal("1/2", parent.ProgressText);
    }

    [Fact]
    public void Rollup_all_pass_is_pass_any_fail_is_fail()
    {
        var parent = new HierarchyStepViewModel(new OpenTapStepNode
        {
            Id = "p",
            Name = "Parent",
            Path = "Parent",
            IsStage = true,
            Children =
            [
                new() { Id = "a", Name = "A", Path = "Parent/A" },
                new() { Id = "b", Name = "B", Path = "Parent/B" },
            ],
        });
        parent.Children[0].StatusText = "Pass";
        parent.Children[1].StatusText = "Pass";
        HierarchyRollup.Apply([parent]);
        Assert.Equal("Pass", parent.StatusText);

        parent.Children[1].StatusText = "Fail";
        HierarchyRollup.Apply([parent]);
        Assert.Equal("Fail", parent.StatusText);
        Assert.Equal("Fail", parent.ChipText);
        Assert.Equal("2/2 · 1F", parent.ProgressText);
        Assert.Equal("Fail", parent.Node.StatusText);
    }

    [Fact]
    public void Rollup_writes_status_to_host_node()
    {
        var parent = new HierarchyStepViewModel(new OpenTapStepNode
        {
            Id = "p",
            Name = "Parent",
            Path = "Parent",
            IsStage = true,
            Children =
            [
                new() { Id = "a", Name = "A", Path = "Parent/A" },
                new() { Id = "b", Name = "B", Path = "Parent/B" },
            ],
        });
        parent.Children[0].StatusText = "Pass";
        parent.Children[1].StatusText = "Pass";
        HierarchyRollup.Apply([parent]);
        Assert.Equal("Pass", parent.Node.StatusText);
        Assert.Equal("Pass", parent.Children[0].Node.StatusText);
    }

    [Fact]
    public async Task Step_filter_and_search_narrow_StepRows()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-FILT");
        await vm.RunCommand.ExecuteAsync();

        vm.StepStatusFilter = StepStatusFilter.Fail;
        Assert.Empty(vm.StepRows);

        vm.StepStatusFilter = StepStatusFilter.All;
        vm.StepSearchText = "Acquire";
        Assert.Contains(vm.StepRows, r => r.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.StepRows, r => r.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NextFail_selects_failed_leaf()
    {
        var openTap = new FakeOpenTapSession { CompletionResult = RunResult.Failed };
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-NF");
        await vm.RunCommand.ExecuteAsync();

        await vm.NextFailCommand.ExecuteAsync();
        Assert.NotNull(vm.SelectedStep);
        Assert.Equal("Fail", StatusChip.FromStatus(vm.SelectedStep!.StatusText, vm.SelectedStep.Verdict));
    }

    [Fact]
    public async Task Inspect_refresh_matches_run_pass_after_rollup()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-INSP");
        await vm.RunCommand.ExecuteAsync();

        var inspect = new HardwareTest.Features.Inspect.InspectViewModel(openTap);
        inspect.Refresh();
        var identity = Flatten(inspect.Hierarchy).First(s => s.Name == "Identity" || s.Path.EndsWith("/Identity", StringComparison.Ordinal));
        Assert.Equal("Pass", identity.ChipText);
    }

    [Fact]
    public async Task ApplySelectionFromInspect_selects_leaf_on_run()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        const string path = "Sample Hardware Suite/Acquire VDC";
        vm.ApplySelectionFromInspect(path);
        Assert.Equal(path, vm.SelectedStep?.Path);
    }

    [Fact]
    public async Task Suite_summary_counts_after_run()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-SUM");
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal(2, vm.SuitePassedCount);
        Assert.Equal(0, vm.SuiteFailedCount);
    }

    [Fact]
    public async Task Board_demo_program_exposes_nested_sections()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.RefreshProgramsCommand.ExecuteAsync();
        Assert.Contains(vm.Programs, p => p.Path == BoardDemoProgramFactory.EmbeddedName);
        vm.SelectedProgram = vm.Programs.First(p => p.Path == BoardDemoProgramFactory.EmbeddedName);
        for (var i = 0; i < 40 && vm.Stages.All(s => s.DisplayName != "Power Rails"); i++)
        {
            await Task.Delay(25);
        }

        var power = vm.Stages.FirstOrDefault(s => s.DisplayName == "Power Rails");
        Assert.NotNull(power);
        vm.SelectedStage = power;
        Assert.True(vm.HasSubsections);
        Assert.Contains(vm.Subsections, s => s.DisplayName == "3V3 Rail");
        vm.SelectedSubsection = vm.Subsections.First(s => s.DisplayName == "3V3 Rail");
        Assert.Contains(vm.StepRows, r => r.Name.Contains("Acquire 3V3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stage_progress_includes_fail_suffix()
    {
        var openTap = new FakeOpenTapSession { CompletionResult = RunResult.Failed };
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-PF");
        await vm.RunCommand.ExecuteAsync();
        var entire = vm.Stages.First(s => s.Step is null);
        Assert.Contains("F", entire.ProgressText, StringComparison.Ordinal);
        Assert.Equal("Fail", entire.ChipText);
    }
    [Fact]
    public async Task StepRows_are_leaves_under_selected_stage()
    {
        var vm = CreateVm();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        var identity = vm.Stages.First(s => s.DisplayName == "Identity");
        vm.SelectedStage = identity;
        Assert.All(vm.StepRows, r => Assert.Empty(r.Children));
        Assert.Contains(vm.StepRows, r => r.Name == "Identity Check");
        Assert.DoesNotContain(vm.StepRows, r => r.Name == "Acquire VDC");
    }

    [Fact]
    public async Task Stage_progress_text_is_completed_over_total()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        var entire = vm.Stages.First(s => s.Step is null);
        Assert.Equal("0/2", entire.ProgressText);
        await ConfirmReadyAsync(vm, "SN-PROG");
        await vm.RunCommand.ExecuteAsync();
        Assert.Equal("2/2", entire.ProgressText);
        Assert.Equal("Pass", entire.ChipText);
    }

    [Fact]
    public async Task Hero_follows_progress_step_while_running()
    {
        var vm = CreateVm();
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        vm.IsRunning = true;
        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Working",
            StepName = "Acquire VDC",
            StepPath = "Sample Hardware Suite/Acquire VDC",
            StatusText = "Running",
            OverallPercent = 40,
        });
        Assert.Equal("CURRENT:", vm.HeroLabel);
        Assert.Equal("Acquire VDC", vm.HeroStepName);
        Assert.Equal("Running", vm.HeroChipText);
        vm.IsRunning = false;
        Assert.Equal("SELECTED:", vm.HeroLabel);
    }

    [Fact]
    public async Task ConditionSummary_engineer_only()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap, settings: new AppSettings { IsEngineerDebugMode = false });
        await vm.RefreshProgramsCommand.ExecuteAsync();
        var acquire = Flatten(vm.Hierarchy).First(s => s.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        vm.SelectedStep = acquire;
        Assert.True(string.IsNullOrEmpty(vm.ConditionSummary));

        vm.IsEngineerDebugMode = true;
        vm.OpenSelectedStepDetail();
        Assert.Contains("Samples=", vm.ConditionSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_uses_failure_first_bindings()
    {
        var vm = CreateVm();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        var leaf = Flatten(vm.Hierarchy).First(s => s.Children.Count == 0);
        leaf.StatusText = "Fail";
        leaf.KeyValue = "mean=0.1";
        vm.SelectedStep = null;
        vm.SelectedStep = leaf;
        vm.OpenSelectedStepDetail();
        Assert.Equal("Fail", vm.DetailChipText);
        Assert.Equal("mean=0.1", vm.DetailPrimaryLine);
        Assert.Equal(leaf.Name, vm.DetailStep?.Name);
    }

    [Fact]
    public async Task SessionLogExpanded_defaults_false()
    {
        var vm = CreateVm();
        await vm.RefreshProgramsCommand.ExecuteAsync();
        Assert.False(vm.SessionLogExpanded);
    }

    [Fact]
    public async Task Replay_recording_applies_pass_statuses_for_filters_and_inspect()
    {
        var openTap = new FakeOpenTapSession();
        await openTap.LoadSampleProgramAsync();
        var recordings = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "opentap", "recordings"));
        var summary = openTap.ReplayRecording(recordings, "sample-pass");
        Assert.Equal(RunResult.Passed, summary.Result);
        Assert.Equal(
            "Pass",
            FlattenNodes(openTap.StepTree).First(n => n.Name == "Acquire VDC").StatusText);

        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.RefreshProgramsCommand.ExecuteAsync();

        var leaf = Flatten(vm.Hierarchy).First(s => s.Name == "Acquire VDC");
        Assert.Equal("Pass", leaf.StatusText);

        vm.StepStatusFilter = StepStatusFilter.Fail;
        Assert.Empty(vm.StepRows);
        vm.StepStatusFilter = StepStatusFilter.All;
        Assert.Contains(vm.StepRows, r => r.Name == "Acquire VDC");
        Assert.Equal(2, vm.SuitePassedCount);

        var inspect = new HardwareTest.Features.Inspect.InspectViewModel(openTap);
        inspect.Refresh();
        var identity = Flatten(inspect.Hierarchy)
            .First(s => s.Name == "Identity" || s.Path.EndsWith("/Identity", StringComparison.Ordinal));
        Assert.Equal("Pass", identity.ChipText);
    }

    [Fact]
    public async Task LoadPlanShape_empty_group_exposes_no_leaves_under_empty_section()
    {
        var openTap = new FakeOpenTapSession();
        await openTap.LoadPlanShapeAsync(PlanShapeFixtures.EmptyGroupName);
        var empty = FlattenNodes(openTap.StepTree)
            .First(n => n.Name.Equals("Empty Section", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(empty.Children);
    }

    private static IEnumerable<OpenTapStepNode> FlattenNodes(IEnumerable<OpenTapStepNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in FlattenNodes(node.Children))
            {
                yield return child;
            }
        }
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
