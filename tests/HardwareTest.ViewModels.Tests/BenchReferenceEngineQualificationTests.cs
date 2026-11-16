using System.Text.Json;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using HardwareTest.ViewModels.Tests.Time;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class BenchReferenceEngineQualificationTests
{
    [Fact]
    public void Execution_identity_is_the_loaded_engine_version_and_module_identity()
    {
        var loaded = typeof(global::OpenTap.TestPlan).Assembly;
        Assert.Equal($"{loaded.GetName().Version}/{loaded.ManifestModule.ModuleVersionId:D}", OpenTapBuildInfo.ExecutionEngineIdentity);
    }

    [Fact]
    public async Task Same_version_engine_change_requires_fresh_acquisition_and_preserves_old_run_and_reference_bytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "engine-qualification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var engine = "9.35.0.0/e11c807c-1111-4111-8111-111111111111";
            var rule = new BenchReferenceRule
            {
                RuleId = "voltage-rule",
                MetricKey = "VDC",
                ProducerStepId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ReferenceKey = "golden.voltage",
                Unit = "V",
                Tolerance = 0.01,
            };
            var path = Path.Combine(root, "engine-qualified.TapPlan"); File.WriteAllText(path, "<TestPlan />");
            File.WriteAllText(Path.ChangeExtension(path, ".program.json"), JsonSerializer.Serialize(
                new ProgramSidecar { ReferenceRules = [rule] }, ProgramCatalogJsonContext.Default.ProgramSidecar));
            var clock = new FakeClock(DateTimeOffset.UtcNow);
            var profiles = new FileBenchReferenceStore(root, clock);
            var runs = new FileRunStore(Path.Combine(root, "runs"));
            var tap = new FakeOpenTapSession
            {
                ReportSamples = false,
                SummarySamplesFactory = (started, completed) => [new StoredSample
                {
                    MetricKey = rule.MetricKey, ProducerStepId = rule.ProducerStepId, Channel = rule.MetricKey,
                    Unit = rule.Unit, Value = 1, StepPath = "Measure voltage", Timestamp = started + (completed - started) / 2,
                }],
            };
            var vm = new RunTestViewModel(tap, tap, tap, new OperatorSession(clock), new FakeRunControl(), new FakeReportService(),
                runs, new AppSettings { DataDirectory = root, UseMockVisa = true, IsEngineerDebugMode = true }, clock: clock, benchReferenceStore: profiles)
            { UiScheduler = action => action(), ExecutionEngineIdentityProvider = () => engine };
            vm.ProgramSelection.RequestPlanFilePath = _ => Task.FromResult<string?>(path);
            await vm.ProgramSelection.OpenPlanFileCommand.ExecuteAsync(); Assert.True(vm.IsSelectedProgramReady);

            var oldProfile = await Acquire("GOLDEN-OLD", "Original engine reference");
            await Choose(oldProfile);
            await Confirm("PRODUCT-OLD"); await vm.Run.RunCommand.ExecuteAsync();
            var oldRun = Assert.IsType<TestRunRecord>(await runs.LoadAsync(vm.LastRunId!));
            Assert.Equal(RunResult.Passed, oldRun.Result);
            Assert.Equal(oldProfile.Id, Assert.Single(oldRun.ReferenceUses).ReferenceId);
            var runPath = Path.Combine(runs.GetRunDirectory(oldRun.RunId), "run.json");
            var profilePath = Path.Combine(root, FileBenchReferenceStore.DirectoryName, oldProfile.Id + ".json");
            var originalRunBytes = File.ReadAllBytes(runPath); var originalProfileBytes = File.ReadAllBytes(profilePath);

            // Reloading the same loaded engine leaves the qualified selection usable.
            await vm.RefreshBenchReferencesAsync();
            Assert.Equal(oldProfile.Id, Assert.Single(vm.References.Rules).SelectedChoice!.Profile.Id);
            await Confirm("PRODUCT-SAME-ENGINE"); await vm.Run.RunCommand.ExecuteAsync();
            var stable = Assert.IsType<TestRunRecord>(await runs.LoadAsync(vm.LastRunId!));
            Assert.Equal(RunResult.Passed, stable.Result);
            Assert.Equal(oldRun.ReferenceContext, stable.ReferenceContext);

            // The version stays the same; only the actual binary's MVID changes.
            engine = "9.35.0.0/bb7b4c3f-2222-4222-8222-222222222222";
            var before = tap.RunCount;
            await vm.Run.RunCommand.ExecuteAsync();
            Assert.Equal(before, tap.RunCount); // Rechecks the effective engine even with a stale visible choice.
            Assert.Contains("requalify", vm.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("choose a compatible reference", vm.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("fresh golden-unit acquisition", vm.Status, StringComparison.OrdinalIgnoreCase);
            await vm.RefreshBenchReferencesAsync();
            Assert.Empty(Assert.Single(vm.References.Rules).Choices);
            Assert.False(vm.CanStartRun);
            Assert.Contains(vm.References.Profiles, item => item.Entry.Profile?.Id == oldProfile.Id);

            var replacement = await Acquire("GOLDEN-NEW", "Requalified engine reference");
            Assert.NotEqual(oldProfile.Context.ConfigurationSha256, replacement.Context.ConfigurationSha256);
            Assert.Equal(oldProfile.Context.PlanSha256, replacement.Context.PlanSha256);
            Assert.Equal(oldProfile.Context.SidecarSha256, replacement.Context.SidecarSha256);
            await Choose(replacement);
            await Confirm("PRODUCT-NEW"); await vm.Run.RunCommand.ExecuteAsync();
            var accepted = Assert.IsType<TestRunRecord>(await runs.LoadAsync(vm.LastRunId!));
            Assert.Equal(RunResult.Passed, accepted.Result);
            Assert.Equal(replacement.Id, Assert.Single(accepted.ReferenceUses).ReferenceId);
            Assert.Equal(originalRunBytes, File.ReadAllBytes(runPath));
            Assert.Equal(originalProfileBytes, File.ReadAllBytes(profilePath));
            Assert.Equal(2, (await profiles.ListAsync()).Count);

            async Task Confirm(string serial)
            {
                vm.SessionPanel.DutSerialInput = serial; vm.SessionPanel.OperatorInput = "Qualification technician";
                await vm.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            }
            async Task<BenchReferenceProfile> Acquire(string serial, string name)
            {
                vm.References.IsAcquisition = true; await Confirm(serial);
                await vm.Run.RunCommand.ExecuteAsync();
                var acquisition = Assert.IsType<TestRunRecord>(await runs.LoadAsync(vm.LastRunId!));
                Assert.Equal(RunExecutionScopes.ReferenceAcquisition, acquisition.ExecutionScope);
                clock.UtcNow = DateTimeOffset.UtcNow;
                var profile = BenchReferenceEvaluator.Capture(name, acquisition, [rule], clock.UtcNow);
                await profiles.SaveAsync(profile); return profile;
            }
            async Task Choose(BenchReferenceProfile profile)
            {
                vm.References.IsAcquisition = false; await vm.RefreshBenchReferencesAsync();
                var row = Assert.Single(vm.References.Rules);
                row.SelectedChoice = Assert.Single(row.Choices, choice => choice.Profile.Id == profile.Id);
                Assert.True(vm.References.IsReady);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
