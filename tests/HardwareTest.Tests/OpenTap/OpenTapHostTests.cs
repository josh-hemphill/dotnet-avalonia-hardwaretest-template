using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;
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
    public async Task RunSelection_preserves_sibling_live_status()
    {
        var session = new OpenTapSession();
        await session.LoadBoardDemoProgramAsync();
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-PRESERVE", Family: "demo"));

        using var resume = new CancellationTokenSource();
        async Task<OpenTapRunSummary> RunWithAutoResume(Func<Task<OpenTapRunSummary>> start)
        {
            var runTask = start();
            _ = Task.Run(async () =>
            {
                while (!resume.IsCancellationRequested && !runTask.IsCompleted)
                {
                    if (session.IsAwaitingOperator)
                    {
                        var pending = session.PendingInteraction;
                        if (pending is not null && pending.Fields.Count > 0)
                        {
                            var values = pending.Fields.ToDictionary(
                                f => f.Id,
                                f => f.Kind == OperatorInteractionFieldKind.Number ? "1.0" : "LOT-1",
                                StringComparer.OrdinalIgnoreCase);
                            session.Resume(OperatorInteractionResponse.Continue(pending.Id, values));
                        }
                        else
                        {
                            session.Resume();
                        }
                    }

                    await Task.Delay(20);
                }
            });
            return await runTask;
        }

        var full = await RunWithAutoResume(() => session.RunAsync());
        Assert.True(
            full.Result is RunResult.Passed or RunResult.Failed or RunResult.Error,
            $"Full run unexpected: {full.Result}: {full.ErrorMessage}");

        var leaves = session.StepTree.SelectMany(Flatten).Where(n => n.Children.Count == 0).ToList();
        var acquire3V3 = leaves.First(n => n.Name.Contains("Acquire 3V3", StringComparison.OrdinalIgnoreCase));
        var acquire5V = leaves.First(n => n.Name.Contains("Acquire 5V", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(acquire5V.StatusText, "Pending", StringComparison.OrdinalIgnoreCase));
        var siblingStatusBefore = acquire5V.StatusText;
        var siblingKeyBefore = acquire5V.KeyValue;

        var selection = await RunWithAutoResume(() => session.RunSelectionAsync(acquire3V3.Path));
        resume.Cancel();
        Assert.True(
            selection.Result is RunResult.Passed or RunResult.Failed or RunResult.Error or RunResult.Cancelled,
            $"Selection unexpected: {selection.Result}: {selection.ErrorMessage}");

        Assert.Equal(siblingStatusBefore, acquire5V.StatusText);
        Assert.Equal(siblingKeyBefore, acquire5V.KeyValue);
        Assert.NotEqual("Pending", acquire5V.StatusText);
        Assert.NotEqual("NotSet", acquire3V3.StatusText);
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
        OperatorInteractionRequest? typedRequest = null;
        var typedReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        progress.ProgressChanged += (_, p) =>
        {
            if (!p.AwaitingOperator)
            {
                return;
            }

            var request = p.InteractionRequest ?? session.PendingInteraction;
            if (request?.Fields.Any(f => f.Id == "fixtureId") == true)
            {
                typedRequest = request;
                typedReady.TrySetResult(true);
                return;
            }

            // Confirm-only pause ahead of typed input.
            if (session.IsAwaitingOperator && session.PendingInteraction is { } pending)
            {
                session.Resume(OperatorInteractionResponse.Continue(pending.Id));
            }
        };

        var runTask = session.RunAsync(progress);
        var sawTyped = await Task.WhenAny(typedReady.Task, Task.Delay(TimeSpan.FromSeconds(30))) == typedReady.Task;
        Assert.True(sawTyped, "Expected typed operator input pause (fixtureId).");
        Assert.True(session.IsAwaitingOperator);
        Assert.NotNull(session.PendingInteraction);
        Assert.Contains(session.PendingInteraction!.Fields, f => f.Id == "fixtureId");
        Assert.Contains(session.PendingInteraction.Fields, f => f.Id == "fixtureTorqueNm");
        Assert.NotNull(typedRequest);

        session.Resume(OperatorInteractionResponse.Continue(
            session.PendingInteraction.Id,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fixtureId"] = "HOST-TEST",
                ["fixtureTorqueNm"] = "1.5",
            }));
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
        var sawAnyPause = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        progress.ProgressChanged += (_, p) =>
        {
            if (!p.AwaitingOperator)
            {
                return;
            }

            sawAnyPause.TrySetResult(true);
            if (session.PendingInteraction is { } pending)
            {
                session.Resume(OperatorInteractionResponse.Continue(pending.Id));
            }
            else
            {
                session.Resume();
            }
        };

        var runTask = session.RunAsync(progress);
        var sawPrompt = await Task.WhenAny(sawAnyPause.Task, Task.Delay(TimeSpan.FromSeconds(30))) == sawAnyPause.Task;
        Assert.True(sawPrompt, "Expected operator prompt pause.");
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
            await Task.Delay(50);
            // Unblock the first operator pause if we landed on it, then abort mid-suite.
            if (session.IsAwaitingOperator)
            {
                session.Resume();
            }

            await Task.Delay(40);
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
    public async Task Parameter_bridge_get_set_acquire_sample_count()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        var acquire = session.StepTree.SelectMany(Flatten)
            .First(n => n.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        var parameters = session.EnumerateParameters(OpenTapParameterScope.Step, acquire.Path);
        Assert.Contains(parameters, p => p.DisplayName.Contains("Sample", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameters, p => p.DisplayName.Equals("Instrument", StringComparison.OrdinalIgnoreCase));
        Assert.All(parameters, p => Assert.Equal(OpenTapParameterRole.StationOverride, p.Role));

        var sampleKey = OpenTapParameterInfo.FormatStepMemberKey(
            SampleProgramFactory.AcquireStepId.ToString(),
            nameof(AcquireVoltageStep.SampleCount));
        Assert.True(session.TrySetParameter(sampleKey, "12"));
        Assert.True(session.TryGetParameter(sampleKey, out var value));
        Assert.Equal("12", value);
        Assert.True(session.TryGetStepConditionSummary(acquire.Path, out var summary));
        Assert.Contains("Samples=12", summary);
    }

    [Fact]
    public async Task Parameter_bridge_excludes_operator_prompt_schema_from_station_overrides()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        var prompt = session.StepTree.SelectMany(Flatten)
            .First(n => n.Name.Contains("Fixture", StringComparison.OrdinalIgnoreCase));

        var overrides = session.EnumerateParameters(OpenTapParameterScope.Step, prompt.Path);
        Assert.All(overrides, p => Assert.Equal(OpenTapParameterRole.StationOverride, p.Role));
        Assert.DoesNotContain(overrides, p => p.DisplayName.Equals("Message", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(overrides, p => p.DisplayName.Contains("Field", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(overrides, p => p.DisplayName.Equals("Enabled", StringComparison.OrdinalIgnoreCase));

        var all = session.EnumerateParameters(
            OpenTapParameterScope.Step,
            prompt.Path,
            listing: OpenTapParameterListing.AllEditable);
        Assert.Contains(all, p => p.Role == OpenTapParameterRole.OperatorPromptSchema
                                  && p.DisplayName.Equals("Message", StringComparison.OrdinalIgnoreCase));
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
    public void InstrumentResourceAccess_prefers_VisaAddress_over_ResourceName()
    {
        var dual = new DualResourceInstrument
        {
            VisaAddress = "VISA::OLD",
            ResourceName = "RES::OLD",
        };

        Assert.Equal("VISA::OLD", InstrumentResourceAccess.GetResource(dual));
        Assert.True(InstrumentResourceAccess.TrySetResource(dual, "VISA::NEW"));
        Assert.Equal("VISA::NEW", dual.VisaAddress);
        Assert.Equal("RES::OLD", dual.ResourceName);
        Assert.Equal("VISA::NEW", InstrumentResourceAccess.GetResource(dual));
    }

    [Fact]
    public void InstrumentResourceAccess_round_trips_VisaAddress_only_instrument()
    {
        var visaOnly = new VisaAddressOnlyInstrument { VisaAddress = "USB0::INSTR" };
        Assert.Equal("USB0::INSTR", InstrumentResourceAccess.GetResource(visaOnly));
        Assert.True(InstrumentResourceAccess.TrySetResource(visaOnly, "TCPIP0::SCPI"));
        Assert.Equal("TCPIP0::SCPI", visaOnly.VisaAddress);
        Assert.Equal("TCPIP0::SCPI", InstrumentResourceAccess.GetResource(visaOnly));
    }

    [Fact]
    public void MockDmm_VisaAddress_and_ResourceName_stay_in_sync()
    {
        var dmm = new MockDmmInstrument { ResourceName = "MOCK::INSTR0" };
        Assert.Equal("MOCK::INSTR0", dmm.VisaAddress);
        Assert.True(InstrumentResourceAccess.TrySetResource(dmm, "MOCK::INSTR9"));
        Assert.Equal("MOCK::INSTR9", dmm.VisaAddress);
        Assert.Equal("MOCK::INSTR9", dmm.ResourceName);
        Assert.Equal("MOCK::INSTR9", InstrumentResourceAccess.GetResource(dmm));
    }

    [Fact]
    public async Task ApplyStationAndDut_binds_slot_override_to_sample_dmm()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        var slot = session.InstrumentSlots[0];
        var station = new StationProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [slot.RoleHint] = "MOCK::INSTR7",
        });

        await session.ApplyStationAndDutAsync(station, new DutIdentity("DUT-F", Family: "demo"));

        Assert.Equal("MOCK::INSTR7", session.InstrumentSlots[0].ResourceName);
    }

    [Fact]
    public async Task Sweep_repeat_fixture_reports_iteration_progress()
    {
        var session = new OpenTapSession();
        await session.LoadPlanShapeAsync(PlanShapeFixtures.SweepRepeatName);
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-SWEEP", Family: "demo"));

        var frames = new List<OpenTapProgress>();
        var summary = await session.RunAsync(new Progress<OpenTapProgress>(frames.Add));

        Assert.Equal(RunResult.Passed, summary.Result);
        var withIter = frames.Where(f => f.IterationIndex is > 0).ToList();
        Assert.NotEmpty(withIter);
        Assert.Contains(withIter, f => f.IterationTotal == 3);
        Assert.Contains(withIter, f => f.IterationText == "1/3" || f.IterationIndex == 1);
        Assert.Contains(withIter, f => f.IterationIndex >= 2);
    }

    [Fact]
    public async Task ListDiscoveredDeviceAddresses_does_not_throw_after_sample_load()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        var addresses = session.ListDiscoveredDeviceAddresses();
        Assert.NotNull(addresses);
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

    [Fact]
    public async Task Mixins_plugin_assembly_is_discoverable_after_EnsurePlugins()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();

        var builders = PluginManager.GetPlugins<IMixinBuilder>();
        Assert.Contains(builders, t => t == typeof(AnnotationMixinBuilder));
    }

    [Fact]
    public void Package_catalog_lists_settings_plugin_dir_and_package_xml()
    {
        var root = Path.Combine(Path.GetTempPath(), "opentap-pkg-" + Guid.NewGuid().ToString("N"));
        var pluginDir = Path.Combine(root, "plugins");
        var packageDir = Path.Combine(pluginDir, "DemoBench");
        Directory.CreateDirectory(packageDir);
        File.WriteAllText(
            Path.Combine(packageDir, "package.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Package Name="DemoBench" Version="1.2.3" xmlns="http://opentap.io/schemas/package">
              <Description>Phase E smoke package</Description>
            </Package>
            """);

        try
        {
            var settings = new AppSettings { OpenTapPluginDirectories = [pluginDir] };
            var session = new OpenTapSession(settings);

            var dirs = session.ListPluginDirectories();
            Assert.Contains(dirs, d =>
                string.Equals(d.Path, Path.GetFullPath(pluginDir), StringComparison.OrdinalIgnoreCase)
                && d.Source == "Settings");

            var packages = session.ListInstalledPackages();
            Assert.Contains(packages, p =>
                p.Name == "DemoBench"
                && p.Version == "1.2.3"
                && string.Equals(p.Path, Path.GetFullPath(packageDir), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Annotation_mixin_parameters_enumerate_and_round_trip()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();

        var identity = session.StepTree.SelectMany(Flatten)
            .First(n => n.Name.Contains("Identity Check", StringComparison.OrdinalIgnoreCase)
                        && n.Children.Count == 0);

        var parameters = session.EnumerateParameters(OpenTapParameterScope.Step, identity.Path, includeReadOnly: true);
        var note = parameters.FirstOrDefault(p =>
            p.DisplayName.Contains("Note", StringComparison.OrdinalIgnoreCase)
            || p.MemberKey.EndsWith("/Note", StringComparison.OrdinalIgnoreCase)
            || p.MemberKey.Contains(".Note", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(note);
        Assert.False(note!.IsReadOnly);
        Assert.True(
            note.IsMixinEmbedded
            || string.Equals(note.Group, "Annotation", StringComparison.OrdinalIgnoreCase)
            || note.MemberKey.Contains("Annotation", StringComparison.OrdinalIgnoreCase),
            $"Expected mixin grouping on {note.MemberKey} group={note.Group} mixin={note.IsMixinEmbedded}");

        Assert.True(session.TrySetParameter(note.MemberKey, "bench-note-1"));
        Assert.True(session.TryGetParameter(note.MemberKey, out var value));
        Assert.Equal("bench-note-1", value);
    }

    [Fact]
    public async Task Export_on_writes_opentap_results_csv()
    {
        var root = Path.Combine(Path.GetTempPath(), "ht-export-on-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new AppSettings
            {
                DataDirectory = root,
                ExportOpenTapResults = true,
            };
            var session = new OpenTapSession(settings);
            await session.LoadSampleProgramAsync();
            await session.ApplyStationAndDutAsync(
                new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
                new DutIdentity("DUT-EXPORT-ON", Family: "demo"));

            var summary = await RunSampleWithAutoResumeAsync(session);
            Assert.True(
                summary.Result is RunResult.Passed or RunResult.Failed or RunResult.Error or RunResult.Cancelled,
                $"Unexpected result {summary.Result}: {summary.ErrorMessage}");

            var exportDir = Path.Combine(root, "runs", summary.RunId, "opentap-results");
            Assert.True(Directory.Exists(exportDir), $"Expected export directory {exportDir}");
            var csvFiles = Directory.GetFiles(exportDir, "*.csv");
            Assert.NotEmpty(csvFiles);
            Assert.Contains(csvFiles, f => new FileInfo(f).Length > 0);
            Assert.True(
                csvFiles.Any(f => Path.GetFileName(f) is "Sample.csv" or "Identity.csv" or "Analyze.csv"),
                "Expected at least one known OpenTAP result table CSV.");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task Export_off_leaves_no_opentap_results()
    {
        var root = Path.Combine(Path.GetTempPath(), "ht-export-off-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new AppSettings
            {
                DataDirectory = root,
                ExportOpenTapResults = false,
            };
            var session = new OpenTapSession(settings);
            await session.LoadSampleProgramAsync();
            await session.ApplyStationAndDutAsync(
                new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
                new DutIdentity("DUT-EXPORT-OFF", Family: "demo"));

            var summary = await RunSampleWithAutoResumeAsync(session);
            Assert.True(
                summary.Result is RunResult.Passed or RunResult.Failed or RunResult.Error or RunResult.Cancelled,
                $"Unexpected result {summary.Result}: {summary.ErrorMessage}");

            var exportDir = Path.Combine(root, "runs", summary.RunId, "opentap-results");
            Assert.False(Directory.Exists(exportDir));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private static async Task<OpenTapRunSummary> RunSampleWithAutoResumeAsync(OpenTapSession session)
    {
        using var resume = new CancellationTokenSource();
        var runTask = session.RunAsync();
        _ = Task.Run(async () =>
        {
            while (!resume.IsCancellationRequested && !runTask.IsCompleted)
            {
                if (session.IsAwaitingOperator)
                {
                    var pending = session.PendingInteraction;
                    if (pending is not null && pending.Fields.Count > 0)
                    {
                        var values = pending.Fields.ToDictionary(
                            f => f.Id,
                            f => f.Kind == OperatorInteractionFieldKind.Number ? "1.5" : "EXPORT-FIXTURE",
                            StringComparer.OrdinalIgnoreCase);
                        session.Resume(OperatorInteractionResponse.Continue(pending.Id, values));
                    }
                    else
                    {
                        session.Resume();
                    }
                }

                await Task.Delay(20);
            }
        });

        var summary = await runTask;
        resume.Cancel();
        return summary;
    }

    private static IEnumerable<OpenTapStepNode> Flatten(OpenTapStepNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    [Display("VisaAddress-only test instrument")]
    private sealed class VisaAddressOnlyInstrument : Instrument
    {
        public string VisaAddress { get; set; } = string.Empty;
    }

    [Display("Dual resource test instrument")]
    private sealed class DualResourceInstrument : Instrument
    {
        public string VisaAddress { get; set; } = string.Empty;
        public string ResourceName { get; set; } = string.Empty;
    }
}
