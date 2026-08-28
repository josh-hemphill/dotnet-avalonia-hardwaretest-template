using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Presentation;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Shell;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
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
        FakeRunStore? store = null,
        FakeSettingsStore? settingsStore = null,
        IDutHistoryService? dutHistory = null,
        ShellNotificationViewModel? shellNotification = null)
    {
        settings ??= settingsStore?.AppSettings ?? new AppSettings();
        var openTapSession = openTap ?? new FakeOpenTapSession();
        return new RunTestViewModel(
            openTapSession,
            openTapSession,
            openTapSession,
            session ?? new OperatorSession(),
            new FakeRunControl(),
            reports ?? new FakeReportService(),
            store ?? new FakeRunStore(),
            settings,
            settingsStore,
            dutHistory,
            shellNotification: shellNotification);
    }

    private static async Task ConfirmReadyAsync(RunTestViewModel vm, string serial = "SN-1", string tech = "Tech")
    {
        vm.SessionPanel.DutSerialInput = serial;
        vm.SessionPanel.OperatorInput = tech;
        await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
    }

    [Fact]
    public async Task Run_selected_uses_selection_path()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-SEL");
        var leaf = Flatten(vm.StepTree.Hierarchy).First(s => s.Children.Count == 0);
        vm.StepTree.SelectedStep = leaf;
        await vm.Run.RunSelectedCommand.ExecuteAsync();
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
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-ROOT");
        vm.StepTree.SelectedStep = vm.StepTree.Hierarchy[0];
        await vm.Run.RunSelectedCommand.ExecuteAsync();
        Assert.Equal(0, openTap.SelectionRunCount);
        Assert.Contains("entire program", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Full_run_updates_hierarchy_status_from_summary()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-STATUS");
        await vm.Run.RunCommand.ExecuteAsync();

        var leaf = Flatten(vm.StepTree.Hierarchy).First(s => s.Children.Count == 0);
        Assert.NotEqual("Pending", leaf.StatusText);
        Assert.Equal("Pass", leaf.StatusText);
        Assert.Contains(vm.StepDetail.DetailKeyValues, line => line.Contains("Status:", StringComparison.Ordinal)
            && !line.Contains("Pending", StringComparison.Ordinal));
        var stageWithStep = vm.StepTree.Stages.FirstOrDefault(s => s.Step is not null);
        Assert.NotNull(stageWithStep);
        Assert.NotEqual("Pending", stageWithStep!.StatusText);
    }

    [Fact]
    public async Task Run_selected_twice_keeps_hierarchy_instances_and_count()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-STABLE");
        var leaf = Flatten(vm.StepTree.Hierarchy).First(s => s.Children.Count == 0);
        vm.StepTree.SelectedStep = leaf;
        var rootBefore = vm.StepTree.Hierarchy[0];
        var hierarchyCount = vm.StepTree.Hierarchy.Count;
        var fullCount = Flatten(vm.StepTree.Hierarchy).Count();

        await vm.Run.RunSelectedCommand.ExecuteAsync();
        await vm.Run.RunSelectedCommand.ExecuteAsync();

        Assert.Equal(2, openTap.SelectionRunCount);
        Assert.Equal(leaf.Path, openTap.LastSelectionPath);
        Assert.Equal(hierarchyCount, vm.StepTree.Hierarchy.Count);
        Assert.Equal(fullCount, Flatten(vm.StepTree.Hierarchy).Count());
        Assert.Same(rootBefore, vm.StepTree.Hierarchy[0]);
        Assert.Equal(leaf.Path, vm.StepTree.SelectedStep?.Path);
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
        Assert.True(vm.Interaction.IsAwaitingOperator);

        await vm.ContinueOperatorCommand.ExecuteAsync();
        Assert.False(vm.Interaction.IsAwaitingOperator);

        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Still running",
            OverallPercent = 20,
        });
        await Task.Delay(50);
        Assert.False(vm.Interaction.IsAwaitingOperator);
    }

    [Fact]
    public async Task Continue_requests_scroll_to_current_step_after_prompt()
    {
        var openTap = new FakeOpenTapSession
        {
            IsAwaitingOperator = true,
            OperatorPromptMessage = "Install fixture",
        };
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await ConfirmReadyAsync(vm);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        var leaf = Flatten(vm.StepTree.Hierarchy).First(n => n.Children.Count == 0);
        vm.CurrentStepPath = leaf.Path;
        vm.CurrentStepName = leaf.Name;
        vm.StepTree.SelectedStep = leaf;

        var scrollRequests = 0;
        vm.StepTree.RequestScrollToSelectedStep += (_, _) => scrollRequests++;

        await vm.ContinueOperatorCommand.ExecuteAsync();

        Assert.False(vm.Interaction.IsAwaitingOperator);
        Assert.True(scrollRequests >= 1, "Continue should re-anchor the step list after the prompt card collapses.");
        Assert.Equal(leaf.Path, vm.StepTree.SelectedStep?.Path);
    }

    [Fact]
    public async Task Awaiting_operator_does_not_change_stage_scope()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        vm.ProgramSelection.SelectedProgram =
            vm.ProgramSelection.Programs.First(p => p.Id == "board-demo");
        for (var i = 0; i < 40 && vm.StepTree.Stages.All(s => s.DisplayName != "Power Rails"); i++)
        {
            await Task.Delay(25);
        }

        var powerRails = vm.StepTree.Stages.First(s => s.DisplayName == "Power Rails");
        vm.StepTree.SelectedStage = powerRails;
        var beforeItems = vm.StepTree.StepListItems.Select(i => i.DisplayName).ToList();
        Assert.Contains(beforeItems, n => n.Contains("3V3", StringComparison.OrdinalIgnoreCase));

        var seat = Flatten(vm.StepTree.Hierarchy)
            .First(n => n.Name.Contains("Seat Board", StringComparison.OrdinalIgnoreCase));
        vm.IsRunning = true;
        vm.CurrentStepPath = seat.Path;
        vm.CurrentStepName = seat.Name;
        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Seat the board",
            StepName = seat.Name,
            StepPath = seat.Path,
            AwaitingOperator = true,
            OperatorPromptMessage = "Seat the board in the demo fixture, then Continue.",
            StatusText = "Awaiting operator",
            OverallPercent = 40,
        });

        Assert.True(vm.Interaction.IsAwaitingOperator);
        Assert.Same(powerRails, vm.StepTree.SelectedStage);
        Assert.Equal(beforeItems, vm.StepTree.StepListItems.Select(i => i.DisplayName).ToList());
        Assert.Equal("Awaiting", vm.HeroChipText);
        Assert.Equal(seat.Name, vm.HeroStepName);
    }

    [Fact]
    public async Task Iteration_progress_appears_on_hero_status_line()
    {
        var vm = CreateVm();
        vm.UiScheduler = action => action();
        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Running Acquire VDC",
            StepName = "Acquire VDC",
            StatusText = "Running",
            OverallPercent = 20,
            IterationIndex = 2,
            IterationTotal = 3,
            IterationText = "2/3",
        });
        await Task.Delay(50);
        Assert.Equal("2/3", vm.IterationText);
        Assert.Contains("iter 2/3", vm.HeroStatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fake_EmitLoopProgress_drives_iteration_text()
    {
        var openTap = new FakeOpenTapSession { EmitLoopProgress = true, Delay = TimeSpan.FromMilliseconds(10) };
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm);
        await vm.Run.RunCommand.ExecuteAsync();
        await Task.Delay(80);
        Assert.Equal("3/3", vm.IterationText);
        Assert.Contains("3/3", vm.HeroStatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendDetailLines_respects_max_per_flush()
    {
        var vm = CreateVm();
        var lines = Enumerable.Range(0, 80).Select(i => $"line {i}").ToList();
        var added = vm.StepDetail.AppendDetailLines(lines, 16);
        Assert.Equal(16, added);
        Assert.Equal(16, vm.StepDetail.DetailLines.Count);
        Assert.Equal("line 0", vm.StepDetail.DetailLines[0]);
        Assert.Equal("line 15", vm.StepDetail.DetailLines[^1]);
    }

    [Fact]
    public async Task Presentation_gauge_tile_appears_for_selected_mean_step()
    {
        var openTap = new FakeOpenTapSession { Delay = TimeSpan.FromMilliseconds(5) };
        var vm = CreateVm(openTap, settings: new AppSettings { PlotRefreshHz = 60 });
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-GAUGE");
        await vm.Run.RunCommand.ExecuteAsync();
        await Task.Delay(80);

        var mean = Flatten(vm.StepTree.Hierarchy).First(s =>
            s.Children.Count == 0 && s.Name.Contains("Mean", StringComparison.OrdinalIgnoreCase));
        vm.StepTree.SelectedStep = mean;
        Assert.True(vm.Live.HasPresentationTiles);
        Assert.Contains(vm.Live.PresentationTiles, t =>
            t.Kind == PresentationTileKind.Scalar
            && t.MetricKey.Contains("mean", StringComparison.OrdinalIgnoreCase));
        Assert.False(vm.Live.ShowPlotForSelection);
    }

    [Fact]
    public async Task Run_without_presentation_metrics_stays_usable_without_tiles()
    {
        var openTap = new FakeOpenTapSession
        {
            Delay = TimeSpan.FromMilliseconds(5),
            ReportPresentationMetrics = false,
            ReportSamples = true,
        };
        var vm = CreateVm(openTap, settings: new AppSettings { PlotRefreshHz = 60 });
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-NOPRES");
        await vm.Run.RunCommand.ExecuteAsync();
        await Task.Delay(80);

        Assert.True(vm.Live.HasPlotData);
        Assert.False(vm.Live.HasPresentationTiles);
        Assert.Empty(vm.Live.PresentationTiles);
    }

    [Fact]
    public async Task Plot_visibility_is_exact_step_path_only()
    {
        var vm = CreateVm(settings: new AppSettings { PlotRefreshHz = 60 });
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-PLOT");
        const string acquirePath = "Sample Hardware Suite/Voltage Sweep/Acquire VDC";
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
        Assert.True(vm.Live.HasPlotData);
        Assert.True(vm.Live.HasChartData);
        Assert.True(vm.Workspace.CanOpenChart);
        Assert.Equal(RunWorkspace.Steps, vm.Workspace.Selected);

        vm.StepTree.SelectedStep = vm.StepTree.Hierarchy[0];
        Assert.False(vm.Live.ShowPlotForSelection);
        Assert.Equal(RunWorkspace.Steps, vm.Workspace.Selected);

        var identity = Flatten(vm.StepTree.Hierarchy).First(s =>
            s.Children.Count == 0 && s.Path.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        vm.StepTree.SelectedStep = identity;
        Assert.False(vm.Live.ShowPlotForSelection);

        var acquire = Flatten(vm.StepTree.Hierarchy).First(s =>
            string.Equals(s.Path, acquirePath, StringComparison.OrdinalIgnoreCase));
        vm.StepTree.SelectedStep = acquire;
        Assert.False(vm.Live.ShowPlotForSelection);
        Assert.True(vm.Live.HasChartData);
        vm.Workspace.OpenChart();
        Assert.True(vm.Workspace.ShowChart);
    }

    [Fact]
    public async Task Continue_operator_resumes_host()
    {
        var openTap = new FakeOpenTapSession { IsAwaitingOperator = true, OperatorPromptMessage = "Install fixture" };
        var vm = CreateVm(openTap);
        vm.Interaction.IsAwaitingOperator = true;
        vm.Interaction.OperatorPromptMessage = "Install fixture";
        await vm.ContinueOperatorCommand.ExecuteAsync();
        Assert.False(openTap.IsAwaitingOperator);
        Assert.False(vm.Interaction.IsAwaitingOperator);
    }

    [Fact]
    public async Task Continue_operator_returns_typed_interaction_values()
    {
        var openTap = new FakeOpenTapSession();
        var request = new OperatorInteractionRequest
        {
            Id = "req-input-1",
            Title = "Install Sweep Fixture",
            Message = "Enter fixture id",
            Fields =
            [
                new OperatorInteractionField
                {
                    Id = "fixtureId",
                    Label = "Fixture id",
                    Kind = OperatorInteractionFieldKind.String,
                    Required = false,
                },
            ],
        };
        openTap.BeginInteraction(request);

        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        vm.IngestProgress(new OpenTapProgress
        {
            Message = request.Message,
            AwaitingOperator = true,
            OperatorPromptMessage = request.Message,
            InteractionRequest = request,
            OverallPercent = 15,
        });
        await Task.Delay(50);

        Assert.True(vm.Interaction.IsAwaitingOperator);
        Assert.True(vm.Interaction.HasInteractionFields);
        Assert.Equal("Install Sweep Fixture", vm.Interaction.InteractionTitle);
        Assert.Single(vm.Interaction.InteractionFields);
        vm.Interaction.InteractionFields[0].Value = "FIXTURE-9";

        await vm.ContinueOperatorCommand.ExecuteAsync();

        Assert.False(vm.Interaction.IsAwaitingOperator);
        Assert.False(vm.Interaction.HasInteractionFields);
        Assert.False(openTap.IsAwaitingOperator);
        Assert.NotNull(openTap.LastInteractionResponse);
        Assert.False(openTap.LastInteractionResponse!.Cancelled);
        Assert.Equal("FIXTURE-9", openTap.LastInteractionResponse.Values["fixtureId"]);
    }

    [Fact]
    public async Task Continue_operator_blocks_when_required_field_empty()
    {
        var openTap = new FakeOpenTapSession();
        var request = new OperatorInteractionRequest
        {
            Id = "req-required",
            Title = "Need serial",
            Message = "Enter serial",
            Fields =
            [
                new OperatorInteractionField
                {
                    Id = "serial",
                    Label = "Serial",
                    Kind = OperatorInteractionFieldKind.String,
                    Required = true,
                },
            ],
        };
        openTap.BeginInteraction(request);

        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        vm.IngestProgress(new OpenTapProgress
        {
            Message = request.Message,
            AwaitingOperator = true,
            OperatorPromptMessage = request.Message,
            InteractionRequest = request,
            OverallPercent = 15,
        });
        await Task.Delay(50);

        await vm.ContinueOperatorCommand.ExecuteAsync();

        Assert.True(vm.Interaction.IsAwaitingOperator);
        Assert.True(openTap.IsAwaitingOperator);
        Assert.Contains("required", vm.Interaction.InteractionValidationError, StringComparison.OrdinalIgnoreCase);
        Assert.Null(openTap.LastInteractionResponse);
    }

    [Fact]
    public async Task Station_overrides_panel_lists_and_applies_in_engineer_mode()
    {
        var openTap = new FakeOpenTapSession();
        var settingsStore = new FakeSettingsStore { AppSettings = { IsEngineerDebugMode = true } };
        var vm = CreateVm(openTap, settings: settingsStore.AppSettings, settingsStore: settingsStore);
        vm.IsEngineerDebugMode = true;
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-PARAM");

        var acquire = Flatten(vm.StepTree.Hierarchy).First(s =>
            s.Children.Count == 0 && s.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        vm.StepTree.SelectedStep = acquire;

        Assert.True(vm.StationOverrides.HasParameterFields);
        Assert.Contains(vm.StationOverrides.ParameterFields, f => f.Label.Contains("Sample", StringComparison.OrdinalIgnoreCase));
        var sampleField = vm.StationOverrides.ParameterFields.First(f => f.Id.EndsWith("/SampleCount", StringComparison.OrdinalIgnoreCase));
        sampleField.Value = "24";

        await vm.StationOverrides.ApplyParametersCommand.ExecuteAsync();

        Assert.Equal(1, settingsStore.SaveAppCount);
        Assert.True(openTap.TryGetParameter(sampleField.Id, out var value));
        Assert.Equal("24", value);
        Assert.Contains(settingsStore.AppSettings.PlanParameterOverrides, o =>
            string.Equals(o.PlanId, "sample", StringComparison.OrdinalIgnoreCase)
            && string.Equals(o.MemberKey, sampleField.Id, StringComparison.OrdinalIgnoreCase)
            && o.Value == "24");
        Assert.Contains("station override", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Station_overrides_shows_annotation_mixin_group_on_identity()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap, settings: new AppSettings { IsEngineerDebugMode = true });
        vm.IsEngineerDebugMode = true;
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-MIXIN");

        var identity = Flatten(vm.StepTree.Hierarchy).First(s =>
            s.Children.Count == 0 && s.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        vm.StepTree.SelectedStep = identity;

        Assert.True(vm.StationOverrides.HasParameterFields);
        Assert.Contains(vm.StationOverrides.ParameterFields, f =>
            f.Label.Contains("Annotation", StringComparison.OrdinalIgnoreCase)
            && f.Label.Contains("Note", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Station_overrides_panel_hidden_without_engineer_mode()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap, settings: new AppSettings { IsEngineerDebugMode = false });
        vm.IsEngineerDebugMode = false;
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-PARAM-OP");

        var acquire = Flatten(vm.StepTree.Hierarchy).First(s =>
            s.Children.Count == 0 && s.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        vm.StepTree.SelectedStep = acquire;

        Assert.False(vm.StationOverrides.HasParameterFields);
        Assert.Empty(vm.StationOverrides.ParameterFields);
    }

    [Fact]
    public async Task Session_stamps_part_and_session_id_on_record()
    {
        var openTap = new FakeOpenTapSession();
        var store = new FakeRunStore();
        var session = new OperatorSession();
        var vm = CreateVm(openTap, store: store, session: session);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        vm.SessionPanel.DutSerialInput = "SN-PART";
        vm.SessionPanel.DutPartInput = "PN-1";
        vm.SessionPanel.DutRevisionInput = "A";
        vm.SessionPanel.OperatorInput = "Tech";
        // Sample requirements do not require part/operator; stamp via ConfirmDut fields through TryConfirm
        Assert.True(session.TryConfirm(
            new ProgramRequirements { RequireSerial = true, RequirePartNumber = true, RequireOperator = true },
            "SN-PART",
            "PN-1",
            "A",
            "Tech",
            "demo",
            out _));
        vm.SessionPanel.ShowSessionForm = false;
        vm.SessionPanel.SessionBlocked = false;
        await vm.Run.RunCommand.ExecuteAsync();
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
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        Assert.NotEmpty(vm.ProgramSelection.Programs);
        Assert.Contains(vm.ProgramSelection.Programs, p => p.IsSample);
    }

    [Fact]
    public async Task Run_blocked_without_dut()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await vm.Run.RunCommand.ExecuteAsync();
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
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-100");
        await vm.Run.RunCommand.ExecuteAsync();

        Assert.Equal(1, openTap.RunCount);
        Assert.Equal("SN-100", openTap.LastDut?.Serial);
        Assert.False(string.IsNullOrWhiteSpace(vm.LastRunId));
        Assert.Equal(1, reports.GenerateCount);
        var saved = await store.LoadAsync(vm.LastRunId!);
        Assert.Equal("SN-100", saved!.DutSerial);
    }

    [Fact]
    public async Task Run_does_not_set_history_banner_when_setting_disabled()
    {
        var openTap = new FakeOpenTapSession { Delay = TimeSpan.FromMilliseconds(5) };
        var historyStore = new FakeRunStore();
        historyStore.Seed(new TestRunRecord
        {
            RunId = "prior",
            PlanId = "sample",
            PlanName = "Sample Hardware Suite",
            DutSerial = "SN-BANNER",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 10, Timestamp = DateTimeOffset.UtcNow }],
        });
        var settings = new AppSettings { ShowDutHistoryOnRun = false, PlotRefreshHz = 60 };
        var vm = CreateVm(
            openTap,
            settings: settings,
            store: historyStore,
            dutHistory: new DutHistoryService(historyStore));
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-BANNER");
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.True(string.IsNullOrEmpty(vm.HistoryBanner));
    }

    [Fact]
    public async Task Run_sets_history_banner_when_setting_enabled()
    {
        var openTap = new FakeOpenTapSession { Delay = TimeSpan.FromMilliseconds(5) };
        var historyStore = new FakeRunStore();
        historyStore.Seed(new TestRunRecord
        {
            RunId = "prior",
            PlanId = "sample",
            PlanName = "Sample Hardware Suite",
            DutSerial = "SN-BANNER-ON",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 10, Timestamp = DateTimeOffset.UtcNow }],
        });
        var settings = new AppSettings { ShowDutHistoryOnRun = true, PlotRefreshHz = 60 };
        var vm = CreateVm(
            openTap,
            settings: settings,
            store: historyStore,
            dutHistory: new DutHistoryService(historyStore));
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-BANNER-ON");
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.False(string.IsNullOrWhiteSpace(vm.HistoryBanner));
    }

    [Fact]
    public async Task ChangeDut_clears_and_blocks_run()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-1");
        await vm.SessionPanel.ChangeSessionCommand.ExecuteAsync();
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal(0, openTap.RunCount);
        Assert.True(vm.SessionPanel.NeedsDutConfirm);
    }

    [Fact]
    public async Task Second_run_same_dut_does_not_require_reconfirm()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-22");
        await vm.Run.RunCommand.ExecuteAsync();
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal(2, openTap.RunCount);
    }

    [Fact]
    public async Task Cancel_during_run_marks_cancelled()
    {
        var openTap = new FakeOpenTapSession { Delay = TimeSpan.FromSeconds(5) };
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-C");
        var runTask = vm.Run.RunCommand.ExecuteAsync();
        await Task.Delay(50);
        await vm.Run.CancelCommand.ExecuteAsync();
        await runTask;
        Assert.Contains("Cancelled", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failed_result_still_attempts_report()
    {
        var openTap = new FakeOpenTapSession { CompletionResult = RunResult.Failed };
        var reports = new FakeReportService();
        var vm = CreateVm(openTap, reports);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-F");
        await vm.Run.RunCommand.ExecuteAsync();
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
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        vm.SessionPanel.OperatorInput = "Tech";
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal(0, openTap.RunCount);
        await vm.SessionPanel.ConfirmSameDutCommand.ExecuteAsync();
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal(1, openTap.RunCount);
    }

    [Fact]
    public async Task Require_dut_confirm_every_run_marks_stale_after_pass()
    {
        var settings = new AppSettings { RequireDutConfirmEveryRun = true, UseMockVisa = true };
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap, settings: settings);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-EVERY");
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal(1, openTap.RunCount);
        Assert.Equal(OperatorSessionState.Stale, vm.SessionPanel.Session.State);
        Assert.True(vm.SessionPanel.SessionBlocked);
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal(1, openTap.RunCount);
        await vm.SessionPanel.ConfirmSameDutCommand.ExecuteAsync();
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal(2, openTap.RunCount);
    }

    [Fact]
    public async Task Run_selected_records_attempt_counts()
    {
        var openTap = new FakeOpenTapSession();
        var store = new FakeRunStore();
        var vm = CreateVm(openTap, store: store);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-ATT");
        vm.StepTree.SelectedStep = Flatten(vm.StepTree.Hierarchy).First(s => s.Children.Count == 0);
        Assert.NotNull(vm.StepTree.SelectedStep);
        await vm.Run.RunSelectedCommand.ExecuteAsync();
        await vm.Run.RunSelectedCommand.ExecuteAsync();
        var saved = await store.LoadAsync(vm.LastRunId!);
        Assert.NotNull(saved);
        Assert.NotEmpty(saved!.StepAttempts);
        Assert.Contains(saved.StepAttempts, a => a.AttemptCount >= 1);
    }

    [Fact]
    public async Task Session_blocked_until_confirmed()
    {
        var vm = CreateVm();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        Assert.True(vm.SessionPanel.SessionBlocked);
        await ConfirmReadyAsync(vm);
        Assert.False(vm.SessionPanel.SessionBlocked);
    }

    [Fact]
    public async Task Burst_progress_is_throttled_and_details_capped()
    {
        var settings = new AppSettings { PlotRefreshHz = 20 };
        var vm = CreateVm(settings: settings);
        vm.UiScheduler = action => action();
        vm.StepDetail.ShowLiveLog = true;
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();

        var publishRef = vm.Live.PlotYs;
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
        Assert.True(vm.StepDetail.DetailLines.Count <= 200);
        Assert.True(vm.Live.PlotYsLength <= 2048);
        Assert.Same(publishRef, vm.Live.PlotYs);
    }

    [Fact]
    public async Task ApplyDebugPatch_clamps_sample_count_and_interval()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap, settings: new AppSettings { IsEngineerDebugMode = true });
        vm.IsEngineerDebugMode = true;
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        vm.StepTree.SelectedStep = vm.StepTree.Hierarchy.Count > 0
            ? Flatten(vm.StepTree.Hierarchy).FirstOrDefault()
            : null;
        if (vm.StepTree.SelectedStep is null)
        {
            // Sample tree should exist after load.
            Assert.Fail("Expected hierarchy after loading sample program.");
        }

        vm.StationOverrides.DebugSampleCount = 50_000;
        vm.StationOverrides.DebugIntervalMs = 0;
        await vm.StationOverrides.ApplyDebugPatchCommand.ExecuteAsync();
        Assert.Equal(4096, vm.StationOverrides.DebugSampleCount);
        Assert.Equal(1, vm.StationOverrides.DebugIntervalMs);
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
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-FILT");
        await vm.Run.RunCommand.ExecuteAsync();

        vm.StepTree.StepStatusFilter = StepStatusFilter.Fail;
        Assert.Empty(vm.StepTree.StepRows);

        vm.StepTree.StepStatusFilter = StepStatusFilter.All;
        vm.StepTree.StepSearchText = "Acquire";
        Assert.Contains(vm.StepTree.StepRows, r => r.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.StepTree.StepRows, r => r.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NextFail_selects_failed_leaf()
    {
        var openTap = new FakeOpenTapSession { CompletionResult = RunResult.Failed };
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-NF");
        await vm.Run.RunCommand.ExecuteAsync();

        await vm.StepTree.NextFailCommand.ExecuteAsync();
        Assert.NotNull(vm.StepTree.SelectedStep);
        Assert.Equal("Fail", StatusChip.FromStatus(vm.StepTree.SelectedStep!.StatusText, vm.StepTree.SelectedStep.Verdict));
    }

    [Fact]
    public async Task Inspect_refresh_matches_run_pass_after_rollup()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-INSP");
        await vm.Run.RunCommand.ExecuteAsync();

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
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        const string path = "Sample Hardware Suite/Voltage Sweep/Acquire VDC";
        vm.ApplySelectionFromInspect(path);
        Assert.Equal(path, vm.StepTree.SelectedStep?.Path);
    }

    [Fact]
    public async Task Suite_summary_counts_after_run()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-SUM");
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal(6, vm.StepTree.SuitePassedCount);
        Assert.Equal(0, vm.StepTree.SuiteFailedCount);
    }

    [Fact]
    public async Task Board_demo_program_exposes_nested_sections()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        Assert.Contains(vm.ProgramSelection.Programs, p => p.Path == BoardDemoProgramFactory.EmbeddedName);
        vm.ProgramSelection.SelectedProgram = vm.ProgramSelection.Programs.First(p => p.Path == BoardDemoProgramFactory.EmbeddedName);
        for (var i = 0; i < 40 && vm.StepTree.Stages.All(s => s.DisplayName != "Power Rails"); i++)
        {
            await Task.Delay(25);
        }

        var power = vm.StepTree.Stages.FirstOrDefault(s => s.DisplayName == "Power Rails");
        Assert.NotNull(power);
        vm.StepTree.SelectedStage = power;
        Assert.True(vm.StepTree.HasSubsections);
        Assert.Contains(vm.StepTree.Subsections, s => s.DisplayName == "3V3 Rail");
        Assert.Contains(
            vm.StepTree.StepListItems,
            i => i.IsHeader
                 && i.DisplayName == "Power Rails"
                 && i.Step is not null
                 && string.Equals(i.Step.Path, power!.Step!.Path, StringComparison.Ordinal));
        vm.StepTree.SelectedSubsection = vm.StepTree.Subsections.First(s => s.DisplayName == "3V3 Rail");
        Assert.Contains(vm.StepTree.StepRows, r => r.Name.Contains("Acquire 3V3", StringComparison.Ordinal));
        Assert.Contains(
            vm.StepTree.StepListItems,
            i => i.IsHeader && i.DisplayName == "3V3 Rail" && i.IsRunnable);
    }

    [Fact]
    public async Task Stage_filter_header_run_selected_targets_whole_stage()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-STAGE-HDR");
        vm.ProgramSelection.SelectedProgram = vm.ProgramSelection.Programs.First(p => p.Path == BoardDemoProgramFactory.EmbeddedName);
        for (var i = 0; i < 40 && vm.StepTree.Stages.All(s => s.DisplayName != "Power Rails"); i++)
        {
            await Task.Delay(25);
        }

        var power = vm.StepTree.Stages.First(s => s.DisplayName == "Power Rails");
        vm.StepTree.SelectedStage = power;
        var stageHeader = vm.StepTree.StepListItems.First(i =>
            i.IsHeader && i.DisplayName == "Power Rails" && i.Step is not null);
        vm.StepTree.SelectedStepListItem = stageHeader;

        Assert.Equal(stageHeader.Step!.Path, vm.StepTree.SelectedStep?.Path);
        await vm.Run.RunSelectedCommand.ExecuteAsync();
        Assert.Equal(1, openTap.SelectionRunCount);
        Assert.Equal(stageHeader.Step.Path, openTap.LastSelectionPath);
    }

    [Fact]
    public async Task Entire_program_step_list_includes_stage_and_section_headers()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        vm.ProgramSelection.SelectedProgram = vm.ProgramSelection.Programs.First(p => p.Path == BoardDemoProgramFactory.EmbeddedName);
        for (var i = 0; i < 40 && vm.StepTree.Stages.All(s => s.DisplayName != "Power Rails"); i++)
        {
            await Task.Delay(25);
        }

        var entire = vm.StepTree.Stages.First(s => s.Step is null);
        vm.StepTree.SelectedStage = entire;

        Assert.Contains(vm.StepTree.StepListItems, i => i.IsHeader && i.DisplayName == "Power Rails / 3V3 Rail");
        Assert.Contains(vm.StepTree.StepListItems, i => i.IsHeader && i.DisplayName == "Power Rails / 5V Rail");
        Assert.Contains(vm.StepTree.StepListItems, i => i.IsHeader && i.DisplayName.Contains("Communications", StringComparison.Ordinal));
        Assert.Contains(vm.StepTree.StepListItems, i => !i.IsHeader && i.DisplayName.Contains("Acquire 3V3", StringComparison.Ordinal));
        Assert.Contains(vm.StepTree.StepListItems, i => !i.IsHeader && i.DisplayName == "Seat Board Fixture");
    }

    [Fact]
    public async Task Section_header_selection_runs_that_subtree()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-SECTION");
        vm.ProgramSelection.SelectedProgram = vm.ProgramSelection.Programs.First(p => p.Path == BoardDemoProgramFactory.EmbeddedName);
        for (var i = 0; i < 40 && vm.StepTree.Stages.All(s => s.DisplayName != "Power Rails"); i++)
        {
            await Task.Delay(25);
        }

        var entire = vm.StepTree.Stages.First(s => s.Step is null);
        vm.StepTree.SelectedStage = entire;

        var sectionHeader = vm.StepTree.StepListItems.First(i =>
            i.IsHeader && i.DisplayName == "Power Rails / 3V3 Rail" && i.Step is not null);
        vm.StepTree.SelectedStepListItem = sectionHeader;

        Assert.Equal(sectionHeader.Step!.Path, vm.StepTree.SelectedStep?.Path);
        Assert.True(sectionHeader.IsRunnable);

        await vm.Run.RunSelectedCommand.ExecuteAsync();

        Assert.Equal(1, openTap.SelectionRunCount);
        Assert.Equal(sectionHeader.Step.Path, openTap.LastSelectionPath);
        Assert.Contains("3V3", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_selected_preserves_sibling_section_status()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-PRESERVE");
        vm.ProgramSelection.SelectedProgram = vm.ProgramSelection.Programs.First(p => p.Path == BoardDemoProgramFactory.EmbeddedName);
        for (var i = 0; i < 40 && vm.StepTree.Stages.All(s => s.DisplayName != "Power Rails"); i++)
        {
            await Task.Delay(25);
        }

        await vm.Run.RunCommand.ExecuteAsync();

        static IEnumerable<HierarchyStepViewModel> Flat(IEnumerable<HierarchyStepViewModel> roots)
        {
            foreach (var n in roots)
            {
                yield return n;
                foreach (var c in Flat(n.Children))
                {
                    yield return c;
                }
            }
        }

        var acquire5V = Flat(vm.StepTree.Hierarchy).First(s => s.Name.Contains("Acquire 5V", StringComparison.Ordinal));
        Assert.Equal("Pass", acquire5V.StatusText);
        acquire5V.KeyValue = "5V=5.010";
        acquire5V.Node.KeyValue = "5V=5.010";

        var entire = vm.StepTree.Stages.First(s => s.Step is null);
        vm.StepTree.SelectedStage = entire;
        var sectionHeader = vm.StepTree.StepListItems.First(i =>
            i.IsHeader && i.DisplayName == "Power Rails / 3V3 Rail" && i.Step is not null);
        vm.StepTree.SelectedStepListItem = sectionHeader;
        await vm.Run.RunSelectedCommand.ExecuteAsync();

        Assert.Equal(1, openTap.SelectionRunCount);
        Assert.Equal("Pass", acquire5V.StatusText);
        Assert.Equal("5V=5.010", acquire5V.KeyValue);
        Assert.True(vm.StepTree.SuitePassedCount > 0);
        Assert.True(vm.StepTree.SuitePendingCount < Flat(vm.StepTree.Hierarchy).Count(s => s.Children.Count == 0));
    }

    [Fact]
    public async Task Sample_entire_program_step_list_follows_plan_order_with_stage_headers()
    {
        var vm = CreateVm();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        var entire = vm.StepTree.Stages.First(s => s.Step is null);
        vm.StepTree.SelectedStage = entire;

        var names = vm.StepTree.StepListItems.Select(i => i.DisplayName).ToList();
        Assert.Contains("Identity", names);
        Assert.Contains("Voltage Sweep", names);
        Assert.DoesNotContain(vm.StepTree.StepListItems, i => i.IsHeader && i.DisplayName == "Sample Hardware Suite");

        var identity = names.IndexOf("Identity");
        var confirm = names.IndexOf("Confirm Sweep Area Clear");
        var install = names.IndexOf("Install Sweep Fixture");
        var voltage = names.IndexOf("Voltage Sweep");
        var acquire = names.IndexOf("Acquire VDC");
        var shutdown = names.IndexOf("Safe Shutdown");

        Assert.True(identity >= 0 && confirm > identity);
        Assert.True(install > confirm && voltage > install);
        Assert.True(acquire > voltage && shutdown > acquire);
    }

    [Fact]
    public async Task Live_status_updates_preserve_step_list_item_identity()
    {
        var vm = CreateVm();
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        var entire = vm.StepTree.Stages.First(s => s.Step is null);
        vm.StepTree.SelectedStage = entire;

        var before = vm.StepTree.StepListItems.ToList();
        Assert.NotEmpty(before);
        var selectedBefore = vm.StepTree.SelectedStepListItem;
        Assert.NotNull(selectedBefore);

        vm.IsRunning = true;
        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Working",
            StepName = "Acquire VDC",
            StepPath = "Sample Hardware Suite/Voltage Sweep/Acquire VDC",
            StatusText = "Running",
            KeyValue = "V=1.2",
            OverallPercent = 40,
        });
        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Working",
            StepName = "Acquire VDC",
            StepPath = "Sample Hardware Suite/Voltage Sweep/Acquire VDC",
            StatusText = "Pass",
            Verdict = "Pass",
            KeyValue = "V=1.25",
            OverallPercent = 55,
        });

        Assert.Equal(before.Count, vm.StepTree.StepListItems.Count);
        Assert.True(
            before.Zip(vm.StepTree.StepListItems).All(pair => ReferenceEquals(pair.First, pair.Second)),
            "Live status flushes must not recreate step list rows (hover/focus jumps under the pointer).");
        Assert.Same(selectedBefore, vm.StepTree.SelectedStepListItem);

        var acquire = before.First(i => !i.IsHeader && i.DisplayName == "Acquire VDC");
        Assert.Equal("Pass", acquire.Step!.ChipText);
        Assert.Equal("V=1.25", acquire.Step.KeyValue);
    }

    [Fact]
    public void Compact_toggle_updates_compact_step_rows()
    {
        var vm = CreateVm();
        Assert.False(vm.StepTree.CompactStepRows);
        vm.StepTree.ToggleCompactCommand.Execute().Subscribe();
        Assert.True(vm.StepTree.CompactStepRows);
        vm.StepTree.ToggleCompactCommand.Execute().Subscribe();
        Assert.False(vm.StepTree.CompactStepRows);
    }

    [Fact]
    public async Task Stage_progress_includes_fail_suffix()
    {
        var openTap = new FakeOpenTapSession { CompletionResult = RunResult.Failed };
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-PF");
        await vm.Run.RunCommand.ExecuteAsync();
        var entire = vm.StepTree.Stages.First(s => s.Step is null);
        Assert.Contains("F", entire.ProgressText, StringComparison.Ordinal);
        Assert.Equal("Fail", entire.ChipText);
    }
    [Fact]
    public async Task StepRows_are_leaves_under_selected_stage()
    {
        var vm = CreateVm();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        var identity = vm.StepTree.Stages.First(s => s.DisplayName == "Identity");
        vm.StepTree.SelectedStage = identity;
        Assert.All(vm.StepTree.StepRows, r => Assert.Empty(r.Children));
        Assert.Contains(vm.StepTree.StepRows, r => r.Name == "Identity Check");
        Assert.DoesNotContain(vm.StepTree.StepRows, r => r.Name == "Acquire VDC");
    }

    [Fact]
    public async Task Stage_progress_text_is_completed_over_total()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        var entire = vm.StepTree.Stages.First(s => s.Step is null);
        Assert.Equal("0/6", entire.ProgressText);
        await ConfirmReadyAsync(vm, "SN-PROG");
        await vm.Run.RunCommand.ExecuteAsync();
        Assert.Equal("6/6", entire.ProgressText);
        Assert.Equal("Pass", entire.ChipText);
    }

    [Fact]
    public async Task Hero_follows_progress_step_while_running()
    {
        var vm = CreateVm();
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        vm.IsRunning = true;
        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Working",
            StepName = "Acquire VDC",
            StepPath = "Sample Hardware Suite/Voltage Sweep/Acquire VDC",
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
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        var acquire = Flatten(vm.StepTree.Hierarchy).First(s => s.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        vm.StepTree.SelectedStep = acquire;
        Assert.True(string.IsNullOrEmpty(vm.StepDetail.ConditionSummary));

        vm.IsEngineerDebugMode = true;
        vm.OpenSelectedStepDetail();
        Assert.Contains("Samples=", vm.StepDetail.ConditionSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_uses_failure_first_bindings()
    {
        var vm = CreateVm();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        var leaf = Flatten(vm.StepTree.Hierarchy).First(s => s.Children.Count == 0);
        leaf.StatusText = "Fail";
        leaf.KeyValue = "mean=0.1";
        vm.StepTree.SelectedStep = null;
        vm.StepTree.SelectedStep = leaf;
        vm.OpenSelectedStepDetail();
        Assert.Equal("Fail", vm.StepDetail.DetailChipText);
        Assert.Equal("mean=0.1", vm.StepDetail.DetailPrimaryLine);
        Assert.Equal(leaf.Name, vm.StepDetail.DetailStep?.Name);
    }

    [Fact]
    public async Task Selecting_step_does_not_force_open_collapsed_detail()
    {
        var vm = CreateVm();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        var leaves = Flatten(vm.StepTree.Hierarchy).Where(s => s.Children.Count == 0).Take(2).ToList();
        Assert.True(leaves.Count >= 2);

        vm.StepTree.SelectedStep = leaves[0];
        await vm.StepDetail.CloseDetailCommand.ExecuteAsync();
        Assert.False(vm.StepDetail.ShowDetailRegion);

        vm.StepTree.SelectedStep = leaves[1];
        Assert.False(vm.StepDetail.ShowDetailRegion);
        Assert.Equal(leaves[1].Name, vm.StepDetail.DetailStep?.Name);
        Assert.Equal(RunWorkspace.Steps, vm.Workspace.Selected);
    }

    [Fact]
    public async Task OpenSelectedStepDetail_and_fail_nav_reopen_detail()
    {
        var openTap = new FakeOpenTapSession { CompletionResult = RunResult.Failed };
        var vm = CreateVm(openTap);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-DETAIL");
        await vm.Run.RunCommand.ExecuteAsync();

        var leaf = Flatten(vm.StepTree.Hierarchy).First(s => s.Children.Count == 0);
        vm.StepTree.SelectedStep = leaf;
        await vm.StepDetail.CloseDetailCommand.ExecuteAsync();
        Assert.False(vm.StepDetail.ShowDetailRegion);

        vm.OpenSelectedStepDetail();
        Assert.True(vm.StepDetail.ShowDetailRegion);
        Assert.Equal(RunWorkspace.Details, vm.Workspace.Selected);

        await vm.StepDetail.CloseDetailCommand.ExecuteAsync();
        Assert.False(vm.StepDetail.ShowDetailRegion);
        Assert.Equal(RunWorkspace.Steps, vm.Workspace.Selected);

        await vm.StepTree.NextFailCommand.ExecuteAsync();
        Assert.True(vm.StepDetail.ShowDetailRegion);
        Assert.Equal(RunWorkspace.Details, vm.Workspace.Selected);
    }

    [Fact]
    public async Task RefreshHero_clears_status_line_while_awaiting()
    {
        var vm = CreateVm();
        vm.UiScheduler = action => action();
        vm.Status = "Suite running…";
        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Install fixture",
            AwaitingOperator = true,
            OperatorPromptMessage = "Install fixture",
            OverallPercent = 10,
        });
        await Task.Delay(50);

        Assert.True(vm.Interaction.IsAwaitingOperator);
        Assert.Equal("Install fixture", vm.Interaction.OperatorPromptMessage);
        Assert.True(string.IsNullOrEmpty(vm.HeroStatusLine));
        Assert.Equal("Awaiting", vm.HeroChipText);
    }

    [Fact]
    public async Task Chart_workspace_restores_after_operator_prompt()
    {
        var vm = CreateVm(settings: new AppSettings { PlotRefreshHz = 60 });
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-CHART");
        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Acquire",
            StepName = "Acquire VDC",
            StepPath = "Sample Hardware Suite/Voltage Sweep/Acquire VDC",
            Sample = new MeasurementSampleEvent("VDC", 0, 1.2, DateTimeOffset.UtcNow, DisplayRole: "timeseries"),
            OverallPercent = 20,
        });
        await Task.Delay(50);
        Assert.True(vm.Live.HasChartData);
        vm.Workspace.OpenChart();
        Assert.True(vm.Workspace.ShowChart);

        vm.IngestProgress(new OpenTapProgress
        {
            Message = "Install fixture",
            AwaitingOperator = true,
            OperatorPromptMessage = "Install fixture",
            OverallPercent = 30,
        });
        await Task.Delay(50);
        Assert.True(vm.Workspace.ShowInteraction);
        Assert.False(vm.Workspace.ShowChart);
        Assert.Equal(RunWorkspace.Chart, vm.Workspace.Selected);

        await vm.ContinueOperatorCommand.ExecuteAsync();
        Assert.False(vm.Workspace.ShowInteraction);
        Assert.True(vm.Workspace.ShowChart);
    }

    [Fact]
    public async Task Out_of_band_warns_via_shell_without_leaving_steps()
    {
        var shell = new ShellNotificationViewModel();
        var vm = CreateVm(settings: new AppSettings { PlotRefreshHz = 60 }, shellNotification: shell);
        vm.UiScheduler = action => action();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-OOB");
        var acquire = Flatten(vm.StepTree.Hierarchy).First(s =>
            s.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        vm.Live.ApplySample(
            new MeasurementSampleEvent(
                "VDC",
                0,
                9.9,
                DateTimeOffset.UtcNow,
                DisplayRole: "timeseries",
                LimitLow: 0,
                LimitHigh: 1),
            acquire.Path,
            null,
            acquire);
        Assert.True(vm.Live.HasChartAttention);
        Assert.Equal(RunWorkspace.Steps, vm.Workspace.Selected);
        Assert.True(shell.HasContent);
        Assert.Contains("Out of band", shell.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("View chart", shell.PrimaryLabel);
    }

    [Fact]
    public async Task AppSettingsSaved_syncs_engineer_mode_to_run_board()
    {
        var store = new FakeSettingsStore { AppSettings = { IsEngineerDebugMode = false } };
        var vm = CreateVm(settings: store.AppSettings, settingsStore: store);
        Assert.False(vm.IsEngineerDebugMode);

        store.AppSettings.IsEngineerDebugMode = true;
        await store.SaveAppSettingsAsync();

        Assert.True(vm.IsEngineerDebugMode);
    }

    [Fact]
    public async Task Run_blocks_mock_resources_when_UseMockVisa_false()
    {
        var openTap = new FakeOpenTapSession();
        await openTap.LoadSampleProgramAsync();
        var settings = new AppSettings { UseMockVisa = false };
        var shell = new ShellNotificationViewModel();
        var vm = CreateVm(openTap, settings: settings, shellNotification: shell);
        StationBindRequestedEventArgs? bind = null;
        vm.NavigateToInstrumentsRequested += (_, e) => bind = e;
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-MOCK");

        await vm.Run.RunCommand.ExecuteAsync();

        Assert.Contains("Use mock VISA is off", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, openTap.RunCount);
        Assert.NotNull(bind);
        Assert.Equal("sample", bind.PlanId);
        Assert.Contains("DMM", bind.SlotNames);
        Assert.Equal("Open Instruments", shell.PrimaryLabel);
        Assert.True(shell.HasPrimaryAction);
    }

    [Fact]
    public async Task Run_blocks_unbound_slots_and_requests_instruments_bind()
    {
        var openTap = new FakeOpenTapSession();
        await openTap.LoadSampleProgramAsync();
        openTap.Slots[0].ResourceName = string.Empty;
        var vm = CreateVm(openTap);
        StationBindRequestedEventArgs? bind = null;
        vm.NavigateToInstrumentsRequested += (_, e) => bind = e;
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await ConfirmReadyAsync(vm, "SN-UNBOUND");

        await vm.Run.RunCommand.ExecuteAsync();

        Assert.Contains("unbound", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, openTap.RunCount);
        Assert.NotNull(bind);
        Assert.Contains("DMM", bind.SlotNames);
    }

    [Fact]
    public async Task RefreshPrograms_lists_built_in_demos_once()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        Assert.Equal(1, vm.ProgramSelection.Programs.Count(p => p.Id == "sample"));
        Assert.Equal(1, vm.ProgramSelection.Programs.Count(p => p.Id == "board-demo"));
        Assert.Equal(1, vm.ProgramSelection.Programs.Count(p => p.Id == "sweep-demo"));
        Assert.Equal(1, vm.ProgramSelection.Programs.Count(p => p.Id == "timing-demo"));
        Assert.Equal(ProgramLoadKind.FactorySample, vm.ProgramSelection.Programs.First(p => p.Id == "sample").LoadKind);
        Assert.Equal(ProgramLoadKind.FactoryBoardDemo, vm.ProgramSelection.Programs.First(p => p.Id == "board-demo").LoadKind);
        Assert.Equal(ProgramLoadKind.FactorySweepDemo, vm.ProgramSelection.Programs.First(p => p.Id == "sweep-demo").LoadKind);
        Assert.Equal(ProgramLoadKind.FactoryTimingDemo, vm.ProgramSelection.Programs.First(p => p.Id == "timing-demo").LoadKind);
    }

    [Fact]
    public async Task Timing_demo_shows_stages_and_step_list()
    {
        var openTap = new FakeOpenTapSession();
        var vm = CreateVm(openTap);
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        vm.ProgramSelection.SelectedProgram =
            vm.ProgramSelection.Programs.First(p => p.Id == "timing-demo");
        for (var i = 0; i < 40 && vm.StepTree.Stages.All(s => s.DisplayName != "Derived timing checks"); i++)
        {
            await Task.Delay(25);
        }

        Assert.Contains(vm.StepTree.Stages, s => s.DisplayName == "Bump waveform");
        Assert.Contains(vm.StepTree.Stages, s => s.DisplayName == "Derived timing checks");
        Assert.Contains(vm.StepTree.Stages, s => s.DisplayName == "Safety");

        var entire = vm.StepTree.Stages.First(s => s.Step is null);
        vm.StepTree.SelectedStage = entire;
        var names = vm.StepTree.StepListItems.Select(i => i.DisplayName).ToList();
        Assert.Contains(names, n => n.Contains("Simulate bump waveform", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("Bump rise time", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("Peak overshoot", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("Safe Shutdown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SessionLogExpanded_defaults_false()
    {
        var vm = CreateVm();
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        Assert.False(vm.StepDetail.SessionLogExpanded);
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
        await vm.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();

        var leaf = Flatten(vm.StepTree.Hierarchy).First(s => s.Name == "Acquire VDC");
        Assert.Equal("Pass", leaf.StatusText);

        vm.StepTree.StepStatusFilter = StepStatusFilter.Fail;
        Assert.Empty(vm.StepTree.StepRows);
        vm.StepTree.StepStatusFilter = StepStatusFilter.All;
        Assert.Contains(vm.StepTree.StepRows, r => r.Name == "Acquire VDC");
        Assert.Equal(2, vm.StepTree.SuitePassedCount);

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
