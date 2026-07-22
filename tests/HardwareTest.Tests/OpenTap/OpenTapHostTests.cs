using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[CollectionDefinition("OpenTapSerial", DisableParallelization = true)]
public sealed class OpenTapSerialCollection;

[Collection("OpenTapSerial")]
public sealed class OperatorSessionTests
{
    [Fact]
    public void TryConfirm_requires_serial()
    {
        var session = new OperatorSession();
        Assert.False(session.TryConfirm(ProgramRequirements.Sample, "  ", null, null, null, "demo", out var error));
        Assert.Contains("serial", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.TryConfirm(ProgramRequirements.Sample, "SN-9", null, null, "Tech", "demo", out _));
        Assert.True(session.CanRun);
        Assert.Equal("SN-9", session.DutSerial);
    }

    [Fact]
    public void TryConfirm_enforces_optional_fields_when_required()
    {
        var session = new OperatorSession();
        var req = new ProgramRequirements
        {
            RequireSerial = true,
            RequirePartNumber = true,
            RequireOperator = true,
        };
        Assert.False(session.TryConfirm(req, "SN-1", null, null, null, "demo", out var error));
        Assert.Contains("part", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.TryConfirm(req, "SN-1", "PN-1", null, "Op", "demo", out _));
        Assert.Equal("PN-1", session.DutPartNumber);
        Assert.Equal("Op", session.OperatorName);
    }

    [Fact]
    public void ConfirmDut_enables_run_ChangeDut_blocks()
    {
        var session = new OperatorSession();
        Assert.False(session.CanRun);
        session.ConfirmDut("ABC123");
        Assert.True(session.CanRun);
        Assert.Equal(OperatorSessionState.Active, session.State);

        session.ChangeDut();
        Assert.False(session.CanRun);
        Assert.Equal(OperatorSessionState.NeedsDut, session.State);
    }

    [Fact]
    public void Idle_timeout_marks_stale()
    {
        var session = new OperatorSession();
        session.ConfirmDut("SN-1");
        session.CheckIdleStale(TimeSpan.FromHours(1), DateTimeOffset.UtcNow.AddHours(2));
        Assert.Equal(OperatorSessionState.Stale, session.State);
        Assert.False(session.CanRun);

        session.ConfirmSameDut();
        Assert.True(session.CanRun);
    }

    [Fact]
    public void Program_family_mismatch_marks_stale()
    {
        var session = new OperatorSession();
        session.ConfirmDut("SN-1", family: "demo");
        session.SelectProgram("other", "other.TapPlan", "Other", "power");
        Assert.Equal(OperatorSessionState.Stale, session.State);
    }
}

[Collection("OpenTapSerial")]
public sealed class OpenTapSessionTests
{
    [Fact]
    public async Task RunSelection_keeps_safe_shutdown_enabled()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-SEL", Family: "demo"));

        var identity = session.StepTree.SelectMany(Flatten)
            .First(n => n.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase)
                        && n.Children.Count == 0);
        var summary = await session.RunSelectionAsync(identity.Path);
        Assert.True(
            summary.Result is RunResult.Passed or RunResult.Failed or RunResult.Error or RunResult.Cancelled,
            $"Unexpected result {summary.Result}: {summary.ErrorMessage}");
        Assert.NotEmpty(session.InstrumentSlots);
    }

    [Fact]
    public async Task Sample_program_runs_and_stamps_dut()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        Assert.NotEmpty(session.StepTree);

        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-42", Family: "demo"));

        using var resume = new CancellationTokenSource();
        var runTask = session.RunAsync();
        _ = Task.Run(async () =>
        {
            while (!resume.IsCancellationRequested && !runTask.IsCompleted)
            {
                if (session.IsAwaitingOperator)
                {
                    session.Resume();
                }

                await Task.Delay(20);
            }
        });

        var summary = await runTask;
        resume.Cancel();
        Assert.Equal("DUT-42", summary.DutSerial);
        Assert.True(
            summary.Result is RunResult.Passed or RunResult.Failed or RunResult.Error,
            $"Unexpected result {summary.Result}: {summary.ErrorMessage}");
        Assert.NotEmpty(summary.Samples);
        Assert.NotEmpty(summary.Steps);
    }

    [Fact]
    public async Task Operator_input_uses_interaction_contract_and_resume()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-PROMPT", Family: "demo"));

        var progress = new Progress<OpenTapProgress>();
        OperatorInteractionRequest? seenRequest = null;
        var awaiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        progress.ProgressChanged += (_, p) =>
        {
            if (p.AwaitingOperator)
            {
                seenRequest = p.InteractionRequest ?? session.PendingInteraction;
                awaiting.TrySetResult(true);
            }
        };

        var runTask = session.RunAsync(progress);
        var sawPrompt = await Task.WhenAny(awaiting.Task, Task.Delay(TimeSpan.FromSeconds(30))) == awaiting.Task;
        Assert.True(sawPrompt, "Expected operator input pause.");
        Assert.True(session.IsAwaitingOperator);
        Assert.NotNull(session.PendingInteraction);
        Assert.Contains(session.PendingInteraction!.Fields, f => f.Id == "fixtureId");
        Assert.NotNull(seenRequest);
        Assert.Equal("fixtureId", seenRequest!.Fields[0].Id);

        session.Resume(OperatorInteractionResponse.Continue(
            session.PendingInteraction.Id,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["fixtureId"] = "HOST-TEST" }));
        var summary = await runTask;
        Assert.False(session.IsAwaitingOperator);
        Assert.Null(session.PendingInteraction);
        Assert.True(
            summary.Result is RunResult.Passed or RunResult.Failed or RunResult.Error or RunResult.Cancelled,
            $"Unexpected result {summary.Result}: {summary.ErrorMessage}");
    }

    [Fact]
    public async Task Operator_prompt_pauses_until_resume()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-PROMPT", Family: "demo"));

        var progress = new Progress<OpenTapProgress>();
        var awaiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        progress.ProgressChanged += (_, p) =>
        {
            if (p.AwaitingOperator)
            {
                awaiting.TrySetResult(true);
            }
        };

        var runTask = session.RunAsync(progress);
        var sawPrompt = await Task.WhenAny(awaiting.Task, Task.Delay(TimeSpan.FromSeconds(30))) == awaiting.Task;
        Assert.True(sawPrompt, "Expected operator prompt pause.");
        Assert.True(session.IsAwaitingOperator);
        session.Resume();
        var summary = await runTask;
        Assert.False(session.IsAwaitingOperator);
        Assert.True(
            summary.Result is RunResult.Passed or RunResult.Failed or RunResult.Error or RunResult.Cancelled,
            $"Unexpected result {summary.Result}: {summary.ErrorMessage}");
    }

    [Fact]
    public async Task Abort_during_run_returns_cancelled()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string>()),
            new DutIdentity("DUT-ABORT"));

        var run = session.RunAsync();
        _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            // Unblock operator prompt if hit before abort.
            if (session.IsAwaitingOperator)
            {
                session.Resume();
            }

            await Task.Delay(30);
            session.Abort(safetyStop: true);
        });
        var summary = await run;
        Assert.Equal(RunResult.Cancelled, summary.Result);
    }

    [Fact]
    public async Task Debug_overlay_toggles_step_enabled()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        var path = session.StepTree.SelectMany(Flatten).First(n => n.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase)).Path;
        Assert.True(session.TrySetStepEnabled(path, false));
        Assert.True(session.TrySetAcquireSettings(path, 8, 1));
        Assert.True(session.TryRebindDmmResource("MOCK::INSTR1"));
    }

    [Fact]
    public void SampleProgramFactory_saves_tapplan()
    {
        var dir = Path.Combine(Path.GetTempPath(), "opentap-sample-" + Guid.NewGuid().ToString("N"));
        try
        {
            SampleProgramFactory.SaveBeside(dir);
            Assert.True(File.Exists(Path.Combine(dir, SampleProgramFactory.EmbeddedName)));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Sample_program_exposes_generic_instrument_slots()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        Assert.NotEmpty(session.InstrumentSlots);
        Assert.Contains(session.InstrumentSlots, s =>
            s.TypeName.Contains("MockDmm", StringComparison.OrdinalIgnoreCase)
            || s.Name.Contains("DMM", StringComparison.OrdinalIgnoreCase));
        Assert.True(session.TryBindSlotResource(session.InstrumentSlots[0].Name, "MOCK::INSTR9"));
        Assert.Equal("MOCK::INSTR9", session.InstrumentSlots[0].ResourceName);
    }

    [Fact]
    public async Task EnsurePlugins_accepts_settings_plugin_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "opentap-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settings = new AppSettings { OpenTapPluginDirectories = [dir] };
            var session = new OpenTapSession(settings);
            await session.LoadSampleProgramAsync();
            Assert.NotEmpty(session.StepTree);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static IEnumerable<OpenTapStepNode> Flatten(OpenTapStepNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }
}
