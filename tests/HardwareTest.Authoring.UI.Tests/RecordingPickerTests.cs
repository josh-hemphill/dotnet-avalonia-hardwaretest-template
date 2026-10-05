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
            File.WriteAllText(source, "{\"schemaVersion\":1,\"planId\":\"sample\",\"samples\":[null]}");
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
