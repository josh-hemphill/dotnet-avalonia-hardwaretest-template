using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using HardwareTest;
using HardwareTest.Core.Settings;
using ReactiveUI.Avalonia;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(HardwareTest.E2E.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HardwareTest.E2E.Tests;

public static class TestAppBuilder
{
    private static int _setupCount;

    public static AppBuilder BuildAvaloniaApp()
    {
        App.SettingsStoreFactory = CreateIsolatedSettingsStore;
        var builder = AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            })
            .WithInterFont()
            .UseReactiveUI(_ => { });

        var initializeRuntime = builder.RuntimePlatformServicesInitializer
            ?? throw new InvalidOperationException("The headless runtime initializer is missing.");
        return builder.UseRuntimePlatformSubsystem(() =>
        {
            // Avalonia 12 resets the global dispatcher before each isolated case.
            // Headless otherwise claims UIThread only after Skia/font setup, when
            // an outgoing background callback can already have claimed it. Claim
            // the actual session thread at the first per-test setup boundary.
            // This callback runs per SetupUnsafe, not when the builder is created.
            var setup = Interlocked.Increment(ref _setupCount);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var global = Dispatcher.UIThread;
            if (!ReferenceEquals(dispatcher, global))
                throw new InvalidOperationException($"Headless setup {setup} ran on thread {Environment.CurrentManagedThreadId}, but UIThread was already claimed by thread {global.Thread.ManagedThreadId}.");
            if (Environment.GetEnvironmentVariable("HT_DIAGNOSE_HEADLESS") == "1")
                System.Diagnostics.Trace.WriteLine($"Headless setup {setup}: session and UI dispatcher thread {dispatcher.Thread.ManagedThreadId}.");
            initializeRuntime();
        }, builder.RuntimePlatformServicesName ?? "Headless test runtime");
    }

    private static ISettingsStore CreateIsolatedSettingsStore()
    {
        var joined = Environment.GetEnvironmentVariable("HT_CROSS_APP_REVIEW_DIRECTORY");
        var persistentJoinedBench = Environment.GetEnvironmentVariable("HT_CROSS_APP_OPERATOR_PERSISTENCE") == "1"
            && !string.IsNullOrWhiteSpace(joined) && Path.IsPathFullyQualified(joined);
        // Only the explicitly selected, multi-process joined workflow uses the
        // same bench data across its restart. Ordinary cases remain isolated.
        var root = persistentJoinedBench ? Path.Combine(joined!, "bench-data")
            : Path.Combine(Path.GetTempPath(), "HardwareTestE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new SettingsStore(root);
        store.LoadAsync().GetAwaiter().GetResult();
        store.AppSettings.UseMockVisa = true;
        return store;
    }
}
