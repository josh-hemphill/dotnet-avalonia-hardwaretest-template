using HardwareTest.Features.Settings;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class OfflineBundleSettingsWorkflowTests
{
    private static readonly OfflineBundleInspection Inspection = new("Program fixture-plan · Linux/x64\n1 pinned package: Instrument 2.0.0", "Program: fixture-plan\nTarget: Linux/x64\nInstrument: 2.0.0\nProgram checks passed");

    [Theory]
    [InlineData("")]
    [InlineData("relative-bundle")]
    [InlineData("line\nbreak")]
    public async Task Invalid_paths_recover_to_the_path_field_without_invoking_validation(string path)
    {
        var vm = Create(); var calls = 0; string? focus = null;
        vm.OfflineBundleInspector = (_, _) => { calls++; return Task.FromResult(Inspection); };
        vm.OfflineBundleFocusRequested += value => focus = value;
        vm.OfflineBundlePath = path;
        await vm.CheckOfflineBundleAsync();
        Assert.Equal(0, calls); Assert.Equal(path, vm.OfflineBundlePath);
        Assert.Equal("Offline bundle folder", focus); Assert.Contains("absolute", vm.OfflineBundleStatus);
        Assert.False(vm.HasOfflineBundleInspection); Assert.False(vm.OfflineBundleIsChecking);
    }

    [Fact]
    public async Task A_noncooperative_old_check_cannot_publish_after_path_change_or_replace_a_new_result()
    {
        var vm = Create();
        var old = new TaskCompletionSource<OfflineBundleInspection>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        vm.OfflineBundlePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        vm.OfflineBundleInspector = (_, token) => { oldToken = token; return old.Task; };
        var pending = vm.CheckOfflineBundleAsync();
        Assert.True(vm.OfflineBundleIsChecking); Assert.False(vm.OfflineBundleCanEdit);
        vm.OfflineBundlePath += "-new";
        Assert.True(oldToken.IsCancellationRequested); Assert.True(vm.OfflineBundleCanEdit);
        vm.OfflineBundleInspector = (_, _) => Task.FromResult(Inspection);
        await vm.CheckOfflineBundleAsync();
        old.SetResult(new("STALE", "STALE")); await pending;
        Assert.Equal(Inspection.Summary, vm.OfflineBundleSummary);
        Assert.DoesNotContain("STALE", vm.OfflineBundleDetails);
        Assert.True(vm.HasOfflineBundleInspection);
        vm.StopOfflineBundleInspection();
    }

    [Fact]
    public async Task Closing_the_owning_view_cancels_check_and_prevents_late_approval()
    {
        var vm = Create(); var result = new TaskCompletionSource<OfflineBundleInspection>();
        vm.OfflineBundlePath = Path.GetTempPath(); vm.OfflineBundleInspector = (_, _) => result.Task;
        var pending = vm.CheckOfflineBundleAsync(); vm.StopOfflineBundleInspection();
        result.SetResult(Inspection); await pending;
        Assert.False(vm.HasOfflineBundleInspection); Assert.False(vm.OfflineBundleIsChecking);
        Assert.Equal(Path.GetTempPath(), vm.OfflineBundlePath); Assert.Contains("Recheck", vm.OfflineBundleStatus);
    }

    [Fact]
    public async Task Backend_failure_retains_folder_and_explains_recovery_without_installation_claims()
    {
        var vm = Create(); vm.OfflineBundlePath = Path.Combine(Path.GetTempPath(), "missing-bundle");
        vm.OfflineBundleInspector = (_, _) => throw new InvalidDataException("Package checksum mismatch: instrument.TapPackage");
        await vm.CheckOfflineBundleAsync();
        Assert.Contains("checksum mismatch", vm.OfflineBundleStatus); Assert.Contains("recopy", vm.OfflineBundleStatus);
        Assert.EndsWith("missing-bundle", vm.OfflineBundlePath); Assert.False(vm.HasOfflineBundleInspection);
        Assert.Empty(vm.OfflineBundleInstallationCommand);
    }

    [Fact]
    public async Task Mutating_a_checked_folder_revokes_the_snapshot_and_copy_command()
    {
        using var folder = new TemporaryFolder(); var vm = Create();
        vm.OfflineBundlePath = folder.Path; await vm.CheckOfflineBundleAsync();
        Assert.True(vm.HasOfflineBundleInspection);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        { if (args.PropertyName == nameof(vm.HasOfflineBundleInspection) && !vm.HasOfflineBundleInspection) changed.TrySetResult(); };
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "changed.txt"), "changed");
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(vm.HasOfflineBundleInspection); Assert.Empty(vm.OfflineBundleDetails);
        Assert.Contains("Recheck", vm.OfflineBundleStatus); vm.StopOfflineBundleInspection();
    }

    [Fact]
    public async Task Copy_requires_new_separate_application_and_fresh_data_paths_and_never_creates_them()
    {
        using var folder = new TemporaryFolder(); var store = new FakeSettingsStore(); var vm = Create(store);
        vm.OfflineBundlePath = folder.Path; await vm.CheckOfflineBundleAsync();
        string? copied = null; string? focus = null;
        vm.CopyTextAsync = text => { copied = text; return Task.CompletedTask; };
        vm.OfflineBundleFocusRequested += value => focus = value;
        vm.OfflineBundleDestination = folder.Path;
        await vm.CopyOfflineBundleCommand.ExecuteAsync(); Assert.Null(copied); Assert.Contains("already exists", vm.OfflineBundleCommandStatus);
        vm.OfflineBundleDestination = folder.Path + " new app"; vm.OfflineBundleDataDirectory = folder.Path;
        await vm.CopyOfflineBundleCommand.ExecuteAsync(); Assert.Null(copied); Assert.Equal("Fresh operator data folder", focus);
        vm.OfflineBundleDataDirectory = vm.OfflineBundleDestination;
        await vm.CopyOfflineBundleCommand.ExecuteAsync(); Assert.Null(copied); Assert.Contains("separate", vm.OfflineBundleCommandStatus);
        vm.OfflineBundleDataDirectory = folder.Path + " fresh data";
        await vm.CopyOfflineBundleCommand.ExecuteAsync(); Assert.NotNull(copied);
        Assert.Contains("--install-offline-bundle", copied); Assert.Contains("--data-directory", copied);
        Assert.False(Directory.Exists(vm.OfflineBundleDestination)); Assert.False(Directory.Exists(vm.OfflineBundleDataDirectory));
        Assert.Contains("nothing has been installed", vm.OfflineBundleCommandStatus); Assert.Equal(0, store.SaveAppCount);
        await vm.CopyOfflineBundleDetailsCommand.ExecuteAsync(); Assert.Equal(vm.OfflineBundleDetails, copied);
        Assert.Contains("FRESH", copied); Assert.Contains("Existing data-directory reuse is unsupported", copied);
        vm.StopOfflineBundleInspection();
    }

    [Theory]
    [InlineData(false, "'a'\"'\"'b'")]
    [InlineData(true, "'a''b'")]
    public void Copied_commands_quote_each_argument_for_the_platform_shell(bool windows, string escaped)
    {
        var command = SettingsViewModel.BuildOfflineBundleInstallationCommand("a'b", "new app", "fresh data", windows, "operator.dll");
        Assert.Contains("--install-offline-bundle " + escaped, command);
        Assert.Contains("--destination 'new app' --data-directory 'fresh data'", command);
        Assert.StartsWith(windows ? "& dotnet " : "dotnet ", command);
    }

    [Fact]
    public async Task Clipboard_and_picker_unavailability_keep_typed_and_selectable_fallbacks()
    {
        var vm = Create(); vm.OfflineBundlePath = Path.Combine(Path.GetTempPath(), "offline-bundle");
        vm.OfflineBundleFolderPickerUnavailable(); Assert.Contains("Type or paste", vm.OfflineBundleStatus);
        await vm.CheckOfflineBundleAsync(); await vm.CopyOfflineBundleDetailsCommand.ExecuteAsync();
        Assert.Contains("selectable", vm.OfflineBundleCommandStatus); Assert.True(vm.HasOfflineBundleInspection);
        Assert.Contains("Program checks passed", vm.OfflineBundleDetails); vm.StopOfflineBundleInspection();
    }

    private static SettingsViewModel Create(FakeSettingsStore? store = null) => new(store ?? new FakeSettingsStore(), new FakeOpenTapSession())
    { UiScheduler = action => action(), OfflineBundleInspector = (_, _) => Task.FromResult(Inspection) };

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "offline-settings-" + Guid.NewGuid().ToString("N"));
        public TemporaryFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
