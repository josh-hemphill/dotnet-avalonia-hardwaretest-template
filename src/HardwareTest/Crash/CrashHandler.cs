using Avalonia.Threading;
using HardwareTest.Core.Crash;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Settings;
using ReactiveUI;
using Serilog;

namespace HardwareTest.Crash;

/// Installs process/UI/Rx exception surfaces and writes offline crash dossiers.
public static class CrashHandler
{
    private static readonly object Gate = new();
    private static bool _processHooksInstalled;
    private static bool _uiHooksInstalled;
    private static CrashDossierWriter? _writer;
    private static ISettingsStore? _settingsStore;
    private static BuildInfo? _buildInfo;
    private static Func<SafeStopOutcome>? _safeStop;
    private static Func<(string? RunId, string? PlanId, string? DutSerial, string? OperatorName, bool DutPresent, string? PlanIdForSession, bool EngineerMode, string? CredentialSerial)>? _sessionSnapshot;
    private static DateTimeOffset _lastRecoverableWriteUtc = DateTimeOffset.MinValue;
    private static readonly TimeSpan RecoverableMinInterval = TimeSpan.FromSeconds(30);

    /// Last recoverable crash message for in-panel status (Home reads this).
    public static string? LastRecoverableMessage { get; private set; }

    public static event EventHandler? RecoverableCrashOccurred;

    public static void Configure(
        ISettingsStore settingsStore,
        BuildInfo buildInfo,
        Func<SafeStopOutcome>? safeStop = null,
        Func<(string? RunId, string? PlanId, string? DutSerial, string? OperatorName, bool DutPresent, string? PlanIdForSession, bool EngineerMode, string? CredentialSerial)>? sessionSnapshot = null)
    {
        lock (Gate)
        {
            _settingsStore = settingsStore;
            _buildInfo = buildInfo;
            _safeStop = safeStop;
            _sessionSnapshot = sessionSnapshot;
            if (settingsStore.AppSettings.CrashEnabled)
            {
                _writer = CrashDossierWriter.FromSettings(settingsStore.AppSettings, settingsStore.RootDirectory);
            }
            else
            {
                _writer = null;
            }
        }
    }

    /// AppDomain + TaskScheduler — call after logging init, before Avalonia.
    public static void InstallProcessHooks()
    {
        lock (Gate)
        {
            if (_processHooksInstalled)
            {
                return;
            }

            _processHooksInstalled = true;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                Handle(e.ExceptionObject as Exception, isFatal: true, source: "AppDomain.UnhandledException");
            }
            catch
            {
                // must not throw
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                e.SetObserved();
                Log.Warning(e.Exception, "Unobserved task exception");
                Handle(e.Exception, isFatal: false, source: "TaskScheduler.UnobservedTaskException", rateLimit: true);
            }
            catch
            {
                // must not throw
            }
        };
    }

    /// Dispatcher + RxApp — call after ReactiveUI / DI are ready.
    public static void InstallUiHooks()
    {
        lock (Gate)
        {
            if (_uiHooksInstalled)
            {
                return;
            }

            _uiHooksInstalled = true;
        }

        try
        {
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                try
                {
                    Handle(e.Exception, isFatal: false, source: "Dispatcher.UIThread.UnhandledException", rateLimit: true);
                    e.Handled = true;
                    LastRecoverableMessage = FormatBanner(e.Exception, fatal: false);
                    RecoverableCrashOccurred?.Invoke(null, EventArgs.Empty);
                }
                catch
                {
                    e.Handled = true;
                }
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to install Dispatcher unhandled exception hook");
        }

        try
        {
            // ReactiveUI 23: configure via builder in Program.UseReactiveUI (InstallUiHooks is a no-op here if already set).
            // Fallback: wrap the current handler if Initialize is unavailable.
            var existing = RxState.DefaultExceptionHandler;
            if (existing is CrashRxExceptionHandler)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to inspect RxState exception handler");
        }
    }

    /// Called from Avalonia UseReactiveUI builder to replace the rethrowing default.
    public static void ConfigureReactiveUi(ReactiveUI.Builder.IReactiveUIBuilder builder)
    {
        try
        {
            builder.WithExceptionHandler(new CrashRxExceptionHandler());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to configure ReactiveUI exception handler");
        }
    }

    public static string? Capture(
        Exception? exception,
        bool isFatal,
        string source,
        bool rateLimit = false)
        => Handle(exception, isFatal, source, rateLimit);

    private static string? Handle(Exception? exception, bool isFatal, string source, bool rateLimit = false)
    {
        try
        {
            CrashDossierWriter? writer;
            ISettingsStore? store;
            BuildInfo? buildInfo;
            Func<SafeStopOutcome>? safeStop;
            Func<(string? RunId, string? PlanId, string? DutSerial, string? OperatorName, bool DutPresent, string? PlanIdForSession, bool EngineerMode, string? CredentialSerial)>? sessionSnapshot;
            lock (Gate)
            {
                writer = _writer;
                store = _settingsStore;
                buildInfo = _buildInfo;
                safeStop = _safeStop;
                sessionSnapshot = _sessionSnapshot;
            }

            if (store is not null && !store.AppSettings.CrashEnabled)
            {
                return null;
            }

            if (writer is null && store is not null)
            {
                writer = CrashDossierWriter.FromSettings(store.AppSettings, store.RootDirectory);
                lock (Gate)
                {
                    _writer ??= writer;
                }
            }

            if (writer is null)
            {
                return null;
            }

            if (rateLimit && !isFatal)
            {
                var now = DateTimeOffset.UtcNow;
                if (now - _lastRecoverableWriteUtc < RecoverableMinInterval)
                {
                    return null;
                }

                _lastRecoverableWriteUtc = now;
            }

            var safeStopOutcome = SafeStopOutcome.NotAttempted;
            if (isFatal)
            {
                safeStopOutcome = TrySafeStop(safeStop);
            }

            string? runId = null;
            string? planId = null;
            string? dutSerial = null;
            string? operatorName = null;
            string? credentialSerial = null;
            var dutPresent = false;
            string? sessionPlanId = null;
            var engineer = store?.AppSettings.IsEngineerDebugMode ?? false;
            try
            {
                if (sessionSnapshot is not null)
                {
                    (runId, planId, dutSerial, operatorName, dutPresent, sessionPlanId, engineer, credentialSerial) = sessionSnapshot();
                }
            }
            catch
            {
                // ignore snapshot failures
            }

            var redact = store?.AppSettings.RedactIdentifiersInDiagnostics ?? true;
            var report = CrashDossierWriter.BuildReport(
                exception,
                isFatal,
                source,
                safeStopOutcome,
                buildInfo,
                runId,
                planId,
                redact,
                dutSerial,
                operatorName,
                credentialSerial);

            CrashConfigSnapshot? config = null;
            try
            {
                if (store is not null)
                {
                    config = CrashDossierWriter.BuildConfigSnapshot(store.Provenance, redact, dutSerial, operatorName, credentialSerial);
                }
            }
            catch
            {
                // degrade
            }

            CrashSessionSnapshot? session = null;
            try
            {
                session = CrashDossierWriter.BuildSessionSnapshot(
                    dutPresent,
                    sessionPlanId ?? planId,
                    engineer,
                    dutSerial,
                    operatorName,
                    redact,
                    credentialSerial);
            }
            catch
            {
                // degrade
            }

            var context = new CrashCaptureContext
            {
                Report = report,
                Config = config,
                Session = session,
                LogTail = CrashDossierWriter.CaptureLogTail(),
                IdentifiersToRedact = [dutSerial, operatorName, credentialSerial],
            };

            var dir = writer.TryWrite(context);
            if (dir is not null)
            {
                try
                {
                    Log.Warning(
                        "Crash dossier written to {DossierPath} fatal={Fatal} source={Source}: {ExceptionType}: {Message}",
                        dir,
                        isFatal,
                        source,
                        exception?.GetType().Name ?? "null",
                        exception?.Message ?? string.Empty);
                }
                catch
                {
                    // ignore
                }
            }

            return dir;
        }
        catch
        {
            return null;
        }
    }

    private static SafeStopOutcome TrySafeStop(Func<SafeStopOutcome>? safeStop)
    {
        if (safeStop is null)
        {
            return SafeStopOutcome.NotAttempted;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var task = Task.Run(safeStop, cts.Token);
            if (!task.Wait(TimeSpan.FromSeconds(2)))
            {
                return SafeStopOutcome.TimedOut;
            }

            return task.Status == TaskStatus.RanToCompletion ? task.Result : SafeStopOutcome.Failed;
        }
        catch (Exception ex)
        {
            try
            {
                Log.Warning(ex, "Safe-stop during crash failed");
            }
            catch
            {
                // ignore
            }

            return SafeStopOutcome.Failed;
        }
    }

    private static string FormatBanner(Exception? ex, bool fatal)
    {
        var kind = fatal ? "Fatal" : "Recoverable";
        var type = ex?.GetType().Name ?? "Exception";
        var msg = ex?.Message ?? "unknown error";
        if (msg.Length > 120)
        {
            msg = msg[..120] + "…";
        }

        return $"{kind} fault ({type}): {msg}. See Home for crash dossier actions.";
    }

    private sealed class CrashRxExceptionHandler : IObserver<Exception>
    {
        public void OnNext(Exception value)
        {
            try
            {
                Handle(value, isFatal: false, source: "RxState.DefaultExceptionHandler", rateLimit: true);
                LastRecoverableMessage = FormatBanner(value, fatal: false);
                RecoverableCrashOccurred?.Invoke(null, EventArgs.Empty);
                Log.Error(value, "Unhandled ReactiveUI exception (recovered)");
            }
            catch
            {
                // must not rethrow
            }
        }

        public void OnError(Exception error)
        {
            OnNext(error);
        }

        public void OnCompleted()
        {
        }
    }
}
