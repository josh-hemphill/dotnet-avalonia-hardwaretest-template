using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.OpenTap.Plugins.Instruments;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AlphaDevicePersonaUiTests
{
    [AvaloniaTheory]
    [InlineData(960, 600)]
    [InlineData(1280, 800)]
    public async Task New_author_adds_mixed_vendor_hardware_and_integrator_edits_shared_device(double width, double height)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
        workspace.Manifest.Package.Name = "Mixed Alpha Persona";
        AuthoringWorkspaceLoader.SaveManifest(fixture.WorkspaceRoot, workspace.Manifest);
        var output = Path.Combine(fixture.WorkspaceRoot, "persona-ship");
        var window = fixture.Show(width, height, packOutputPicker: new TestWorkspacePicker { Path = output });
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        fixture.NavigateTask(3);
        Capture(window, $"authoring-environment-missing-{width}");
        fixture.ViewModel.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")), action => Dispatcher.UIThread.Post(action));
        fixture.Control<Expander>("Environment dependency setup").IsExpanded = true;
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Declare Instrument Components dependency"));
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        await fixture.WaitForReadinessAsync();
        AuthoringUiFixture.Click(fixture.Control<Button>("Show package sources and manual import"));
        var repository = fixture.Control<TextBox>("Environment package repository URL");
        var originalRepository = repository.Text!;
        fixture.Type(repository, "http://invalid-package-source");
        Assert.False(string.IsNullOrWhiteSpace(fixture.ViewModel.PackageRepositoryError));
        fixture.Control<TextBlock>("Environment repository validation").BringIntoView(); AuthoringUiFixture.Drain();
        Capture(window, $"authoring-environment-error-{width}");
        fixture.Type(repository, originalRepository);
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-wait"), "");
        AuthoringUiFixture.Click(fixture.Control<Button>("Prepare selected authoring environment"));
        await Until(() => File.Exists(Path.Combine(fixture.WorkspaceRoot, "fixture-child.json")));
        fixture.Control<TextBlock>("Environment preparation progress").BringIntoView(); AuthoringUiFixture.Drain();
        Capture(window, $"authoring-environment-cancellable-{width}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel environment preparation"));
        await Until(() => !fixture.ViewModel.OperationBusy);
        File.Delete(Path.Combine(fixture.WorkspaceRoot, "fixture-wait"));
        Capture(window, $"authoring-environment-cancelled-{width}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Prepare selected authoring environment"));
        Capture(window, $"authoring-environment-preparing-{width}");
        await Until(() => !fixture.ViewModel.OperationBusy);
        await fixture.WaitForReadinessAsync();
        Assert.True(fixture.ViewModel.Error is null, fixture.ViewModel.Error);
        Assert.Contains(fixture.ViewModel.InstrumentTypeChoices, adapter => adapter.TypeId == DeviceDriverCatalog.RegisteredTypeId);
        fixture.Control<TextBlock>("Instrument Components readiness").BringIntoView(); AuthoringUiFixture.Drain();
        Capture(window, $"authoring-environment-ready-{width}");
        fixture.NavigateTask(1);
        await fixture.WaitForReadinessAsync();
        Capture(window, $"authoring-hardware-before-{width}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Open add instrument"));
        var type = fixture.Control<ComboBox>("New instrument type");
        Select(window, type, fixture.ViewModel.InstrumentTypeChoices.ToList().FindIndex(adapter => adapter.TypeId == DeviceDriverCatalog.RegisteredTypeId));
        AuthoringUiFixture.Drain();
        foreach (var id in new[] { "keysight.34461a", "keithley.2602b", "rigol.ds1054z", "siglent.sdg2042x", "tektronix.fca3100" })
        {
            var slot = id.Split('.')[1].ToUpperInvariant();
            fixture.Type(fixture.Control<TextBox>("Add instrument slot"), slot);
            var model = fixture.Control<ComboBox>("New device model");
            fixture.Type(fixture.Control<TextBox>("Search device models"), id.Split('.')[1]);
            var search = fixture.Control<TextBox>("Search device models");
            Assert.True(search.Focus());
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            AuthoringUiFixture.Drain(); Assert.True(model.IsFocused);
            Select(window, model, 0);
            AuthoringUiFixture.Drain();
            Assert.Equal(id, fixture.ViewModel.NewDeviceDriver!.Id);
            fixture.Type(fixture.Control<TextBox>("New slot VISA"), "MOCK::" + slot);
            var add = fixture.Control<Button>("Add instrument slot");
            add.BringIntoView();
            AuthoringUiFixture.Drain();
            Assert.True(add.Focus());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            AuthoringUiFixture.Drain();
            Assert.Contains(fixture.ViewModel.Instruments, instrument => instrument.SlotName == slot && instrument.Settings.GetValueOrDefault("DriverId") == id);
        }
        fixture.Type(fixture.Control<TextBox>("New slot VISA"), "MOCK::DUPLICATE");
        fixture.Type(fixture.Control<TextBox>("Add instrument slot"), "FCA3100");
        Assert.False(fixture.Control<Button>("Add instrument slot").IsEnabled);
        Assert.Contains("already exists", fixture.ViewModel.NewInstrumentAvailabilityText);
        Capture(window, $"authoring-hardware-validation-{width}");
        fixture.Type(fixture.Control<TextBox>("Add instrument slot"), "SPARE");
        await fixture.WaitForReadinessAsync();
        fixture.Control<Button>("Open add instrument").BringIntoView();
        AuthoringUiFixture.Drain();
        var instrumentList = fixture.Control<ListBox>("Instruments");
        instrumentList.ScrollIntoView(fixture.ViewModel.Instruments.First());
        AuthoringUiFixture.Drain();
        var instrumentRows = instrumentList.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Assert.Equal(fixture.ViewModel.Instruments.Count, instrumentRows.Length);
        for (var index = 1; index < instrumentRows.Length; index++)
        {
            var previous = instrumentRows[index - 1];
            var current = instrumentRows[index];
            Assert.True(previous.Bounds.Bottom <= current.Bounds.Top + 0.5,
                $"Instrument rows overlap: {previous.Bounds} and {current.Bounds}.");
        }
        Capture(window, $"authoring-hardware-mixed-{width}");
        fixture.ViewModel.SelectedInstrumentSlot = "2602B";
        AuthoringUiFixture.Click(fixture.Control<Button>("Load selected binding"));
        Assert.Equal("keithley.2602b", fixture.ViewModel.EditDeviceDriver!.Id);
        fixture.Type(fixture.Control<TextBox>("Confirmed device channels"), "1,2");
        fixture.Type(fixture.Control<TextBox>("Confirmed device outputs"), "psu:1,psu:2");
        Capture(window, $"authoring-hardware-edit-{width}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Review binding change"));
        var review = Assert.Single(window.OwnedWindows);
        Capture(review, $"authoring-binding-review-{width}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Apply reviewed binding", review));
        Assert.Equal("psu:1,psu:2", fixture.ViewModel.SelectedInstrument!.Settings["Outputs"]);
        fixture.ViewModel.Undo();
        Assert.False(fixture.ViewModel.SelectedInstrument!.Settings.ContainsKey("Outputs"));
        fixture.ViewModel.Redo();
        Assert.Equal("1,2", fixture.ViewModel.SelectedInstrument!.Settings["Channels"]);
        Capture(window, $"authoring-hardware-applied-{width}");
        fixture.ViewModel.SelectedInstrumentSlot = "FCA3100";
        var removeSection = fixture.Control<Expander>("Remove instrument section");
        removeSection.IsExpanded = true; AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove instrument slot from selected program"));
        var removal = Assert.Single(window.OwnedWindows);
        Select(removal, fixture.Control<ComboBox>("Compatible replacement instrument slot", removal), 0);
        Capture(removal, $"authoring-hardware-removal-review-{width}");
        AuthoringUiFixture.Click(removal.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Remove and replace slot")));
        Assert.DoesNotContain(fixture.ViewModel.Instruments, instrument => instrument.SlotName == "FCA3100");
        fixture.ViewModel.Undo();
        Assert.Contains(fixture.ViewModel.Instruments, instrument => instrument.SlotName == "FCA3100" && instrument.Settings.GetValueOrDefault("DriverId") == "tektronix.fca3100");

        fixture.NavigateTask(0);
        foreach (var (slot, operation) in new[] { ("34461A", "DmmMeasureVoltageDc"), ("2602B", "DcPowerSupplyReadVoltage"), ("DS1054Z", "OscilloscopeMeasureVpp"), ("SDG2042X", "FunctionGeneratorReadFrequency"), ("FCA3100", "CounterMeasureFrequency") })
        {
            AuthoringUiFixture.Click(fixture.Control<Button>("Add step"));
            var palette = Assert.Single(window.OwnedWindows);
            fixture.Type(fixture.Control<TextBox>("Search sequence palette", palette), operation);
            var binding = fixture.Control<ComboBox>("Step instrument binding", palette);
            Select(palette, binding, fixture.ViewModel.Instruments.ToList().FindIndex(instrument => instrument.SlotName == slot));
            var recipes = fixture.Control<ListBox>("Recipe", palette);
            recipes.SelectedItem = fixture.ViewModel.Recipes.Single(recipe => recipe.Id == "device:" + operation);
            await fixture.ViewModel.WaitForRecipeAvailabilityAsync();
            Assert.True(fixture.ViewModel.CanInsertRecipe, fixture.ViewModel.SelectedRecipePrerequisites);
            Capture(palette, $"authoring-sequence-binding-{slot}-{width}");
            AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe", palette));
            AuthoringUiFixture.Drain();
            Assert.Equal(slot, fixture.ViewModel.MetricInstrumentSlot);
            Assert.NotNull(fixture.ViewModel.SelectedMetric!.YUnit);
        }
        var arguments = fixture.Control<TextBox>("Arguments");
        fixture.Type(arguments, "unknown=1");
        fixture.Control<Button>("Save all").Focus(); AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(fixture.Control<TextBlock>("Selected step errors"), window);
        Capture(window, $"authoring-sequence-invalid-{width}");
        Assert.Contains("unknown", fixture.ViewModel.SelectedStepErrors, StringComparison.OrdinalIgnoreCase);
        fixture.Type(arguments, "");
        fixture.Control<Button>("Save all").Focus(); AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        await fixture.WaitForReadinessAsync();
        Assert.True(fixture.ViewModel.Error is null, fixture.ViewModel.Error);
        AuthoringUiFixture.Click(fixture.Control<Button>("Validate workspace"));
        await Until(() => !fixture.ViewModel.OperationBusy);
        Assert.True(fixture.ViewModel.Error is null, fixture.ViewModel.Error);
        Capture(window, $"authoring-sequence-validated-{width}");
        fixture.NavigateTask(4); await fixture.WaitForReadinessAsync();
        Assert.True(fixture.ViewModel.CanPack, fixture.ViewModel.BuildReadinessText);
        AuthoringUiFixture.Click(fixture.Control<Button>("Pack workspace"));
        await Until(() => !fixture.ViewModel.OperationBusy); await fixture.WaitForReadinessAsync();
        Assert.True(fixture.ViewModel.Error is null, fixture.ViewModel.Error);
        Assert.NotNull(fixture.ViewModel.LastBuildReceipt);
        Assert.NotEmpty(Directory.GetFiles(output, "*.TapPackage"));
        Capture(window, $"authoring-build-handoff-{width}");
        if (Environment.GetEnvironmentVariable("HARDWARETEST_UI_EVIDENCE_DIRECTORY") is { Length: > 0 } evidenceDirectory)
        {
            var handoff = Path.Combine(evidenceDirectory, $"handoff-{width}"); Directory.CreateDirectory(handoff);
            foreach (var file in Directory.GetFiles(output)) File.Copy(file, Path.Combine(handoff, Path.GetFileName(file)), overwrite: true);
            File.Copy(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan"), Path.Combine(handoff, "sample.TapPlan"), overwrite: true);
            File.Copy(Path.Combine(fixture.WorkspaceRoot, "sample.program.json"), Path.Combine(handoff, "sample.program.json"), overwrite: true);
        }

        fixture.ViewModel.InitializePlan(new("alpha-empty-state") { Instruments = [], IncludeSafeShutdown = false });
        fixture.NavigateTask(1); await fixture.WaitForReadinessAsync();
        Assert.Empty(fixture.ViewModel.Instruments);
        Capture(window, $"authoring-hardware-empty-{width}");


    }

    private static void Select(Window window, ComboBox box, int index)
    {
        Assert.True(index >= 0); box.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(box.Focus());
        // Closed ComboBox arrow navigation commits the choice without moving focus to a popup root.
        for (var i = 0; box.SelectedIndex != index && i <= box.ItemCount; i++)
        {
            var key = box.SelectedIndex < index ? Key.Down : Key.Up;
            var physical = key == Key.Down ? PhysicalKey.ArrowDown : PhysicalKey.ArrowUp;
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
            AuthoringUiFixture.Drain();
        }
        AuthoringUiFixture.Drain(); Assert.Equal(index, box.SelectedIndex);
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!predicate()) { AuthoringUiFixture.Drain(); await Task.Delay(5, timeout.Token); }
        AuthoringUiFixture.Drain();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("HARDWARETEST_UI_EVIDENCE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        var controls = window.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible)
            .Select(control => $"{control.GetType().Name}\t{AutomationProperties.GetName(control)}\t{control.Bounds}\t{control.IsEnabled}");
        File.WriteAllLines(Path.Combine(directory, name + ".controls.tsv"), controls);
    }
}
