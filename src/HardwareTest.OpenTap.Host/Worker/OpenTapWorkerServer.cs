using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Plugins.Basic;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host.Worker;

/// Worker-process dispatcher: one in-process <see cref="OpenTapSession"/> plus NDJSON on stdin/stdout.
public static class OpenTapWorkerServer
{
    public static async Task RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        OpenTapSession? session = null;
        VisaModeController? visa = null;
        AppSettings settings = new();
        var writeLock = new object();
        ILogger log = Log.ForContext(typeof(OpenTapWorkerServer));

        void Write(WorkerEnvelope envelope)
        {
            var line = WorkerProtocol.FormatLine(envelope);
            lock (writeLock)
            {
                output.WriteLine(line);
                output.Flush();
            }
        }

        void WriteEvent(long id, string method, OpenTapProgress progress)
        {
            Write(new WorkerEnvelope
            {
                Id = id,
                Kind = WorkerProtocol.KindEvent,
                Method = method,
                Ok = true,
                Payload = WorkerProtocol.SerializePayload(progress, WorkerJsonContext.Default.OpenTapProgress),
            });
        }

        void WriteOk(long id, string method, System.Text.Json.JsonElement? payload)
            => Write(new WorkerEnvelope
            {
                Id = id,
                Kind = WorkerProtocol.KindResponse,
                Method = method,
                Ok = true,
                Payload = payload,
            });

        void WriteError(long id, string method, string error)
            => Write(new WorkerEnvelope
            {
                Id = id,
                Kind = WorkerProtocol.KindResponse,
                Method = method,
                Ok = false,
                Error = error,
            });

        WorkerSnapshot RequireSnapshot()
            => WorkerSnapshot.Capture(session ?? throw new InvalidOperationException("Worker is not initialized."));

        OpenTapSession RequireSession()
            => session ?? throw new InvalidOperationException("Worker is not initialized.");

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            if (!WorkerProtocol.TryParseLine(line, out var envelope))
            {
                continue;
            }

            try
            {
                if (IsControlMethod(envelope.Method)
                    || envelope.Method is not (WorkerProtocol.Run or WorkerProtocol.RunSelection))
                {
                    await DispatchAsync(
                            envelope,
                            s => session = s,
                            () => visa,
                            v => visa = v,
                            () => settings,
                            s => settings = s,
                            log,
                            WriteOk,
                            WriteError,
                            WriteEvent,
                            RequireSession,
                            RequireSnapshot)
                        .ConfigureAwait(false);
                }
                else
                {
                    var captured = envelope;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await DispatchAsync(
                                    captured,
                                    s => session = s,
                                    () => visa,
                                    v => visa = v,
                                    () => settings,
                                    s => settings = s,
                                    log,
                                    WriteOk,
                                    WriteError,
                                    WriteEvent,
                                    RequireSession,
                                    RequireSnapshot)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            log.Warning(ex, "OpenTAP worker request {Method} failed", captured.Method);
                            WriteError(captured.Id, captured.Method, ex.Message);
                        }
                    }, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.Warning(ex, "OpenTAP worker request {Method} failed", envelope.Method);
                WriteError(envelope.Id, envelope.Method, ex.Message);
            }
        }
    }

    private static bool IsControlMethod(string method)
        => method is WorkerProtocol.Abort or WorkerProtocol.Pause or WorkerProtocol.Resume or WorkerProtocol.Ping;

    private static async Task DispatchAsync(
        WorkerEnvelope envelope,
        Action<OpenTapSession> setSession,
        Func<VisaModeController?> getVisa,
        Action<VisaModeController> setVisa,
        Func<AppSettings> getSettings,
        Action<AppSettings> setSettings,
        ILogger log,
        Action<long, string, System.Text.Json.JsonElement?> writeOk,
        Action<long, string, string> writeError,
        Action<long, string, OpenTapProgress> writeEvent,
        Func<OpenTapSession> requireSession,
        Func<WorkerSnapshot> requireSnapshot)
    {
        var method = envelope.Method;
        switch (method)
        {
            case WorkerProtocol.Init:
                {
                    var init = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerInitRequest)
                               ?? new WorkerInitRequest();
                    var settings = init.Settings ?? new AppSettings();
                    setSettings(settings);
                    var gate = new VisaSessionGate();
                    var bench = new BenchOperationCoordinator();
                    var runControl = new RunControl(gate);
                    var visa = new VisaModeController(
                        settings.UseMockVisa,
                        gate,
                        runControl,
                        message => log.Warning("{Message}", message),
                        bench: bench);
                    setVisa(visa);
                    var session = new OpenTapSession(
                        settings,
                        log,
                        visa,
                        bench,
                        cancelExecuteWithToken: true);
                    setSession(session);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                    return;
                }

            case WorkerProtocol.Ping:
                writeOk(envelope.Id, method, null);
                return;

            case WorkerProtocol.Shutdown:
                writeOk(envelope.Id, method, null);
                Environment.Exit(0);
                return;

            case WorkerProtocol.ApplySettings:
                {
                    var incoming = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.AppSettings);
                    if (incoming is not null)
                    {
                        CopySettings(getSettings(), incoming);
                        getVisa()?.TryApply(incoming.UseMockVisa, out _);
                    }

                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                    return;
                }

            case WorkerProtocol.LoadPlan:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerPathRequest)
                              ?? throw new InvalidOperationException("loadPlan requires a path.");
                    await requireSession().LoadPlanAsync(req.Path).ConfigureAwait(false);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                    return;
                }

            case WorkerProtocol.LoadSample:
                await requireSession().LoadSampleProgramAsync().ConfigureAwait(false);
                writeOk(
                    envelope.Id,
                    method,
                    WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                return;

            case WorkerProtocol.LoadBoardDemo:
                await requireSession().LoadBoardDemoProgramAsync().ConfigureAwait(false);
                writeOk(
                    envelope.Id,
                    method,
                    WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                return;

            case WorkerProtocol.LoadSweepDemo:
                await requireSession().LoadSweepDemoProgramAsync().ConfigureAwait(false);
                writeOk(
                    envelope.Id,
                    method,
                    WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                return;

            case WorkerProtocol.LoadTimingDemo:
                await requireSession().LoadTimingDemoProgramAsync().ConfigureAwait(false);
                writeOk(
                    envelope.Id,
                    method,
                    WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                return;

            case WorkerProtocol.LoadPlanShape:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerFixtureRequest)
                              ?? throw new InvalidOperationException("loadPlanShape requires a fixture name.");
                    await requireSession().LoadPlanShapeAsync(req.FixtureFileName).ConfigureAwait(false);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                    return;
                }

            case WorkerProtocol.TrySetStepEnabled:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerSetEnabledRequest)
                              ?? throw new InvalidOperationException("trySetStepEnabled requires a payload.");
                    var ok = requireSession().TrySetStepEnabled(req.StepPath, req.Enabled);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerBoolResult { Ok = ok, Snapshot = requireSnapshot() },
                            WorkerJsonContext.Default.WorkerBoolResult));
                    return;
                }

            case WorkerProtocol.TryGetStepConditionSummary:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerPathRequest)
                              ?? throw new InvalidOperationException("tryGetStepConditionSummary requires a payload.");
                    var ok = requireSession().TryGetStepConditionSummary(req.Path, out var summary);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerBoolResult { Ok = ok, Value = summary, Snapshot = requireSnapshot() },
                            WorkerJsonContext.Default.WorkerBoolResult));
                    return;
                }

            case WorkerProtocol.Run:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerRunRequest)
                              ?? new WorkerRunRequest();
                    var progress = new Progress<OpenTapProgress>(p => writeEvent(envelope.Id, WorkerProtocol.Progress, p));
                    // Cooperative cancel is Abort (cancels OpenTapSession._runCts). Do not bind this
                    // wait to a client token — abandoning IPC does not stop the plan thread.
                    var summary = await requireSession()
                        .RunAsync(progress, CancellationToken.None, req.RunId)
                        .ConfigureAwait(false);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerRunResult { Summary = summary, Snapshot = requireSnapshot() },
                            WorkerJsonContext.Default.WorkerRunResult));
                    return;
                }

            case WorkerProtocol.RunSelection:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerRunSelectionRequest)
                              ?? throw new InvalidOperationException("runSelection requires a payload.");
                    var progress = new Progress<OpenTapProgress>(p => writeEvent(envelope.Id, WorkerProtocol.Progress, p));
                    // Cooperative cancel is Abort; see Run above.
                    var summary = await requireSession()
                        .RunSelectionAsync(req.StepPath, progress, CancellationToken.None, req.RunId, req.IncludeCleanup)
                        .ConfigureAwait(false);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerRunResult { Summary = summary, Snapshot = requireSnapshot() },
                            WorkerJsonContext.Default.WorkerRunResult));
                    return;
                }

            case WorkerProtocol.Pause:
                requireSession().Pause();
                writeOk(
                    envelope.Id,
                    method,
                    WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                return;

            case WorkerProtocol.Resume:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerResumeRequest);
                    requireSession().Resume(req?.Response);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                    return;
                }

            case WorkerProtocol.Abort:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerAbortRequest)
                              ?? new WorkerAbortRequest();
                    requireSession().Abort(req.SafetyStop);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                    return;
                }

            case WorkerProtocol.ApplyStationAndDut:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerStationDutRequest)
                              ?? throw new InvalidOperationException("applyStationAndDut requires a payload.");
                    await requireSession()
                        .ApplyStationAndDutAsync(
                            new StationProfile(req.RoleToResource),
                            new DutIdentity(req.Serial, req.PartNumber, req.Revision, req.Family))
                        .ConfigureAwait(false);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(requireSnapshot(), WorkerJsonContext.Default.WorkerSnapshot));
                    return;
                }

            case WorkerProtocol.TrySetAcquireSettings:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerAcquireSettingsRequest)
                              ?? throw new InvalidOperationException("trySetAcquireSettings requires a payload.");
                    var ok = requireSession().TrySetAcquireSettings(req.StepPath, req.SampleCount, req.IntervalMs);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerBoolResult { Ok = ok, Snapshot = requireSnapshot() },
                            WorkerJsonContext.Default.WorkerBoolResult));
                    return;
                }

            case WorkerProtocol.TrySetMeanGteThreshold:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerMeanGteRequest)
                              ?? throw new InvalidOperationException("trySetMeanGteThreshold requires a payload.");
                    var ok = requireSession().TrySetMeanGteThreshold(req.StepPath, req.Threshold);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerBoolResult { Ok = ok, Snapshot = requireSnapshot() },
                            WorkerJsonContext.Default.WorkerBoolResult));
                    return;
                }

            case WorkerProtocol.TryRebindDmmResource:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerResourceRequest)
                              ?? throw new InvalidOperationException("tryRebindDmmResource requires a payload.");
                    var ok = requireSession().TryRebindDmmResource(req.Resource);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerBoolResult { Ok = ok, Snapshot = requireSnapshot() },
                            WorkerJsonContext.Default.WorkerBoolResult));
                    return;
                }

            case WorkerProtocol.TryBindSlotResource:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerBindSlotRequest)
                              ?? throw new InvalidOperationException("tryBindSlotResource requires a payload.");
                    var ok = requireSession().TryBindSlotResource(req.SlotName, req.Resource);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerBoolResult { Ok = ok, Snapshot = requireSnapshot() },
                            WorkerJsonContext.Default.WorkerBoolResult));
                    return;
                }

            case WorkerProtocol.EnumerateParameters:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerEnumerateParametersRequest)
                              ?? new WorkerEnumerateParametersRequest();
                    var items = requireSession()
                        .EnumerateParameters(req.Scope, req.StepPath, req.IncludeReadOnly, req.Listing)
                        .ToList();
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerParameterListResult { Items = items },
                            WorkerJsonContext.Default.WorkerParameterListResult));
                    return;
                }

            case WorkerProtocol.TryGetParameter:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerMemberKeyRequest)
                              ?? throw new InvalidOperationException("tryGetParameter requires a payload.");
                    var ok = requireSession().TryGetParameter(req.MemberKey, out var value);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerBoolResult { Ok = ok, Value = value },
                            WorkerJsonContext.Default.WorkerBoolResult));
                    return;
                }

            case WorkerProtocol.TrySetParameter:
                {
                    var req = WorkerProtocol.ReadPayload(envelope, WorkerJsonContext.Default.WorkerMemberKeyRequest)
                              ?? throw new InvalidOperationException("trySetParameter requires a payload.");
                    var ok = requireSession().TrySetParameter(req.MemberKey, req.Value ?? string.Empty);
                    writeOk(
                        envelope.Id,
                        method,
                        WorkerProtocol.SerializePayload(
                            new WorkerBoolResult { Ok = ok, Snapshot = requireSnapshot() },
                            WorkerJsonContext.Default.WorkerBoolResult));
                    return;
                }

            case WorkerProtocol.ListPluginDirectories:
                writeOk(
                    envelope.Id,
                    method,
                    WorkerProtocol.SerializePayload(
                        new WorkerPluginDirectoryListResult { Items = requireSession().ListPluginDirectories().ToList() },
                        WorkerJsonContext.Default.WorkerPluginDirectoryListResult));
                return;

            case WorkerProtocol.ListInstalledPackages:
                writeOk(
                    envelope.Id,
                    method,
                    WorkerProtocol.SerializePayload(
                        new WorkerPackageListResult { Items = requireSession().ListInstalledPackages().ToList() },
                        WorkerJsonContext.Default.WorkerPackageListResult));
                return;

            case WorkerProtocol.ListDiscoveredDeviceAddresses:
                writeOk(
                    envelope.Id,
                    method,
                    WorkerProtocol.SerializePayload(
                        new WorkerDiscoveredAddressListResult
                        {
                            Items = requireSession().ListDiscoveredDeviceAddresses().ToList(),
                        },
                        WorkerJsonContext.Default.WorkerDiscoveredAddressListResult));
                return;

            default:
                writeError(envelope.Id, method, $"Unknown worker method '{method}'.");
                return;
        }
    }

    private static void CopySettings(AppSettings dest, AppSettings src)
    {
        dest.UseMockVisa = src.UseMockVisa;
        dest.DataDirectory = src.DataDirectory;
        dest.DefaultVisaResource = src.DefaultVisaResource;
        dest.OpenTapPluginDirectories = [.. src.OpenTapPluginDirectories];
        dest.ExportOpenTapResults = src.ExportOpenTapResults;
        dest.IsEngineerDebugMode = src.IsEngineerDebugMode;
        dest.LogMinimumLevel = src.LogMinimumLevel;
        dest.OpenTapWorkerKillTimeoutMilliseconds = src.OpenTapWorkerKillTimeoutMilliseconds;
    }
}
