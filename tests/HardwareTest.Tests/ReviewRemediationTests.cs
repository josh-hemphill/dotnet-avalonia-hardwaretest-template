using HardwareTest.Core;
using HardwareTest.Core.IO;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Xunit;

namespace HardwareTest.Tests;

public sealed class ReviewRemediationTests
{
    [Fact]
    public async Task Injected_AppSettings_stays_same_instance_and_picks_up_saves()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync(new Dictionary<string, string>(), new Dictionary<string, string>());

        var services = new ServiceCollection();
        services.AddHardwareTestCore(store);
        var provider = services.BuildServiceProvider();
        var injected = provider.GetRequiredService<AppSettings>();
        Assert.Same(store.AppSettings, injected);

        store.AppSettings.RunRetentionMaxRuns = 4242;
        store.AppSettings.ExportDirectory = "/mnt/usb/newtarget";
        await store.SaveAppSettingsAsync();

        Assert.Same(store.AppSettings, injected);
        Assert.Equal(4242, injected.RunRetentionMaxRuns);
        Assert.Equal("/mnt/usb/newtarget", injected.ExportDirectory);
    }

    [Fact]
    public void RunId_dotdot_stays_under_runs_directory()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var dir = store.GetRunDirectory("..");
        Assert.True(
            PathContainment.IsUnderRoot(temp.RunsDirectory, dir),
            $"runs='{temp.RunsDirectory}' resolved='{dir}'");
    }

    [Fact]
    public void Export_dotdot_relative_cannot_escape_root()
    {
        using var temp = new TempDataDirectory();
        var root = Path.Combine(temp.Path, "export");
        Directory.CreateDirectory(root);
        var settings = new AppSettings { DataDirectory = temp.Path };
        var svc = new ExportTargetService(settings, temp.Path, Log.Logger);
        var target = new ExportTarget
        {
            Id = "t",
            DisplayName = "t",
            RootPath = root,
            IsRemovable = false,
            AvailableBytes = long.MaxValue,
        };

        var written = svc.WriteAtomic(
            target,
            Path.Combine("..", "export-evil", "payload.bin"),
            [1, 2, 3]);

        Assert.False(File.Exists(Path.Combine(temp.Path, "export-evil", "payload.bin")));
        Assert.True(PathContainment.IsUnderRoot(root, written));
        Assert.True(File.Exists(written));
    }

    [Fact]
    public void PathContainment_rejects_sibling_prefix_match()
    {
        using var temp = new TempDataDirectory();
        var root = Path.Combine(temp.Path, "export");
        Directory.CreateDirectory(root);
        var sibling = Path.Combine(temp.Path, "export-evil", "payload.bin");
        Assert.False(PathContainment.IsUnderRoot(root, sibling));
    }

    [Fact]
    public async Task Settings_save_is_atomic_temp_files_are_cleaned()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync(new Dictionary<string, string>(), new Dictionary<string, string>());
        store.AppSettings.ThemePreference = "Dark";
        await store.SaveAppSettingsAsync();

        Assert.True(File.Exists(store.SettingsPath));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp-*"));
    }

    [Fact]
    public void PortableFileNames_collapses_dotdot()
    {
        Assert.Equal("_", PortableFileNames.Sanitize(".."));
        Assert.Equal("_", PortableFileNames.Sanitize("."));
        Assert.Equal("_", PortableFileNames.Sanitize(""));
    }
}
