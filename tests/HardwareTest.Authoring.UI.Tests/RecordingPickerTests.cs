using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class RecordingPickerTests
{
    [AvaloniaTheory]
    [InlineData("workspace")]
    [InlineData("program")]
    [InlineData("context")]
    [InlineData("hidden")]
    [InlineData("cancel")]
    [InlineData("stale-error")]
    public async Task Late_recording_picker_result_cannot_publish_into_a_changed_or_hidden_session(string transition)
    {
        using var fixture = new AuthoringUiFixture();
        using var replacement = new AuthoringUiFixture();
        var vm = fixture.ViewModel;
        vm.Open(fixture.WorkspaceRoot);
        vm.StopRecovery();
        var originalId = vm.SelectedProgram!.PlanId;
        new PlanCompiler().Save(vm.SelectedProgram! with { PlanId = "other" }, Path.Combine(fixture.WorkspaceRoot, "other.TapPlan"));
        vm.Open(fixture.WorkspaceRoot);
        vm.StopRecovery();
        vm.SelectProgram("other");
        var otherSource = Recording(fixture.WorkspaceRoot, "other");
        vm.ImportRecording(otherSource, "other-selected");
        vm.SelectProgram(originalId);
        var source = Recording(fixture.WorkspaceRoot, originalId);
        vm.ImportRecording(source, "captured");
        var selected = vm.SelectedDataset!;
        var before = File.ReadAllBytes(selected.Path);
        var recordings = Path.Combine(fixture.WorkspaceRoot, "recordings");
        var entries = Directory.GetFileSystemEntries(recordings, "*", SearchOption.AllDirectories);
        var picker = new ControlledRecordingPicker();
        var view = new WorkspacePreviewView(picker) { DataContext = vm };
        var owner = new Window { Content = view, Width = 960, Height = 800 };
        owner.Show();
        AuthoringUiFixture.Drain();
        try
        {
            var pending = view.ImportRecordingAsync();
            Assert.False(pending.IsCompleted);
            Assert.Same(owner, picker.Owner);
            switch (transition)
            {
                case "workspace":
                case "stale-error": vm.Open(replacement.WorkspaceRoot); vm.StopRecovery(); break;
                case "program": vm.SelectProgram("other"); break;
                case "context":
                    replacement.ViewModel.Open(replacement.WorkspaceRoot);
                    replacement.ViewModel.StopRecovery();
                    view.DataContext = replacement.ViewModel;
                    break;
                case "hidden": owner.Hide(); break;
            }
            var currentSelection = vm.SelectedDataset;
            if (transition == "stale-error") picker.Pending.SetException(new IOException("Picker source became unavailable."));
            else picker.Pending.SetResult(transition == "cancel" ? null : source);
            Assert.False(await pending);
            Assert.Same(currentSelection, vm.SelectedDataset);
            Assert.Equal(before, File.ReadAllBytes(selected.Path));
            Assert.Equal(entries, Directory.GetFileSystemEntries(recordings, "*", SearchOption.AllDirectories));
            Assert.False(Directory.Exists(Path.Combine(replacement.WorkspaceRoot, "recordings")));
            Assert.Null(vm.Error);
            Assert.Null(replacement.ViewModel.Error);
        }
        finally { owner.Close(); vm.StopRecovery(); replacement.ViewModel.StopRecovery(); }
    }

    [AvaloniaFact]
    public async Task Current_recording_picker_imports_and_invalid_json_reports_a_usable_error()
    {
        using var fixture = new AuthoringUiFixture();
        var vm = fixture.ViewModel;
        vm.Open(fixture.WorkspaceRoot);
        vm.StopRecovery();
        var source = Recording(fixture.WorkspaceRoot, vm.SelectedProgram!.PlanId);
        var picker = new ControlledRecordingPicker();
        var view = new WorkspacePreviewView(picker) { DataContext = vm };
        var owner = new Window { Content = view, Width = 960, Height = 800 };
        owner.Show();
        try
        {
            var pending = view.ImportRecordingAsync();
            picker.Pending.SetResult(source);
            Assert.True(await pending);
            var selected = vm.SelectedDataset!;
            var bytes = File.ReadAllBytes(selected.Path);
            var recordings = Path.Combine(fixture.WorkspaceRoot, "recordings");
            var entries = Directory.GetFileSystemEntries(recordings, "*", SearchOption.AllDirectories);
            File.WriteAllText(source, "{\"schemaVersion\":4,\"planId\":\"sample\",\"samples\":[null]}");
            picker.Pending = new();
            pending = view.ImportRecordingAsync();
            picker.Pending.SetResult(source);
            Assert.False(await pending);
            Assert.Contains("samples", vm.Error!);
            Assert.Same(selected, vm.SelectedDataset);
            Assert.Equal(bytes, File.ReadAllBytes(selected.Path));
            Assert.Equal(entries, Directory.GetFileSystemEntries(recordings, "*", SearchOption.AllDirectories));
        }
        finally { owner.Close(); vm.StopRecovery(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainWindow_owner_context_replacement_suppresses_pending_recording_success_and_error(bool fails)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        using var replacement = new AuthoringUiFixture();
        fixture.Show(); fixture.OpenRememberedWorkspace();
        await fixture.ViewModel.StopRecoveryAsync();
        replacement.ViewModel.Open(replacement.WorkspaceRoot); await replacement.ViewModel.StopRecoveryAsync();
        var source = Recording(fixture.WorkspaceRoot, fixture.ViewModel.SelectedProgram!.PlanId);
        var picker = new ControlledRecordingPicker();
        var preview = new WorkspacePreviewView(picker) { DataContext = fixture.ViewModel };
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 5;
        fixture.Window!.FindControl<ContentControl>("SeparatePreview")!.Content = preview;
        AuthoringUiFixture.Drain();
        var files = Directory.GetFiles(fixture.WorkspaceRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var program = fixture.ViewModel.SelectedProgram; var revision = fixture.ViewModel.SelectedDocument!.Revision;
        var status = fixture.ViewModel.Status;
        var pending = preview.ImportRecordingAsync();
        Assert.False(pending.IsCompleted); Assert.Same(fixture.Window, picker.Owner);
        fixture.Window!.DataContext = replacement.ViewModel;
        Assert.Same(fixture.ViewModel, preview.DataContext);
        if (fails) picker.Pending.SetException(new IOException("Late obsolete picker failure")); else picker.Pending.SetResult(source);
        Assert.False(await pending);
        Assert.Same(program, fixture.ViewModel.SelectedProgram); Assert.Equal(revision, fixture.ViewModel.SelectedDocument.Revision);
        Assert.Equal(status, fixture.ViewModel.Status); Assert.Null(fixture.ViewModel.Error); Assert.Null(replacement.ViewModel.Error);
        Assert.Empty(fixture.ViewModel.Datasets); Assert.Null(fixture.ViewModel.SelectedDataset);
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(fixture.WorkspaceRoot, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        fixture.Window!.DataContext = fixture.ViewModel;
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MainWindow_owner_context_replacement_suppresses_pending_folder_failure(bool throws, bool replaceOwner)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        using var replacement = new AuthoringUiFixture();
        fixture.Show(); fixture.OpenRememberedWorkspace(); await fixture.ViewModel.StopRecoveryAsync();
        replacement.ViewModel.Open(replacement.WorkspaceRoot); await replacement.ViewModel.StopRecoveryAsync();
        Directory.CreateDirectory(Path.Combine(fixture.WorkspaceRoot, "recordings"));
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = false;
        var preview = new WorkspacePreviewView(new ControlledRecordingPicker(), (owner, directory) =>
        {
            Assert.Same(fixture.Window, owner); Assert.Equal(Path.Combine(fixture.WorkspaceRoot, "recordings"), directory.FullName);
            requested = true; return completed.Task;
        })
        { DataContext = fixture.ViewModel };
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 5;
        fixture.Window!.FindControl<ContentControl>("SeparatePreview")!.Content = preview; AuthoringUiFixture.Drain();
        var status = fixture.ViewModel.Status; var revision = fixture.ViewModel.SelectedDocument!.Revision;
        var pending = preview.OpenRecordingFolderAsync(); Assert.True(requested); Assert.False(pending.IsCompleted);
        if (replaceOwner) fixture.Window!.DataContext = replacement.ViewModel;
        Assert.Same(fixture.ViewModel, preview.DataContext);
        if (throws) completed.SetException(new IOException("Late obsolete folder failure")); else completed.SetResult(false);
        await pending;
        Assert.Equal(revision, fixture.ViewModel.SelectedDocument.Revision);
        if (replaceOwner) { Assert.Equal(status, fixture.ViewModel.Status); Assert.Null(fixture.ViewModel.Error); }
        else
        {
            var error = throws ? "Late obsolete folder failure" : "Could not open the recordings folder.";
            Assert.Equal(error, fixture.ViewModel.Error); Assert.Equal(error, fixture.ViewModel.Status);
        }
        Assert.Null(replacement.ViewModel.Error);
        fixture.Window!.DataContext = fixture.ViewModel;
    }

    private static string Recording(string root, string planId)
    {
        var path = Path.Combine(root, planId + "-incoming.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new TestRunRecord
        {
            PlanId = planId,
            Samples = [new StoredSample { Channel = "VDC", Value = 2, ElapsedMs = 5 }]
        }, AppJsonContext.Default.TestRunRecord));
        return path;
    }

    private sealed class ControlledRecordingPicker : IAuthoringRecordingPicker
    {
        public TopLevel? Owner { get; private set; }
        public TaskCompletionSource<string?> Pending { get; set; } = new();
        public Task<string?> PickAsync(TopLevel owner) { Owner = owner; return Pending.Task; }
    }
}
