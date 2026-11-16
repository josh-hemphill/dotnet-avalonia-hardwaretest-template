using System.Collections.Specialized;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Settings;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class SettingsUiThreadTests
{
    [Fact]
    public async Task Yielding_save_publishes_busy_feedback_and_provenance_only_through_UI_scheduler()
    {
        var store = new YieldingSettingsStore();
        var vm = new SettingsViewModel(store, new FakeOpenTapSession());
        var inScheduler = false;
        var outsideScheduler = 0;
        var publishedCollections = 0;
        vm.UiScheduler = action =>
        {
            inScheduler = true;
            try { action(); }
            finally { inScheduler = false; }
        };
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(vm.IsBusy) or nameof(vm.Status) or nameof(vm.AllowOsFolderBrowse))
                if (!inScheduler) Interlocked.Increment(ref outsideScheduler);
        };
        ((INotifyCollectionChanged)vm.ProvenanceRows).CollectionChanged += (_, _) =>
        {
            Interlocked.Increment(ref publishedCollections);
            if (!inScheduler) Interlocked.Increment(ref outsideScheduler);
        };

        await vm.SaveCommand.ExecuteAsync();

        Assert.True(store.CompletedAsynchronousWrite);
        Assert.Equal(0, outsideScheduler);
        Assert.True(publishedCollections > 0);
        Assert.False(vm.IsBusy);
        Assert.Contains("Saved at", vm.Status);
    }

    private sealed class YieldingSettingsStore : ISettingsStore
    {
        private readonly FakeSettingsStore _inner = new();
        public bool CompletedAsynchronousWrite { get; private set; }
        public AppSettings AppSettings => _inner.AppSettings;
        public UiState UiState => _inner.UiState;
        public string RootDirectory => _inner.RootDirectory;
        public string RunsDirectory => _inner.RunsDirectory;
        public string SettingsPath => _inner.SettingsPath;
        public IReadOnlyList<SettingProvenance> Provenance => _inner.Provenance;
        public bool IsSettingsWritable => _inner.IsSettingsWritable;
        public string? LastPersistenceError => _inner.LastPersistenceError;
        public string? SettingsSchemaWarning => _inner.SettingsSchemaWarning;
        public bool IsOverridden(string key) => _inner.IsOverridden(key);
        public Task LoadAsync(CancellationToken cancellationToken = default) => _inner.LoadAsync(cancellationToken);
        public Task LoadAsync(IReadOnlyDictionary<string, string>? environmentOverlays,
            IReadOnlyDictionary<string, string>? commandLineOverlays, Action<string>? warn = null,
            CancellationToken cancellationToken = default)
            => _inner.LoadAsync(environmentOverlays, commandLineOverlays, warn, cancellationToken);
        public event EventHandler? AppSettingsSaved;

        public async Task SaveAppSettingsAsync(CancellationToken cancellationToken = default)
        {
            await Task.Run(async () =>
            {
                await Task.Yield();
                await _inner.SaveAppSettingsAsync(cancellationToken);
                CompletedAsynchronousWrite = true;
                AppSettingsSaved?.Invoke(this, EventArgs.Empty);
            }, cancellationToken).ConfigureAwait(false);
        }

        public Task SaveUiStateAsync(CancellationToken cancellationToken = default)
            => _inner.SaveUiStateAsync(cancellationToken);
    }
}
