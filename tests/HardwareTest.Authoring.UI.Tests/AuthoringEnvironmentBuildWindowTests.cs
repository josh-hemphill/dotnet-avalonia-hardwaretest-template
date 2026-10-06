using System.IO.Compression;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringEnvironmentBuildWindowTests
{
    [AvaloniaTheory]
    [InlineData("formula")]
    [InlineData("corrupt")]
    [InlineData("future")]
    public async Task Real_GUI_import_and_pack_use_rendered_home_exclude_invalid_source_and_retain_checked_receipt_after_edit_failure_cancel_and_prepare(string sourceKind)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
        workspace.Manifest.Package.Name = "GUI Checked Program";
        workspace.Manifest.ExcludedProgramIds.Add("blocked");
        workspace.Manifest.Dependencies.Add(new() { Package = "Offline Fixture", Version = "^1.2.0" });
        // These synthetic archives prove generic two-package import and checked receipt behavior.
        // A supported hardware-library package must carry its actual validated DLL payload.
        const string secondPackage = "Second Offline Fixture";
        workspace.Manifest.Dependencies.Add(new() { Package = secondPackage, Version = "^1.0.0" });
        AuthoringWorkspaceLoader.SaveManifest(fixture.WorkspaceRoot, workspace.Manifest);
        var store = new AuthoringDocumentStore(fixture.WorkspaceRoot);
        var sample = new PlanCompiler().Load(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan"));
        store.Save(AuthoringDocumentDto.FromDraft(sample, 31, compiledPlanHash: AuthoringDocumentStore.ComputeHash(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan")),
            compiledSidecarHash: AuthoringDocumentStore.ComputeHash(Path.Combine(fixture.WorkspaceRoot, "sample.program.json"))));
        var blocked = AuthoringRecipeCatalog.CreateProgram("blocked") with
        {
            Measure = [new MetricNode(new MetricDraft("Unsupported deployment", "result", "scalar", "V", new LimitSpec(null, null, 0), null, new ExpressionAlgorithm([], "std(input)")))]
        };
        store.Save(AuthoringDocumentDto.FromDraft(blocked, 7));
        var blockedPath = store.GetDocumentPath("blocked");
        if (sourceKind != "formula") File.WriteAllText(blockedPath, sourceKind == "corrupt" ? "{" : "{\"schemaVersion\":999,\"planId\":\"blocked\"}");
        var excludedBytes = File.ReadAllBytes(blockedPath);
        var selectedHome = Path.Combine(fixture.WorkspaceRoot, "selected-home"); Directory.CreateDirectory(selectedHome);
        File.WriteAllText(Path.Combine(selectedHome, "selected-home.marker"), "distinct rendered home");
        fixture.ViewModel.OpenTapHomeOverride = selectedHome;
        var offlinePicker = new TestWorkspacePicker(); var outputPicker = new TestWorkspacePicker();
        var window = fixture.Show(packOutputPicker: outputPicker, offlinePackagePicker: offlinePicker); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")), action => Dispatcher.UIThread.Post(action));
        Assert.False(fixture.ViewModel.Workspace!.IsReadOnly);
        fixture.ViewModel.SetBuildProgramIncluded("blocked", true);
        Assert.False(fixture.ViewModel.CanPack);
        Assert.Contains("Repair incomplete", fixture.ViewModel.PackGuardText);
        fixture.ViewModel.SetBuildProgramIncluded("blocked", false);
        Assert.True(fixture.ViewModel.SaveAll().Succeeded);
        fixture.ViewModel.Open(fixture.WorkspaceRoot);
        Assert.Equal(excludedBytes, File.ReadAllBytes(blockedPath));
        SelectTab(window, 3);
        Assert.Equal(selectedHome, fixture.Control<TextBlock>("Selected authoring home").Text);
        Assert.Contains(fixture.ViewModel.EnvironmentPackages, p => p.Package == "Offline Fixture" && !p.Satisfied);
        Assert.Equal("Not checked", fixture.ViewModel.CompatibilityState);
        var archivePath = Path.Combine(fixture.WorkspaceRoot, "offline.TapPackage");
        WriteArchive(archivePath, "Offline Fixture", "1.3.0");
        offlinePicker.Path = archivePath;
        AuthoringUiFixture.Click(fixture.Control<Button>("Import offline authoring package"));
        await Until(() => !fixture.ViewModel.OperationBusy);
        Assert.Null(fixture.ViewModel.Error);
        Assert.Contains(fixture.ViewModel.EnvironmentPackages, p => p.Package == "Offline Fixture" && p.Satisfied && p.InstalledVersion == "1.3.0");
        Assert.Contains(fixture.ViewModel.EnvironmentPackages, p => p.Package == secondPackage && !p.Satisfied);
        Assert.False(fixture.ViewModel.CanPack); Assert.Equal("Not checked", fixture.ViewModel.CompatibilityState); Assert.Null(fixture.ViewModel.LastBuildReceipt);
        var secondArchive = Path.Combine(fixture.WorkspaceRoot, "second-offline.TapPackage");
        WriteArchive(secondArchive, secondPackage, "1.2.0"); offlinePicker.Path = secondArchive;
        AuthoringUiFixture.Click(fixture.Control<Button>("Import offline authoring package")); await Until(() => !fixture.ViewModel.OperationBusy);
        Assert.Null(fixture.ViewModel.Error);
        Assert.Contains(fixture.ViewModel.EnvironmentPackages, p => p.Package == secondPackage && p.Satisfied);
        Assert.True(File.Exists(Path.Combine(selectedHome, "selected-home.marker")));
        Assert.False(Directory.Exists(Path.Combine(fixture.WorkspaceRoot, OpenTapHomeBootstrapper.DefaultHomeRelativePath)));
        var output = Path.Combine(fixture.WorkspaceRoot, "custom-output"); outputPicker.Path = output;
        SelectTab(window, 4);
        Assert.True(fixture.ViewModel.CanPack);
        var stages = new List<string?>(); fixture.ViewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AuthoringWorkspaceViewModel.OperationStage)) stages.Add(fixture.ViewModel.OperationStage); };
        File.Delete(Path.Combine(fixture.WorkspaceRoot, "fixture-child.json"));
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-wait"), "");
        AuthoringUiFixture.Click(fixture.Control<Button>("Pack workspace"));
        await Until(() => File.Exists(Path.Combine(fixture.WorkspaceRoot, "fixture-child.json")));
        Assert.Null(fixture.ViewModel.LastBuildReceipt);
        Assert.Equal("Not checked", fixture.ViewModel.CompatibilityState);
        SelectTab(window, 1); fixture.ViewModel.SelectProgram("sample");
        var displayName = fixture.Control<TextBox>("Display name"); Assert.True(displayName.IsEffectivelyEnabled);
        displayName.Text = "Edited during checked build"; AuthoringUiFixture.Drain();
        Assert.Contains("sample", fixture.ViewModel.DirtyProgramIds);
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-release"), "");
        await Until(() => !fixture.ViewModel.OperationBusy);
        File.Delete(Path.Combine(fixture.WorkspaceRoot, "fixture-wait"));
        File.Delete(Path.Combine(fixture.WorkspaceRoot, "fixture-release"));
        Assert.Null(fixture.ViewModel.Error);
        var receipt = Assert.IsType<AuthoringBuildReceipt>(fixture.ViewModel.LastBuildReceipt);
        Assert.Contains(receipt.RequiredChecks, f => f.Code == "BUILD_COMPATIBILITY_PASS");
        Assert.Contains(receipt.RequiredChecks, f => f.Code == "BUILD_COMPAT_PROVIDER" && f.Message.Contains("Production in-process TuiCompatChecker"));
        Assert.Contains(receipt.Sources, s => s.PlanId == "sample" && s.SavedRevision == 31);
        Assert.Contains(receipt.Inputs, i => i.Path == Path.Combine(selectedHome, "selected-home.marker"));
        Assert.Equal(["sample"], receipt.IncludedPlans); Assert.Equal(["blocked"], receipt.ExcludedPlans);
        Assert.Contains("Publishing checked artifacts", stages);
        Assert.False(fixture.ViewModel.CanPack); Assert.Contains("needs a new build", fixture.ViewModel.BuildReadinessText);
        Assert.Equal("Edited during checked build", fixture.ViewModel.DisplayName);
        SelectTab(window, 4);
        Assert.Equal(output, fixture.Control<TextBlock>("Completed build output").Text);
        Assert.Contains("Passed for recorded revision", fixture.Control<TextBlock>("Recorded build compatibility").Text);
        var durableReceipt = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(output, AuthoringBuildService.ReceiptFileName)), AuthoringBuildJsonContext.Default.AuthoringBuildReceipt)!;
        Assert.Equal(receipt.BuildId, durableReceipt.BuildId);
        using (var archive = ZipFile.OpenRead(Directory.GetFiles(output, "*.TapPackage").Single()))
        {
            Assert.Contains(archive.Entries, e => e.FullName.EndsWith("sample.TapPlan", StringComparison.Ordinal));
            Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains("blocked", StringComparison.OrdinalIgnoreCase));
        }
        var previousFiles = Directory.GetFiles(output).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        fixture.ViewModel.OpenTapHomeOverride = "invalid\0home";
        SelectTab(window, 3);
        Assert.Contains("Invalid OpenTAP home setting", fixture.Control<TextBlock>("Selected authoring home").Text);
        Assert.All(fixture.ViewModel.EnvironmentPackages, package => Assert.False(package.Satisfied));
        Assert.Same(receipt, fixture.ViewModel.LastBuildReceipt); Assert.False(fixture.ViewModel.CanPack);
        Assert.True(fixture.ViewModel.SaveAll().Succeeded); Assert.NotNull(fixture.ViewModel.SavePreviewWarning);
        fixture.ViewModel.OpenTapHomeOverride = selectedHome;
        Assert.Null(fixture.ViewModel.SavePreviewWarning); Assert.Null(fixture.ViewModel.Error);
        var fileHome = Path.Combine(fixture.WorkspaceRoot, "unavailable-file-home"); File.WriteAllText(fileHome, "preserved file home");
        fixture.ViewModel.OpenTapHomeOverride = fileHome;
        Assert.Contains(fileHome, fixture.Control<TextBlock>("Selected authoring home").Text);
        Assert.False(fixture.ViewModel.CanPack); Assert.All(fixture.ViewModel.EnvironmentPackages, package => Assert.False(package.Satisfied));
        SelectTab(window, 4); Assert.False(fixture.Control<Button>("Pack workspace").IsEffectivelyEnabled); SelectTab(window, 3);
        Assert.Same(receipt, fixture.ViewModel.LastBuildReceipt); Assert.Equal("preserved file home", File.ReadAllText(fileHome));
        fixture.ViewModel.OpenTapHomeOverride = selectedHome;
        var engineRequirement = fixture.ViewModel.Workspace!.Manifest.Dependencies.Single(dependency => dependency.Package == "OpenTAP");
        var requiredEngine = engineRequirement.Version; engineRequirement.Version = "99.0.0";
        var homeBeforeRejection = Directory.GetFiles(selectedHome, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        Assert.Throws<AuthoringWorkspaceException>(() => fixture.ViewModel.Bootstrap(new() { Offline = true }));
        Assert.Same(receipt, fixture.ViewModel.LastBuildReceipt);
        Assert.Equal(homeBeforeRejection.Keys.Order(), Directory.GetFiles(selectedHome, "*", SearchOption.AllDirectories).Order());
        foreach (var file in homeBeforeRejection) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        engineRequirement.Version = requiredEngine;
        AuthoringUiFixture.Click(fixture.Control<Button>("Prepare selected authoring environment"));
        await Until(() => !fixture.ViewModel.OperationBusy);
        Assert.Same(receipt, fixture.ViewModel.LastBuildReceipt); Assert.Single(fixture.ViewModel.BuildHistory);
        SelectTab(window, 1); fixture.ViewModel.SelectProgram("sample"); fixture.ViewModel.DisplayName = "Edited after receipt";
        Assert.False(fixture.ViewModel.CanPack); Assert.Contains("needs a new build", fixture.ViewModel.BuildReadinessText);
        Assert.Same(receipt, fixture.ViewModel.LastBuildReceipt); Assert.True(fixture.ViewModel.SaveAll().Succeeded);
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-result-wait"), "");
        SelectTab(window, 4); AuthoringUiFixture.Click(fixture.Control<Button>("Pack workspace"));
        await Until(() => File.Exists(Path.Combine(fixture.WorkspaceRoot, "fixture-prepared")));
        File.AppendAllText(Path.Combine(selectedHome, "selected-home.marker"), "external edit after checking");
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-release"), "");
        await Until(() => !fixture.ViewModel.OperationBusy);
        Assert.NotNull(fixture.ViewModel.Error); Assert.Same(receipt, fixture.ViewModel.LastBuildReceipt); Assert.Single(fixture.ViewModel.BuildHistory);
        File.WriteAllText(Path.Combine(selectedHome, "selected-home.marker"), "distinct rendered home");
        File.Delete(Path.Combine(fixture.WorkspaceRoot, "fixture-result-wait")); File.Delete(Path.Combine(fixture.WorkspaceRoot, "fixture-prepared")); File.Delete(Path.Combine(fixture.WorkspaceRoot, "fixture-release"));
        Assert.True(fixture.ViewModel.CanPack, $"Error: {fixture.ViewModel.Error}; status: {fixture.ViewModel.Status}; busy: {fixture.ViewModel.OperationBusy}; guard: {fixture.ViewModel.PackGuardText}");
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-result-wait"), "");
        AuthoringUiFixture.Click(fixture.Control<Button>("Pack workspace"));
        Assert.True(fixture.ViewModel.OperationBusy);
        await Until(() => File.Exists(Path.Combine(fixture.WorkspaceRoot, "fixture-prepared")));
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel authoring operation"));
        await Until(() => !fixture.ViewModel.OperationBusy);
        Assert.Contains("cancelled", fixture.ViewModel.Status); Assert.Same(receipt, fixture.ViewModel.LastBuildReceipt); Assert.Single(fixture.ViewModel.BuildHistory);
        foreach (var file in previousFiles) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(output, file.Key!)));
        File.Delete(Path.Combine(fixture.WorkspaceRoot, "fixture-result-wait"));
        fixture.ViewModel.Open(fixture.WorkspaceRoot);
        Assert.Null(fixture.ViewModel.LastBuildReceipt); Assert.Empty(fixture.ViewModel.BuildHistory);
        Assert.Equal(excludedBytes, File.ReadAllBytes(blockedPath));
    }

    [AvaloniaFact]
    public async Task Enabled_next_pack_starts_before_previous_window_handler_continuation_completes()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
        workspace.Manifest.Package.Name = "Reentrant Checked Program";
        AuthoringWorkspaceLoader.SaveManifest(fixture.WorkspaceRoot, workspace.Manifest);
        var output = Path.Combine(fixture.WorkspaceRoot, "reentrant-output");
        var picker = new CountingOutputPicker(output);
        var window = fixture.Show(packOutputPicker: picker); fixture.OpenRememberedWorkspace();
        Assert.True(fixture.ViewModel.SaveAll().Succeeded);
        fixture.ViewModel.Bootstrap(new() { Offline = true });
        fixture.ViewModel.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")), action => Dispatcher.UIThread.Post(action));
        SelectTab(window, 4);
        var requestedNext = false;
        var transition = new TaskCompletionSource<(bool Enabled, int PickerCalls, bool Started)>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AuthoringWorkspaceViewModel.CanPack) || requestedNext || fixture.ViewModel.OperationBusy || !fixture.ViewModel.HasCompletedBuild || !fixture.ViewModel.CanPack) return;
            requestedNext = true;
            try
            {
                AuthoringUiFixture.Drain();
                var button = fixture.Control<Button>("Pack workspace");
                var enabled = button.IsEffectivelyVisible && button.IsEffectivelyEnabled;
                if (enabled) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                transition.SetResult((enabled, picker.Calls, fixture.ViewModel.OperationBusy));
            }
            catch (Exception error) { transition.SetException(error); }
        };
        AuthoringUiFixture.Click(fixture.Control<Button>("Pack workspace"));
        await Until(() => transition.Task.IsCompleted || (!requestedNext && !fixture.ViewModel.OperationBusy));
        Assert.True(transition.Task.IsCompleted, $"Error: {fixture.ViewModel.Error}; status: {fixture.ViewModel.Status}; guard: {fixture.ViewModel.PackGuardText}");
        var observed = await transition.Task;
        Assert.True(observed.Enabled); Assert.Equal(2, observed.PickerCalls); Assert.True(observed.Started);
        await Until(() => !fixture.ViewModel.OperationBusy);
        Assert.Null(fixture.ViewModel.Error); Assert.Equal(2, fixture.ViewModel.BuildHistory.Count);
        Assert.Equal(2, fixture.ViewModel.BuildHistory.Select(build => build.Result.Receipt.BuildId).Distinct().Count());
        foreach (var build in fixture.ViewModel.BuildHistory)
        {
            Assert.Contains(build.Result.Receipt.RequiredChecks, check => check.Code == "BUILD_COMPATIBILITY_PASS");
            Assert.Contains(build.Result.Receipt.RequiredChecks, check => check.Code == "BUILD_COMPAT_PROVIDER" && check.Message.Contains("Production in-process TuiCompatChecker"));
        }
        var durable = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(output, AuthoringBuildService.ReceiptFileName)), AuthoringBuildJsonContext.Default.AuthoringBuildReceipt)!;
        Assert.Equal(fixture.ViewModel.LastBuildReceipt!.BuildId, durable.BuildId);
    }

    private sealed class CountingOutputPicker(string path) : IAuthoringWorkspacePicker
    {
        public int Calls { get; private set; }
        public Task<string?> PickAsync() { Calls++; return Task.FromResult<string?>(path); }
    }

    [AvaloniaTheory]
    [InlineData(false, "session")]
    [InlineData(true, "session")]
    [InlineData(false, "home")]
    [InlineData(true, "home")]
    [InlineData(false, "owner")]
    [InlineData(true, "owner")]
    public async Task Awaited_import_and_output_pickers_reject_replaced_workspace_session_and_changed_rendered_home(bool import, string change)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var picker = new TestWorkspacePicker { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var window = fixture.Show(packOutputPicker: import ? null : picker, offlinePackagePicker: import ? picker : null); fixture.OpenRememberedWorkspace();
        SelectTab(window, import ? 3 : 4);
        var button = fixture.Control<Button>(import ? "Import offline authoring package" : "Pack workspace");
        AuthoringUiFixture.Click(button);
        if (change == "session") fixture.ViewModel.Open(fixture.WorkspaceRoot);
        else if (change == "home") fixture.ViewModel.OpenTapHomeOverride = Path.Combine(fixture.WorkspaceRoot, "changed-home");
        else window.Close();
        picker.Pending.SetResult(Path.Combine(fixture.WorkspaceRoot, import ? "never-read.TapPackage" : "never-created-output"));
        await Task.Delay(50); AuthoringUiFixture.Drain();
        Assert.False(fixture.ViewModel.OperationBusy); Assert.Null(fixture.ViewModel.Error);
        Assert.False(Directory.Exists(Path.Combine(fixture.WorkspaceRoot, "changed-home")));
        Assert.False(Directory.Exists(Path.Combine(fixture.WorkspaceRoot, "never-created-output")));
    }

    internal static void WriteArchive(string path, string name, string version, string payloadEntry = "payload.txt")
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("package.xml").Open()))
            writer.Write($"<Package Name=\"{name}\" Version=\"{version}\"><Files><File Path=\"payload.txt\"/></Files></Package>");
        using var payload = new StreamWriter(archive.CreateEntry(payloadEntry).Open()); payload.Write("offline payload");
    }
    private static void SelectTab(Window window, int index) { window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = index; AuthoringUiFixture.Drain(); }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (!condition()) { AuthoringUiFixture.Drain(); await Task.Delay(10, timeout.Token); }
        AuthoringUiFixture.Drain();
    }
}
