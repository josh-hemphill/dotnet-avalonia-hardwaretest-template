using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringLoadingTests
{
    [AvaloniaFact]
    public async Task Program_picker_reports_progress_and_keeps_unsaved_drafts()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show();
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.DisplayName = "Unsaved sample name";
        vm.InitializePlan(new("another-program") { Instruments = [] });
        var stages = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.LoadingStage)) stages.Add(vm.LoadingStage);
        };
        fixture.Control<ListBox>("Programs").SelectedItem = vm.ProgramRows.Single(row => row.PlanId == "sample");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (vm.LoadingBusy) await Task.Delay(1, timeout.Token);
        Assert.Equal("sample", vm.SelectedProgram!.PlanId);
        Assert.Equal("Unsaved sample name", vm.DisplayName);
        Assert.Contains(stages, stage => stage?.Contains("sample", StringComparison.Ordinal) == true);
        Assert.Null(vm.LoadingStage);
        Assert.Null(vm.Error);
    }

    [AvaloniaFact]
    public async Task Workspace_import_yields_to_UI_and_publishes_only_after_preparation()
    {
        using var compiler = new GatedCompiler();
        using var fixture = new AuthoringUiFixture(compiler: compiler);
        fixture.Show();
        var callerThread = Environment.CurrentManagedThreadId;
        var loading = fixture.Window!.OpenWorkspaceAsync(fixture.WorkspaceRoot);
        try
        {
            Assert.True(fixture.ViewModel.LoadingBusy);
            Assert.Contains("programs", fixture.ViewModel.LoadingStage);
            Assert.False(loading.IsCompleted);
            Assert.False(fixture.ViewModel.HasWorkspace);
            Assert.False(await fixture.Window.ExecuteCommandAsync("save-all"));
            Assert.Null(fixture.ViewModel.Error);
            await compiler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(callerThread, compiler.LoadThread);
            Assert.False(await fixture.Window.OpenWorkspaceAsync(fixture.WorkspaceRoot));
        }
        finally { compiler.Release.Set(); }
        Assert.True(await loading);
        Assert.True(fixture.ViewModel.HasWorkspace);
        Assert.False(fixture.ViewModel.LoadingBusy);
        Assert.Null(fixture.ViewModel.LoadingStage);
    }

    [AvaloniaFact]
    public async Task Failed_open_clears_progress_and_preserves_existing_draft()
    {
        using var fixture = new AuthoringUiFixture();
        fixture.ViewModel.Open(fixture.WorkspaceRoot);
        fixture.ViewModel.DisplayName = "Keep this draft";
        fixture.Show();
        fixture.Interaction.Choice = UnsavedChangesChoice.Discard;
        var selected = fixture.ViewModel.SelectedProgram;
        Assert.False(await fixture.Window!.OpenWorkspaceAsync(Path.Combine(fixture.WorkspaceRoot, "missing")));
        Assert.Same(selected, fixture.ViewModel.SelectedProgram);
        Assert.Equal("Keep this draft", fixture.ViewModel.DisplayName);
        Assert.False(fixture.ViewModel.LoadingBusy);
        Assert.Null(fixture.ViewModel.LoadingStage);
        Assert.NotNull(fixture.ViewModel.Error);
    }

    [AvaloniaFact]
    public async Task Obsolete_owner_cannot_publish_prepared_workspace()
    {
        using var compiler = new GatedCompiler();
        using var fixture = new AuthoringUiFixture(compiler: compiler);
        var current = true;
        var loading = fixture.ViewModel.OpenAsync(fixture.WorkspaceRoot, ownerIsCurrent: () => current);
        try
        {
            await compiler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            current = false;
        }
        finally { compiler.Release.Set(); }
        Assert.False(await loading);
        Assert.False(fixture.ViewModel.HasWorkspace);
        Assert.False(fixture.ViewModel.LoadingBusy);
    }

    private sealed class GatedCompiler : IPlanCompiler, IDisposable
    {
        private readonly PlanCompiler _inner = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public int LoadThread { get; private set; }
        public DraftWorkspace LoadAll(AuthoringWorkspace workspace)
        {
            LoadThread = Environment.CurrentManagedThreadId;
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Test did not release workspace import.");
            return _inner.LoadAll(workspace);
        }
        public void Save(ProgramDraft draft, string path) => _inner.Save(draft, path);
        public ProgramDraft Load(string path) => _inner.Load(path);
        public void SaveSidecar(string path, ProgramSidecar sidecar) => _inner.SaveSidecar(path, sidecar);
        public void Dispose() => Release.Dispose();
    }
}
