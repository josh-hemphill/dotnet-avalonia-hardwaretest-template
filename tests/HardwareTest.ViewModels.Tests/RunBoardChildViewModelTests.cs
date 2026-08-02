using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Phase 9 guard: every run-board child must be constructible and exercisable without RunTestViewModel.
public sealed class RunBoardChildViewModelTests
{
    private static OpenTapStepNode SampleTree() => new()
    {
        Id = "root",
        Name = "Suite",
        Path = "Suite",
        IsStage = true,
        Children =
        [
            new() { Id = "a", Name = "Identity", Path = "Suite/Identity" },
            new() { Id = "b", Name = "Acquire VDC", Path = "Suite/Acquire VDC" },
        ],
    };

    private static HierarchyStepViewModel Leaf(string name = "Acquire VDC")
        => new(new OpenTapStepNode { Id = "leaf", Name = name, Path = $"Suite/{name}" });

    [Fact]
    public void StepDetail_show_populates_key_values_and_chip()
    {
        var detail = new StepDetailViewModel();
        var step = Leaf();
        step.StatusText = "Fail";
        step.KeyValue = "mean=0.1";

        detail.Show(step, [], ledger: null, conditionSummary: "Samples=32", revealDetail: true);

        Assert.True(detail.ShowDetailRegion);
        Assert.Equal("Fail", detail.DetailChipText);
        Assert.Equal("mean=0.1", detail.DetailPrimaryLine);
        Assert.Equal("Samples=32", detail.ConditionSummary);
        Assert.Contains(detail.DetailKeyValues, l => l == "Path: Suite/Acquire VDC");
        Assert.Same(step, detail.DetailStep);
    }

    [Fact]
    public void StepDetail_append_respects_batch_limit_and_close_hides_region()
    {
        var detail = new StepDetailViewModel();
        var lines = Enumerable.Range(0, 40).Select(i => $"line {i}").ToList();

        Assert.Equal(8, detail.AppendDetailLines(lines, 8));
        Assert.Equal(8, detail.DetailLines.Count);
        Assert.Equal("line 7", detail.DetailLines[^1]);

        detail.CloseDetailCommand.Execute().Subscribe();
        Assert.False(detail.ShowDetailRegion);
    }

    [Fact]
    public void StepDetail_open_command_delegates_to_coordinator_hook()
    {
        var opened = 0;
        var detail = new StepDetailViewModel(() => opened++);
        detail.OpenStepDetailCommand.Execute().Subscribe();
        Assert.Equal(1, opened);
    }

    [Fact]
    public void Interaction_apply_builds_fields_and_blocks_missing_required_value()
    {
        var host = new InteractionHostViewModel();
        var request = new OperatorInteractionRequest
        {
            Id = "req-1",
            Title = "Install fixture",
            Message = "Enter fixture id",
            Fields =
            [
                new OperatorInteractionField
                {
                    Id = "fixtureId",
                    Label = "Fixture id",
                    Kind = OperatorInteractionFieldKind.String,
                    Required = true,
                },
            ],
        };

        host.Apply(request, fallbackMessage: null);

        Assert.True(host.HasInteractionFields);
        Assert.Equal("Install fixture", host.InteractionTitle);
        Assert.False(host.TryCollectResponse(request, out _));
        Assert.Contains("required", host.InteractionValidationError!, StringComparison.OrdinalIgnoreCase);

        host.InteractionFields[0].Value = "FIXTURE-9";
        Assert.True(host.TryCollectResponse(request, out var values));
        Assert.Equal("FIXTURE-9", values["fixtureId"]);

        host.Clear();
        Assert.False(host.HasInteractionFields);
    }

    [Fact]
    public void SessionPanel_confirm_unblocks_and_change_session_reblocks()
    {
        var statuses = new List<string>();
        var panel = new OperatorSessionPanelViewModel(
            new OperatorSession(),
            new AppSettings(),
            statuses.Add);

        panel.RefreshSessionSummary();
        Assert.True(panel.SessionBlocked);
        Assert.True(panel.NeedsDutConfirm);

        panel.DutSerialInput = "SN-CHILD";
        panel.OperatorInput = "Tech";
        panel.ConfirmSessionCommand.Execute().Subscribe();

        Assert.False(panel.SessionBlocked);
        Assert.Contains("SN-CHILD", panel.SessionSummary, StringComparison.Ordinal);

        panel.ChangeSessionCommand.Execute().Subscribe();
        Assert.True(panel.SessionBlocked);
        Assert.Equal(string.Empty, panel.DutSerialInput);
        Assert.NotEmpty(statuses);
    }

    [Fact]
    public void SessionPanel_same_dut_without_require_operator_allows_empty_tech()
    {
        var session = new OperatorSession();
        session.ConfirmDut("SN-1");
        session.MarkStale();
        var panel = new OperatorSessionPanelViewModel(
            session,
            new AppSettings(),
            _ => { },
            () => new ProgramItemViewModel
            {
                Id = "sample",
                DisplayName = "Sample",
                Path = "sample",
                DutFamily = "demo",
                Requirements = new ProgramRequirements { RequireSerial = true, RequireOperator = false },
            });

        panel.RefreshRequirementFlags();
        Assert.False(panel.RequireOperator);
        Assert.Equal("Technician", panel.TechnicianPlaceholder);
        panel.ConfirmSameDutCommand.Execute().Subscribe();
        Assert.True(session.CanRun);
        Assert.False(panel.SessionBlocked);
    }

    [Fact]
    public void SessionPanel_same_dut_require_operator_shows_stale_tech_field()
    {
        var session = new OperatorSession();
        session.ConfirmDut("SN-1");
        session.OperatorName = null;
        session.MarkStale();
        var panel = new OperatorSessionPanelViewModel(
            session,
            new AppSettings(),
            status => { },
            () => new ProgramItemViewModel
            {
                Id = "sample",
                DisplayName = "Sample",
                Path = "sample",
                DutFamily = "demo",
                Requirements = new ProgramRequirements { RequireSerial = true, RequireOperator = true },
            });

        panel.RefreshRequirementFlags();
        panel.RefreshSessionSummary();
        Assert.True(panel.ShowStaleTechnicianField);
        panel.ConfirmSameDutCommand.Execute().Subscribe();
        Assert.False(session.CanRun);

        panel.OperatorInput = "Tech";
        panel.ConfirmSameDutCommand.Execute().Subscribe();
        Assert.True(session.CanRun);
        Assert.Equal("Tech", session.OperatorName);
    }

    [Fact]
    public void SessionPanel_idle_soft_warn_blocks_until_same_dut()
    {
        var session = new OperatorSession();
        session.ConfirmDut("SN-1");
        session.OperatorName = "Tech";
        var settings = new AppSettings
        {
            OperatorSessionIdleMinutes = 100,
            OperatorSessionIdleWarnPercent = 80,
        };
        var panel = new OperatorSessionPanelViewModel(session, settings, _ => { });
        session.EvaluateIdle(TimeSpan.FromMinutes(100), 80, session.LastActivityAt!.Value.AddMinutes(80));
        panel.RefreshSessionSummary();
        Assert.True(panel.IsIdleWarningPrompt);
        Assert.True(panel.SessionBlocked);

        panel.ConfirmSameDutCommand.Execute().Subscribe();
        Assert.False(panel.IsIdleWarningPrompt);
        Assert.True(session.CanRun);
    }

    [Fact]
    public async Task ProgramSelection_refresh_fills_catalog_and_loads_selection()
    {
        var loads = 0;
        var programs = new ProgramSelectionViewModel(
            _ => { },
            loadSelectedProgramAsync: () =>
            {
                loads++;
                return Task.CompletedTask;
            });

        await programs.RefreshProgramsAsync();

        Assert.NotEmpty(programs.Programs);
        Assert.NotNull(programs.SelectedProgram);
        Assert.Equal(1, loads);
    }

    [Fact]
    public void StationOverrides_apply_debug_patch_clamps_knobs()
    {
        var step = Leaf();
        var overrides = new StationOverridesViewModel(
            new FakeOpenTapSession(),
            new AppSettings { IsEngineerDebugMode = true },
            settingsStore: null,
            setStatus: _ => { },
            isEngineerDebugMode: () => true,
            getSelectedStep: () => step)
        {
            DebugSampleCount = 50_000,
            DebugIntervalMs = 0,
        };

        overrides.ApplyDebugPatch();

        Assert.Equal(4096, overrides.DebugSampleCount);
        Assert.Equal(1, overrides.DebugIntervalMs);
    }

    [Fact]
    public void Live_apply_sample_publishes_plot_and_reset_clears_it()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        var frames = 0;
        live.PlotDataChanged += (_, _) => frames++;

        var plotted = live.ApplySample(
            new MeasurementSampleEvent("VDC", 0, 1.23, DateTimeOffset.UtcNow),
            step.Path,
            fallbackStepPath: null,
            selectedStep: step);

        Assert.True(plotted);
        Assert.True(live.HasPlotData);
        Assert.True(live.ShowPlotForSelection);
        Assert.Equal(1, live.PlotYsLength);
        Assert.Equal(1, frames);

        live.ResetForRun();

        Assert.False(live.HasPlotData);
        Assert.False(live.ShowPlotForSelection);
        Assert.Equal(0, live.PlotYsLength);
    }

    [Fact]
    public void StepTree_rebuild_exposes_rows_and_search_narrows_them()
    {
        var tree = new StepTreeViewModel(() => [SampleTree()]);
        tree.RebuildFromHost();

        Assert.NotEmpty(tree.Hierarchy);
        Assert.Equal(2, tree.StepRows.Count);

        tree.StepSearchText = "Acquire";
        Assert.Single(tree.StepRows);
        Assert.Equal("Acquire VDC", tree.StepRows[0].Name);

        tree.StepSearchText = string.Empty;
        Assert.Equal(2, tree.SuitePendingCount);
    }

    [Fact]
    public void StepTree_next_fail_selects_failed_leaf_and_opens_detail()
    {
        var opened = 0;
        var tree = new StepTreeViewModel(() => [SampleTree()], openSelectedDetail: () => opened++);
        tree.RebuildFromHost();

        var failing = tree.StepRows.First(r => r.Name == "Acquire VDC");
        failing.StatusText = "Fail";
        failing.Verdict = "Fail";

        tree.NextFailCommand.Execute().Subscribe();

        Assert.Same(failing, tree.SelectedStep);
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task RunExecution_refuses_to_start_without_a_confirmed_session()
    {
        var openTap = new FakeOpenTapSession();
        var host = new StubRunBoardHost();
        var run = BuildRunExecution(openTap, host, new OperatorSession());

        await run.RunCommand.ExecuteAsync();

        Assert.Equal(0, openTap.RunCount);
        Assert.Contains("Confirm DUT", host.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunExecution_persists_a_record_once_the_session_is_confirmed()
    {
        var openTap = new FakeOpenTapSession();
        var store = new FakeRunStore();
        var session = new OperatorSession();
        session.ConfirmDut("SN-CHILD-RUN");
        session.OperatorName = "Tech";
        var host = new StubRunBoardHost();
        var run = BuildRunExecution(openTap, host, session, store);

        await run.RunCommand.ExecuteAsync();

        Assert.Equal(1, openTap.RunCount);
        Assert.False(string.IsNullOrWhiteSpace(host.LastRunId));
        var saved = await store.LoadAsync(host.LastRunId!);
        Assert.Equal("SN-CHILD-RUN", saved!.DutSerial);
    }

    private static RunExecutionViewModel BuildRunExecution(
        FakeOpenTapSession openTap,
        IRunBoardHost host,
        OperatorSession session,
        FakeRunStore? store = null)
    {
        var settings = new AppSettings();
        var programs = new ProgramSelectionViewModel(_ => { });
        programs.Programs.Add(new ProgramItemViewModel
        {
            Id = "sample",
            DisplayName = "Sample Hardware Suite",
            Path = SampleProgramFactory.EmbeddedName,
            DutFamily = "generic",
            LoadKind = ProgramLoadKind.FactorySample,
            Requirements = ProgramRequirements.Sample,
        });
        programs.SelectedProgram = programs.Programs[0];

        return new RunExecutionViewModel(
            host,
            openTap,
            session,
            new FakeRunControl(),
            new FakeReportService(),
            store ?? new FakeRunStore(),
            settings,
            dutHistory: null,
            BuildInfo.FromAssembly(typeof(RunExecutionViewModel).Assembly),
            new NoopProgress(),
            programs,
            new OperatorSessionPanelViewModel(session, settings, _ => { }),
            new StationOverridesViewModel(openTap, settings, settingsStore: null, setStatus: _ => { }),
            new StepTreeViewModel(() => openTap.StepTree),
            new StepDetailViewModel(),
            new InteractionHostViewModel(),
            new LivePresentationViewModel());
    }

    private sealed class NoopProgress : IProgress<OpenTapProgress>
    {
        public void Report(OpenTapProgress value)
        {
        }
    }

    /// Minimal coordinator stand-in: records shared state and runs UI work inline.
    private sealed class StubRunBoardHost : IRunBoardHost
    {
        public string Status { get; set; } = string.Empty;
        public bool IsRunning { get; set; }
        public double OverallPercent { get; set; }
        public string? LastRunId { get; set; }
        public string HistoryBanner { get; set; } = string.Empty;
        public string IterationText { get; set; } = string.Empty;
        public bool IsEngineerDebugMode => false;
        public bool HasBanner { get; set; }
        public RunBannerSeverity BannerSeverity { get; set; }
        public string BannerMessage { get; set; } = string.Empty;

        public void SetBanner(RunBannerSeverity severity, string message)
        {
            BannerSeverity = severity;
            BannerMessage = message;
            HasBanner = true;
        }

        public Task RunOnUiAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task WaitForPendingFlushesAsync() => Task.CompletedTask;

        public void ForceUiFlush()
        {
        }

        public void ResetPumpForRun()
        {
        }

        public Task LoadSelectedProgramAsync(string? preserveStagePath = null, string? preserveStepPath = null)
            => Task.CompletedTask;

        public void SyncHierarchyLive()
        {
        }

        public void OpenSelectedDetail(bool revealDetail)
        {
        }

        public void RefreshHero()
        {
        }
    }
}
