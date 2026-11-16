using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.Tests.OpenTap;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class AlphaOperatorPersonaTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Operator_sees_overload_without_a_fake_zero_and_a_genuine_zero_remains_a_measurement(bool overload)
    {
        var programs = Path.Combine(AppContext.BaseDirectory, "Programs"); Directory.CreateDirectory(programs);
        var path = AlphaDevicePlanFixture.SaveOverloadPlan(programs, overload);
        var id = Path.GetFileNameWithoutExtension(path);
        var sidecar = Path.ChangeExtension(path, ".program.json");
        File.WriteAllText(sidecar, "{\"displayName\":\"Qualified DMM " + (overload ? "overload" : "zero") + " (simulated)\",\"requireSerial\":true,\"requireOperator\":false}");
        try
        {
            using var shown = E2EHarness.ShowMainWindow();
            var window = shown.Window; window.Width = 900; window.Height = 600;
            var main = shown.Main; main.NavigateToPageId("RunTest"); var run = main.RunTest;
            await run.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() => window.GetVisualDescendants().OfType<ComboBox>().Any(box => AutomationProperties.GetName(box) == "Program" && box.IsEffectivelyVisible));
            window.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "Program" && box.IsEffectivelyVisible).SelectedItem = run.ProgramSelection.Programs.Single(item => item.Id == id);
            await E2EHarness.WaitUntilAsync(() => Descendants(run.StepTree.FullHierarchy).Any(node => node.Name == "Commissioned voltage"));
            Type(window, window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "DutSerialBox" && box.IsEffectivelyVisible), overload ? "OVERLOAD-001" : "ZERO-001");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Confirm Session") && button.IsEffectivelyVisible));
            await E2EHarness.WaitUntilAsync(() => run.Workspace.ShowSteps);
            Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Run" && button.IsEffectivelyVisible));
            await E2EHarness.WaitUntilAsync(() => !run.IsRunning && !string.IsNullOrWhiteSpace(run.LastRunId));
            var leaf = Descendants(run.StepTree.FullHierarchy).Single(node => node.Name == "Commissioned voltage");
            Assert.Equal(overload ? "Fail" : "Pass", leaf.ChipText);
            if (overload) Assert.Contains("Overload: no valid measurement (V)", leaf.KeyValue);
            else Assert.Contains("0 V", leaf.KeyValue);
            var tag = overload ? "overload" : "true-zero";
            Capture(window, "operator-run-" + tag + "-900");
            main.NavigateToPageId("Results"); await main.Results.RefreshCommand.ExecuteAsync();
            main.Results.SelectedRun = main.Results.Runs.Single(record => record.RunId == run.LastRunId);
            await main.Results.OpenSelectedRunAsync(); await E2EHarness.PumpUiAsync();
            var record = main.Results.OpenedRun!;
            Assert.Equal(overload ? 0 : 1, record.Samples.Count);
            if (overload)
            {
                Assert.Contains(main.Results.StepDetails, detail => detail.Contains("Overload: no valid measurement", StringComparison.Ordinal));
                Assert.Contains("MOCK::AGILENT-OVERLOAD", Assert.Single(record.Steps).OutcomeDetail);
                Assert.Empty(main.Results.SampleDetails);
            }
            else { Assert.Equal(0, Assert.Single(record.Samples).Value); Assert.Equal("V", record.Samples[0].Unit); }
            Capture(window, "operator-results-" + tag + "-900");
            var line = main.Results.StepDetails.Single(detail => detail.Contains("Commissioned voltage", StringComparison.Ordinal));
            window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == line).BringIntoView();
            await E2EHarness.PumpUiAsync();
            Capture(window, "operator-results-" + tag + "-steps-900");
            var reportService = E2EHarness.RequireApp().Services.GetRequiredService<HardwareTest.Core.Reporting.IReportService>();
            var pdf = await reportService.GeneratePdfAsync(record);
            var reportText = AlphaDevicePlanFixture.ReadPdfText(pdf);
            if (overload) Assert.Contains("Overload: no valid measurement", reportText);
            else Assert.DoesNotContain("Overload", reportText);
            var evidence = Environment.GetEnvironmentVariable("HARDWARETEST_UI_EVIDENCE_DIRECTORY");
            if (evidence is not null)
            {
                var output = Path.Combine(evidence, "qualified-dmm-" + tag); Directory.CreateDirectory(output);
                File.Copy(pdf, Path.Combine(output, "status.pdf"), overwrite: true);
                File.WriteAllText(Path.Combine(output, "report.txt"), reportText);
                File.WriteAllText(Path.Combine(output, "run.json"), System.Text.Json.JsonSerializer.Serialize(record, HardwareTest.Core.Serialization.AppJsonContext.Default.TestRunRecord));
            }
        }
        finally { File.Delete(path); File.Delete(sidecar); }
    }

    [AvaloniaFact]
    public async Task Operator_pauses_between_real_device_operations_resumes_and_stops_while_paused()
    {
        var programs = Path.Combine(AppContext.BaseDirectory, "Programs");
        Directory.CreateDirectory(programs);
        var path = AlphaDevicePlanFixture.SavePausePlan(programs);
        var sidecar = Path.ChangeExtension(path, ".program.json");
        File.WriteAllText(sidecar, """{"displayName":"Alpha pause (simulated)","requireSerial":true,"requireOperator":false}""");
        var broker = new AlphaDevicePlanFixture.HeldReplyBroker();
        var settings = E2EHarness.RequireApp().Services.GetRequiredService<AppSettings>();
        var host = new OpenTapSession(settings, visaBroker: broker);
        try
        {
            using var shown = E2EHarness.ShowMainWindowWithSession(host);
            var window = shown.Window; window.Width = 900; window.Height = 600;
            var main = shown.Main; main.NavigateToPageId("RunTest");
            var run = main.RunTest;
            await run.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() => window.GetVisualDescendants().OfType<ComboBox>().Any(box => AutomationProperties.GetName(box) == "Program" && box.IsEffectivelyVisible));
            window.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "Program" && box.IsEffectivelyVisible).SelectedItem = run.ProgramSelection.Programs.Single(item => item.Id == "alpha-pause");
            await E2EHarness.WaitUntilAsync(() => Descendants(run.StepTree.FullHierarchy).Any(node => node.Name == "Following current"));
            Type(window, window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "DutSerialBox" && box.IsEffectivelyVisible), "PAUSE-001");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Confirm Session") && button.IsEffectivelyVisible));
            await E2EHarness.WaitUntilAsync(() => run.Workspace.ShowSteps);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                broker.Arm();
                await E2EHarness.WaitUntilAsync(() => window.GetVisualDescendants().OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Run" && button.IsEffectivelyVisible && button.IsEnabled));
                Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Run" && button.IsEffectivelyVisible));
                await E2EHarness.WaitUntilAsync(() => broker.Entered.Task.IsCompleted);
                if (attempt == 2)
                {
                    broker.Release.TrySetResult();
                }
                else
                {
                    Activate(window, window.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == "Pause" && button.IsEffectivelyVisible));
                    await E2EHarness.WaitUntilAsync(() => main.IsPaused && main.PauseResumeLabel == "Resume");
                    broker.Release.TrySetResult();
                    await E2EHarness.WaitUntilAsync(() => broker.Returned.Task.IsCompleted);
                    await Task.Delay(300); await E2EHarness.PumpUiAsync();
                    Assert.False(broker.Following.Task.IsCompleted);
                    Assert.True(run.IsRunning);
                    Assert.Equal(1, broker.Queries);
                    Capture(window, attempt == 0 ? "operator-run-paused-900" : "operator-run-paused-before-stop-900");
                    if (attempt == 0)
                        Activate(window, window.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == "Resume" && button.IsEffectivelyVisible));
                    else
                        Activate(window, window.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == run.HeaderStopLabel && button.IsEffectivelyVisible));
                }
                await E2EHarness.WaitUntilAsync(() => !run.IsRunning && !string.IsNullOrWhiteSpace(run.LastRunId));
                Assert.False(main.IsPaused);
                Assert.Equal(attempt + 1, broker.Opens); Assert.Equal(broker.Opens, broker.Closes);
                Assert.Equal(attempt == 1 ? 1 : 2, broker.Queries);
                var following = Descendants(run.StepTree.FullHierarchy).Single(node => node.Name == "Following current");
                Assert.Equal(attempt == 1 ? "Cancelled" : "Pass", following.ChipText);
                Assert.Equal(0, run.StepTree.SuiteFailedCount);
                Assert.Equal(attempt == 1 ? 1 : 0, run.StepTree.SuiteCancelledCount);
                Assert.Equal(0, run.StepTree.SuiteErrorCount);
                Assert.Equal(0, run.StepTree.SuitePendingCount);
                if (attempt != 1)
                {
                    Assert.Contains("Passed", run.Status, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("following.current", Descendants(run.StepTree.FullHierarchy).Single(node => node.Name == "Following current").KeyValue);
                }
                Capture(window, attempt == 0 ? "operator-run-resumed-complete-900" : attempt == 1 ? "operator-run-paused-stopped-900" : "operator-run-paused-retry-complete-900");
                if (attempt == 1)
                {
                    Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Needs attention") && button.IsEffectivelyVisible));
                    await E2EHarness.PumpUiAsync();
                    Assert.Equal("Cancelled", Assert.Single(run.StepTree.StepListItems, item => !item.IsHeader && item.Step is not null).ChipText);
                    Capture(window, "operator-run-cancelled-attention-900");
                    Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "All") && button.IsEffectivelyVisible));
                    main.NavigateToPageId("Results"); await main.Results.RefreshCommand.ExecuteAsync();
                    main.Results.SelectedRun = main.Results.Runs.Single(result => result.RunId == run.LastRunId);
                    await main.Results.OpenSelectedRunAsync();
                    Assert.Contains(main.Results.StepDetails, detail => detail.Contains("[Following current", StringComparison.Ordinal) && detail.Contains("] CANCELLED", StringComparison.Ordinal));
                    Assert.DoesNotContain(main.Results.StepDetails, detail => detail.Contains("] FAIL", StringComparison.Ordinal));
                    Capture(window, "operator-results-paused-stopped-900");
                    main.NavigateToPageId("RunTest");
                }
            }
        }
        finally { host.Abort(); File.Delete(path); File.Delete(sidecar); }
    }

    [AvaloniaTheory]
    [InlineData(900, 600)]
    [InlineData(1280, 800)]
    public async Task Operator_commissions_mixed_bench_runs_and_QA_inspects_results(double width, double height)
    {
        var programs = Path.Combine(AppContext.BaseDirectory, "Programs");
        Directory.CreateDirectory(programs);
        var path = AlphaDevicePlanFixture.SavePlan(programs);
        File.WriteAllText(path, File.ReadAllText(path).Replace("MOCK::keysight.34461a", "", StringComparison.Ordinal));
        var sidecar = Path.ChangeExtension(path, ".program.json");
        File.WriteAllText(sidecar, """{"displayName":"Mixed alpha bench (simulated)","dutFamily":"alpha-demo","requireSerial":true,"requireOperator":false,"selectionIncludesCleanup":false}""");
        try
        {
            using var shown = E2EHarness.ShowMainWindow();
            var window = shown.Window;
            window.Width = width; window.Height = height;
            var main = shown.Main;
            main.NavigateToPageId("Instruments");
            var instruments = Assert.IsType<InstrumentsViewModel>(main.CurrentPage);
            await instruments.RefreshSlotsCommand.ExecuteAsync();
            instruments.FocusProgram("mixed-alpha");
            await E2EHarness.PumpUiAsync();
            Assert.Equal(5, instruments.VisibleSlots.Count);
            Assert.Contains(instruments.VisibleSlots, slot => slot.TypeName.Contains("Keithley", StringComparison.Ordinal));
            Capture(window, $"operator-instruments-unbound-{width}");
            var slots = window.GetVisualDescendants().OfType<ComboBox>().Single(list => ReferenceEquals(list.ItemsSource, instruments.VisibleSlots));
            slots.SelectedIndex = 0;
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).First(button => Equals(button.Content, "Discover VISA")));
            await E2EHarness.WaitUntilAsync(() => !instruments.IsBusy && instruments.DiscoveredVisa.Count > 0);
            var resources = window.GetVisualDescendants().OfType<ComboBox>().Single(list => ReferenceEquals(list.ItemsSource, instruments.DiscoveredVisa));
            Assert.True(resources.Focus());
            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            window.KeyRelease(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            await E2EHarness.PumpUiAsync();
            Assert.NotNull(instruments.SelectedVisa);
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => Equals(button.Content, "Apply selected resource")));
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => Equals(button.Content, "Query *IDN?")));
            await E2EHarness.WaitUntilAsync(() => !instruments.IsBusy && instruments.SelectedVisa!.HasIdn);
            Assert.True(instruments.SelectedSlot!.HasLastIdn);
            Capture(window, $"operator-instruments-identified-{width}");
            await E2EHarness.PumpUiAsync();
            window.GetVisualDescendants().OfType<Expander>().Single(control => AutomationProperties.GetName(control) == "Selected instrument slot details").IsExpanded = true;
            await E2EHarness.PumpUiAsync();
            var edit = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Station override address");
            Type(window, edit, "MOCK::REBOUND");
            Assert.False(instruments.SelectedSlot!.HasLastIdn);
            Assert.Empty(instruments.SelectedSlot.LastIdnSummary);
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => Equals(button.Content, "Save overrides")));
            await E2EHarness.WaitUntilAsync(() => instruments.Status.Contains("Saved", StringComparison.OrdinalIgnoreCase));
            Capture(window, $"operator-instruments-rebound-{width}");
            var slotName = instruments.SelectedSlot.SlotName;
            Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Refresh plan slots" && button.IsEffectivelyVisible));
            await E2EHarness.PumpUiAsync();
            Assert.Equal("mixed-alpha", instruments.SelectedSlot!.PlanId);
            Assert.Equal(slotName, instruments.SelectedSlot.SlotName);
            Assert.Equal("MOCK::REBOUND", instruments.SelectedSlot.EffectiveResource);
            Assert.False(instruments.SelectedSlot.HasLastIdn);
            Capture(window, $"operator-instruments-rebound-unidentified-{width}");
            edit = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Station override address");
            // Restore a simulated resource; station edits must not rewrite the plan's driver identity.
            Type(window, edit, "MOCK::keysight.34461a");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => Equals(button.Content, "Save overrides")));
            await E2EHarness.PumpUiAsync();
            Capture(window, $"operator-instruments-ready-{width}");
            main.NavigateToPageId("RunTest");
            var run = E2EHarness.RunTestVm(main);
            await run.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() => window.GetVisualDescendants().OfType<ComboBox>().Where(control => control.IsEffectivelyVisible).Any(box => AutomationProperties.GetName(box) == "Program"), TimeSpan.FromSeconds(10), "Run Program picker did not appear; page=" + main.CurrentPage?.GetType().Name);
            Capture(window, $"operator-run-programs-{width}");
            var program = window.GetVisualDescendants().OfType<ComboBox>().Where(control => control.IsEffectivelyVisible).Single(box => AutomationProperties.GetName(box) == "Program");
            program.SelectedItem = run.ProgramSelection.Programs.Single(item => item.Id == "mixed-alpha");
            await E2EHarness.WaitUntilAsync(() => run.StepTree.FullHierarchy.SelectMany(node => node.Children).Any(node => node.Name.Contains("keysight.34461a", StringComparison.Ordinal)), failureMessage: "Selected alpha plan did not load: " + run.Status);
            await E2EHarness.PumpUiAsync();
            Capture(window, $"operator-run-awaiting-session-{width}");
            var serial = window.GetVisualDescendants().OfType<TextBox>().Where(control => control.IsEffectivelyVisible).Single(box => box.Name == "DutSerialBox");
            Type(window, serial, "ALPHA-PERSONA-001");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => Equals(button.Content, "Confirm Session")));
            await E2EHarness.WaitUntilAsync(() => run.Workspace.ShowSteps);
            Capture(window, $"operator-run-ready-{width}");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => AutomationProperties.GetName(button) == "Run"));
            await E2EHarness.PumpUiAsync();
            Capture(window, $"operator-run-after-click-{width}");
            await E2EHarness.WaitUntilAsync(() => run.IsRunning || !string.IsNullOrWhiteSpace(run.LastRunId) || run.HasBanner, TimeSpan.FromSeconds(5));
            Assert.True(run.IsRunning || !string.IsNullOrWhiteSpace(run.LastRunId), run.Status + " / " + run.BannerMessage);
            await E2EHarness.WaitUntilAsync(() => !run.IsRunning && !string.IsNullOrWhiteSpace(run.LastRunId), TimeSpan.FromSeconds(60));
            Assert.Contains("finished", run.Status, StringComparison.OrdinalIgnoreCase);
            foreach (var item in run.StepTree.StepListItems.Where(item => item.Step is not null && item.Step.Children.Count == 0))
            {
                var step = item.Step!;
                var driver = AlphaDevicePlanFixture.Cases.FirstOrDefault(device => step.Name.Contains(device.Driver, StringComparison.Ordinal));
                if (driver.Driver is not null) Assert.Contains(driver.Driver, step.KeyValue, StringComparison.Ordinal);
            }
            Capture(window, $"operator-run-complete-{width}");
            main.NavigateToPageId("Results");
            await main.Results.RefreshCommand.ExecuteAsync();
            main.Results.SelectedRun = main.Results.Runs.Single(result => result.RunId == run.LastRunId);
            await main.Results.OpenSelectedRunAsync();
            await E2EHarness.PumpUiAsync();
            Capture(window, $"operator-results-{width}");
            Assert.Equal(5, main.Results.OpenedRun!.Samples.Count);
        }
        finally { File.Delete(path); File.Delete(sidecar); }
    }

    [AvaloniaFact]
    public async Task Service_engineer_repairs_commissioning_stops_a_prompt_then_inspects_a_failed_run()
    {
        var programs = Path.Combine(AppContext.BaseDirectory, "Programs");
        Directory.CreateDirectory(programs);
        var path = Path.Combine(programs, "alpha-service.TapPlan");
        var sidecar = Path.ChangeExtension(path, ".program.json");
        void Save(bool commissioned)
        {
            var instrument = new HardwareTest.OpenTap.Plugins.Instruments.RegisteredInstrument
            {
                Name = "Commissioned Agilent DMM",
                DriverId = "keysight.34401a",
                VisaAddress = "MOCK::AGILENT",
                Commissioning = commissioned ? "GpibScpiConfirmed" : ""
            };
            var plan = new global::OpenTap.TestPlan();
            plan.ChildTestSteps.Add(new HardwareTest.OpenTap.Plugins.Basic.OperatorPromptStep { Name = "Confirm serviced fixture", Message = "Simulated service workflow: confirm fixture before measurement." });
            plan.ChildTestSteps.Add(new HardwareTest.OpenTap.Plugins.Instruments.DeviceOperationStep
            {
                Name = "Commissioned voltage",
                Instrument = instrument,
                Channel = "agilent.voltage",
                Operation = "DmmMeasureVoltageDc",
                LimitHigh = 0.5
            });
            plan.Save(path);
        }
        Save(false);
        File.WriteAllText(sidecar, """{"displayName":"Alpha service (simulated)","requireSerial":true,"requireOperator":false}""");
        try
        {
            using var shown = E2EHarness.ShowMainWindow();
            var window = shown.Window; window.Width = 900; window.Height = 600;
            var main = shown.Main;
            main.NavigateToPageId("Instruments");
            var instruments = Assert.IsType<InstrumentsViewModel>(main.CurrentPage);
            await instruments.RefreshSlotsCommand.ExecuteAsync(); instruments.FocusProgram("alpha-service");
            Assert.False(string.IsNullOrWhiteSpace(main.RunTest.Session.ProgramId));
            Assert.NotEqual("alpha-service", main.RunTest.Session.ProgramId);
            instruments.SelectedSlot = Assert.Single(instruments.VisibleSlots);
            await E2EHarness.PumpUiAsync();
            Assert.Contains(instruments.VisibleSlots, slot => !string.IsNullOrWhiteSpace(slot.ConfigurationIssue));
            Capture(window, "operator-instruments-configuration-error-900");
            // A service technician obtains the repaired plan from Authoring; operator cannot silently assert commissioning.
            Save(true);
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => Equals(button.Content, "Refresh plan slots")));
            await E2EHarness.WaitUntilAsync(() => !instruments.IsBusy && instruments.VisibleSlots.Count == 1 && string.IsNullOrWhiteSpace(instruments.VisibleSlots[0].ConfigurationIssue));
            Assert.Equal("Alpha service (simulated)", instruments.PlanFilter);
            var repaired = Assert.Single(instruments.VisibleSlots);
            Assert.Equal("alpha-service", repaired.PlanId);
            Assert.Equal("Commissioned Agilent DMM", repaired.SlotName);
            Assert.Contains("34401A", repaired.TypeName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("MOCK::AGILENT", repaired.EffectiveResource);
            Assert.False(repaired.Readiness.BlocksRun);
            Assert.Same(repaired, instruments.SelectedSlot);
            var repairedPlan = global::OpenTap.TestPlan.Load(path);
            var repairedDevice = Assert.IsType<HardwareTest.OpenTap.Plugins.Instruments.DeviceOperationStep>(repairedPlan.ChildTestSteps[1]).Instrument;
            Assert.Equal("keysight.34401a", repairedDevice.DriverId);
            Assert.Equal("GpibScpiConfirmed", repairedDevice.Commissioning);
            Assert.False(repaired.SupportsIdentityQuery);
            Assert.DoesNotContain("Query *IDN?", instruments.CommissionTitle);
            Assert.Contains("not advertised", instruments.CommissionHint);
            Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Query *IDN?") && button.IsEffectivelyVisible));
            await E2EHarness.PumpUiAsync();
            Assert.Contains("no query was sent", instruments.Status);
            Assert.False(repaired.HasLastIdn);
            Capture(window, "operator-instruments-commissioned-900");
            main.NavigateToPageId("RunTest");
            var run = E2EHarness.RunTestVm(main);
            await run.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() => window.GetVisualDescendants().OfType<ComboBox>().Where(control => control.IsEffectivelyVisible).Any(box => AutomationProperties.GetName(box) == "Program"));
            window.GetVisualDescendants().OfType<ComboBox>().Where(control => control.IsEffectivelyVisible).Single(box => AutomationProperties.GetName(box) == "Program").SelectedItem = run.ProgramSelection.Programs.Single(item => item.Id == "alpha-service");
            await E2EHarness.WaitUntilAsync(() => run.StepTree.FullHierarchy.Any(node => node.Name == "Confirm serviced fixture"), failureMessage: "Service plan did not load: " + run.Status);
            Capture(window, "operator-run-blocked-900");
            Type(window, window.GetVisualDescendants().OfType<TextBox>().Where(control => control.IsEffectivelyVisible).Single(box => box.Name == "DutSerialBox"), "SERVICE-001");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => Equals(button.Content, "Confirm Session")));
            await E2EHarness.WaitUntilAsync(() => run.Workspace.ShowSteps);
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => AutomationProperties.GetName(button) == "Run"));
            await E2EHarness.WaitUntilAsync(() => run.Interaction.IsAwaitingOperator);
            Capture(window, "operator-run-running-prompt-900");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).First(button => AutomationProperties.GetName(button) == run.HeaderStopLabel));
            await E2EHarness.WaitUntilAsync(() => !run.IsRunning);
            Assert.False(run.Interaction.IsAwaitingOperator);
            Capture(window, "operator-run-stopped-900");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => AutomationProperties.GetName(button) == "Run"));
            await E2EHarness.WaitUntilAsync(() => run.Interaction.IsAwaitingOperator);
            Activate(window, window.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible).Single(button => AutomationProperties.GetName(button) == "Continue operator prompt"));
            await E2EHarness.WaitUntilAsync(() => !run.IsRunning && !string.IsNullOrWhiteSpace(run.LastRunId));
            Assert.Contains("Fail", run.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Fail", Descendants(run.StepTree.FullHierarchy).Single(node => node.Name == "Commissioned voltage").ChipText);
            Assert.Equal(1, run.StepTree.SuiteFailedCount);
            Assert.Equal(0, run.StepTree.SuiteErrorCount);
            Assert.Equal(0, run.StepTree.SuiteCancelledCount);
            Capture(window, "operator-run-failed-900");
            main.NavigateToPageId("Results"); await main.Results.RefreshCommand.ExecuteAsync();
            main.Results.SelectedRun = main.Results.Runs.Single(result => result.RunId == run.LastRunId);
            await main.Results.OpenSelectedRunAsync(); await E2EHarness.PumpUiAsync();
            Assert.Single(main.Results.OpenedRun!.Samples);
            Capture(window, "operator-results-failed-900");
            var lastDetail = main.Results.StepDetails.Last();
            window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == lastDetail).BringIntoView();
            await E2EHarness.PumpUiAsync();
            Capture(window, "operator-results-failed-steps-900");
        }
        finally { File.Delete(path); File.Delete(sidecar); }
    }

    [AvaloniaFact]
    public async Task Operator_runs_the_checked_five_vendor_package_created_in_authoring()
    {
        var evidence = Environment.GetEnvironmentVariable("HARDWARETEST_UI_EVIDENCE_DIRECTORY");
        var handoff = evidence is null ? "" : Path.Combine(evidence, "handoff-960");
        if (!File.Exists(Path.Combine(handoff, "sample.TapPlan"))) Assert.Skip("Run the rendered authoring persona first with HARDWARETEST_UI_EVIDENCE_DIRECTORY to supply its checked handoff.");
        // Use the actual checked package bytes; compare them to the authoring saved plan.
        var package = Directory.GetFiles(handoff, "*.TapPackage").Single();
        using var archive = System.IO.Compression.ZipFile.OpenRead(package);
        var entry = archive.Entries.Single(item => item.FullName.EndsWith("sample.TapPlan", StringComparison.Ordinal));
        using var packed = new MemoryStream(); using (var input = entry.Open()) input.CopyTo(packed);
        var savedXml = System.Xml.Linq.XDocument.Load(Path.Combine(handoff, "sample.TapPlan"));
        packed.Position = 0;
        var packedXml = System.Xml.Linq.XDocument.Load(packed);
        var savedLeaves = savedXml.Descendants("TestStep").Where(step => step.Element("ChildTestSteps")?.Elements("TestStep").Any() != true).ToDictionary(step => (string)step.Attribute("Id")!);
        var packedLeaves = packedXml.Descendants("TestStep").Where(step => step.Element("ChildTestSteps")?.Elements("TestStep").Any() != true).ToDictionary(step => (string)step.Attribute("Id")!);
        Assert.Equal(savedLeaves.Keys.Order(), packedLeaves.Keys.Order());
        foreach (var (id, savedStep) in savedLeaves)
        {
            // Compiler-owned phase containers get fresh IDs; authored leaves and their
            // setup/measure/cleanup membership, bindings and presentation remain stable.
            Assert.True(System.Xml.Linq.XNode.DeepEquals(savedStep, packedLeaves[id]), "Handoff changed authored step " + savedStep.Element("Name")?.Value);
            Assert.Equal(savedStep.Ancestors("TestStep").Select(parent => parent.Element("Name")?.Value), packedLeaves[id].Ancestors("TestStep").Select(parent => parent.Element("Name")?.Value));
        }
        var programs = Path.Combine(AppContext.BaseDirectory, "Programs");
        var path = Path.Combine(programs, "alpha-authored-handoff.TapPlan");
        var sidecar = Path.ChangeExtension(path, ".program.json");
        File.WriteAllBytes(path, packed.ToArray());
        File.Copy(Path.Combine(handoff, "sample.program.json"), sidecar, overwrite: true);
        try
        {
            using var shown = E2EHarness.ShowMainWindow();
            var window = shown.Window; window.Width = 1280; window.Height = 800;
            var main = shown.Main; main.NavigateToPageId("RunTest");
            var run = E2EHarness.RunTestVm(main); await run.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() => window.GetVisualDescendants().OfType<ComboBox>().Any(box => AutomationProperties.GetName(box) == "Program" && box.IsEffectivelyVisible));
            window.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "Program" && box.IsEffectivelyVisible).SelectedItem = run.ProgramSelection.Programs.Single(item => item.Id == "alpha-authored-handoff");
            await E2EHarness.WaitUntilAsync(() => Descendants(run.StepTree.FullHierarchy).Any(node => node.Name.Contains("Dmm", StringComparison.Ordinal)));
            await E2EHarness.PumpUiAsync();
            Type(window, window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "DutSerialBox" && box.IsEffectivelyVisible), "AUTHORED-HANDOFF-001");
            Type(window, window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "TechnicianBox" && box.IsEffectivelyVisible), "Alpha QA");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Confirm Session") && button.IsEffectivelyVisible));
            await E2EHarness.WaitUntilAsync(() => run.Workspace.ShowSteps);
            Capture(window, "operator-authored-handoff-ready-1280");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Run" && button.IsEffectivelyVisible));
            await E2EHarness.WaitUntilAsync(() =>
            {
                if (run.Interaction.IsAwaitingOperator)
                {
                    // Simulated fixture responses for the original sample's prompt/input setup.
                    foreach (var field in run.Interaction.InteractionFields)
                        if (field.IsBoolean) field.BoolValue = true;
                        else if (string.IsNullOrWhiteSpace(field.Value)) field.Value = field.IsNumber ? "0" : "alpha-fixture";
                    run.ContinueOperatorAttention();
                }
                return !run.IsRunning && !string.IsNullOrWhiteSpace(run.LastRunId);
            }, TimeSpan.FromSeconds(90));
            Capture(window, "operator-authored-handoff-complete-1280");
            main.NavigateToPageId("Results"); await main.Results.RefreshCommand.ExecuteAsync();
            main.Results.SelectedRun = main.Results.Runs.Single(result => result.RunId == run.LastRunId);
            await main.Results.OpenSelectedRunAsync();
            Assert.True(main.Results.OpenedRun!.Samples.Count >= 5);
            foreach (var operation in new[] { "DmmMeasureVoltageDc", "DcPowerSupplyReadVoltage", "OscilloscopeMeasureVpp", "FunctionGeneratorReadFrequency", "CounterMeasureFrequency" })
                Assert.Contains(main.Results.OpenedRun.Samples, sample => sample.Channel.StartsWith(operation, StringComparison.Ordinal));
            Capture(window, "operator-authored-handoff-results-1280");
        }
        finally { File.Delete(path); File.Delete(sidecar); }
    }

    [AvaloniaFact]
    public async Task Operator_sees_device_protocol_error_and_recovery_in_run_and_recorded_results()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Programs", "alpha-error.TapPlan");
        var sidecar = Path.ChangeExtension(path, ".program.json");
        var plan = new global::OpenTap.TestPlan();
        plan.ChildTestSteps.Add(new HardwareTest.OpenTap.Plugins.Instruments.DeviceOperationStep
        {
            Name = "Current measurement (simulated error)",
            Operation = "DmmMeasureCurrentDc",
            Channel = "current",
            Instrument = new() { Name = "Keysight service DMM", DriverId = "keysight.34461a", VisaAddress = "MOCK::BAD-CURRENT-REPLY" }
        });
        plan.Save(path);
        File.WriteAllText(sidecar, """{"displayName":"Alpha protocol error (simulated)","requireSerial":true,"requireOperator":false}""");
        try
        {
            using var shown = E2EHarness.ShowMainWindow();
            var window = shown.Window; window.Width = 900; window.Height = 600;
            var main = shown.Main; main.NavigateToPageId("RunTest");
            var run = E2EHarness.RunTestVm(main); await run.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() => window.GetVisualDescendants().OfType<ComboBox>().Any(box => AutomationProperties.GetName(box) == "Program" && box.IsEffectivelyVisible));
            window.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "Program" && box.IsEffectivelyVisible).SelectedItem = run.ProgramSelection.Programs.Single(item => item.Id == "alpha-error");
            await E2EHarness.WaitUntilAsync(() => Descendants(run.StepTree.FullHierarchy).Any(step => step.Name == "Current measurement (simulated error)"));
            Type(window, window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "DutSerialBox" && box.IsEffectivelyVisible), "PROTOCOL-ERROR-001");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Confirm Session") && button.IsEffectivelyVisible));
            await E2EHarness.WaitUntilAsync(() => run.Workspace.ShowSteps);
            Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Run" && button.IsEffectivelyVisible));
            await E2EHarness.WaitUntilAsync(() => !run.IsRunning && !string.IsNullOrWhiteSpace(run.LastRunId));
            Assert.Contains("Device I/O failed", run.Status);
            Assert.Contains("Keysight service DMM", run.Status);
            Assert.Equal("Error", Descendants(run.StepTree.FullHierarchy).Single(node => node.Name == "Current measurement (simulated error)").ChipText);
            Assert.Equal(0, run.StepTree.SuiteFailedCount);
            Assert.Equal(1, run.StepTree.SuiteErrorCount);
            Capture(window, "operator-run-protocol-error-900");
            Activate(window, window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Needs attention") && button.IsEffectivelyVisible));
            await E2EHarness.PumpUiAsync();
            Assert.Equal("Error", Assert.Single(run.StepTree.StepListItems, item => !item.IsHeader && item.Step is not null).ChipText);
            Capture(window, "operator-run-protocol-error-attention-900");
            main.NavigateToPageId("Results"); await main.Results.RefreshCommand.ExecuteAsync();
            main.Results.SelectedRun = main.Results.Runs.Single(result => result.RunId == run.LastRunId);
            await main.Results.OpenSelectedRunAsync(); await E2EHarness.PumpUiAsync();
            var detail = window.GetVisualDescendants().OfType<TextBlock>().Single(block => AutomationProperties.GetName(block) == "Run outcome details");
            Assert.True(detail.IsEffectivelyVisible);
            Assert.Contains("before retrying", detail.Text);
            Assert.Contains("DmmMeasureCurrentDc", detail.Text);
            Assert.Contains(main.Results.StepDetails, row => row.Contains("] ERROR", StringComparison.Ordinal));
            Assert.DoesNotContain(main.Results.StepDetails, row => row.Contains("] FAIL", StringComparison.Ordinal));
            Capture(window, "operator-results-protocol-error-900");
        }
        finally { File.Delete(path); File.Delete(sidecar); }
    }

    private static IEnumerable<HierarchyStepViewModel> Descendants(IEnumerable<HierarchyStepViewModel> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var nested in Descendants(step.Children)) yield return nested;
        }
    }

    private static void Type(Window window, TextBox box, string value)
    {
        box.BringIntoView(); Dispatcher.UIThread.RunJobs(); Assert.True(box.Focus());
        box.SelectAll();
        if (value.Length == 0) { window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null); window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null); }
        else window.KeyTextInput(value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(value, box.Text);
    }
    private static void Activate(Window window, Button button)
    {
        button.BringIntoView(); Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEnabled); Assert.True(button.Focus());
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("HARDWARETEST_UI_EVIDENCE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
        File.WriteAllLines(Path.Combine(directory, name + ".controls.tsv"), window.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible).Select(control => $"{control.GetType().Name}\t{AutomationProperties.GetName(control)}\t{control.Bounds}\t{control.IsEnabled}"));
    }
}
