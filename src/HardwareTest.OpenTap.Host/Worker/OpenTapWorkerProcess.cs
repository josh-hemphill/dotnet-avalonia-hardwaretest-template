using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using HardwareTest.Core.Settings;
using Serilog;
using StreamJsonRpc;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host.Worker;

public sealed class OpenTapWorkerProcessException : InvalidOperationException
{
    public OpenTapWorkerProcessException(string message)
        : base(message)
    {
    }
}

/// Owns the OpenTAP worker child process and its StreamJsonRpc channel.
public sealed class OpenTapWorkerProcess : IDisposable
{
    public const string ExecutableName = "HardwareTest.OpenTap.Worker";

    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<long, Action<WorkerEnvelope>> _eventHandlers = new();
    private readonly StringBuilder _stderr = new();
    private readonly object _stderrLock = new();
    private Process? _process;
    private JsonRpc? _rpc;
    private long _nextId = 1;
    private int _disposed;

    public OpenTapWorkerProcess(ILogger? logger = null, string? executablePath = null)
    {
        _logger = logger ?? Log.ForContext<OpenTapWorkerProcess>();
        ExecutablePath = executablePath;
    }

    public string? ExecutablePath { get; }

    public bool IsAlive
    {
        get
        {
            try
            {
                return _process is { HasExited: false };
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public int? ExitCode
    {
        get
        {
            try
            {
                return _process is { HasExited: true } p ? p.ExitCode : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public string StderrTail
    {
        get
        {
            lock (_stderrLock)
            {
                return _stderr.ToString();
            }
        }
    }

    public static string ResolveExecutablePath(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var fileName = OperatingSystem.IsWindows() ? ExecutableName + ".exe" : ExecutableName;
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException($"OpenTAP worker executable not found at '{path}'.", path);
    }

    public void EnsureStarted(AppSettings settings)
    {
        if (!IsAlive)
        {
            Start(settings);
        }
    }

    public void Start(AppSettings settings)
    {
        Stop(writeDossier: false);
        var exe = ResolveExecutablePath(ExecutablePath);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "false";

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            AppendStderr(e.Data);
            _logger.Debug("OpenTAP worker stderr: {Line}", e.Data);
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start OpenTAP worker '{exe}'.");
        }

        process.BeginErrorReadLine();
        _process = process;
        var handler = new HeaderDelimitedMessageHandler(
            process.StandardInput.BaseStream,
            process.StandardOutput.BaseStream,
            WorkerProtocol.CreateFormatter());
        var rpc = new JsonRpc(handler);
        rpc.AddLocalRpcTarget(new CallbackTarget(this));
        rpc.StartListening();
        _rpc = rpc;

        var init = Request(
                WorkerProtocol.Init,
                new WorkerInitRequest { Settings = settings },
                WorkerJsonContext.Default.WorkerInitRequest,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (!init.Ok)
        {
            Stop(writeDossier: false);
            throw new InvalidOperationException(init.Error ?? "OpenTAP worker init failed.");
        }
    }

    public Task<WorkerEnvelope> Request(string method, CancellationToken cancellationToken)
        => RequestCore<object?>(method, null, null, cancellationToken, onEvent: null);

    public Task<WorkerEnvelope> Request<TPayload>(
        string method,
        TPayload payload,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TPayload> payloadType,
        CancellationToken cancellationToken,
        Action<WorkerEnvelope>? onEvent = null)
        => RequestCore(method, payload, payloadType, cancellationToken, onEvent);

    private async Task<WorkerEnvelope> RequestCore<TPayload>(
        string method,
        TPayload? payload,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TPayload>? payloadType,
        CancellationToken cancellationToken,
        Action<WorkerEnvelope>? onEvent)
    {
        var rpc = _rpc;
        if (!IsAlive || rpc is null)
        {
            throw new OpenTapWorkerProcessException("OpenTAP worker is not running.");
        }

        var id = Interlocked.Increment(ref _nextId);
        if (onEvent is not null)
        {
            _eventHandlers[id] = onEvent;
        }

        var envelope = new WorkerEnvelope { Id = id, Method = method, Ok = true };
        if (payload is not null && payloadType is not null)
        {
            envelope.Payload = WorkerProtocol.SerializePayload(payload, payloadType);
        }

        try
        {
            var response = await rpc.InvokeWithCancellationAsync<WorkerEnvelope>(
                    "invoke",
                    [envelope],
                    cancellationToken)
                .ConfigureAwait(false);
            if (method == WorkerProtocol.Shutdown && response.Ok)
            {
                Stop(writeDossier: false);
            }

            return response;
        }
        catch (ConnectionLostException ex)
        {
            var stderr = StderrTail.Trim();
            throw new OpenTapWorkerProcessException(
                string.IsNullOrWhiteSpace(stderr) ? ex.Message : $"{ex.Message} {stderr}");
        }
        finally
        {
            _eventHandlers.TryRemove(id, out _);
        }
    }

    public void KillTree()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit(milliseconds: 2000);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to kill OpenTAP worker process tree.");
        }
    }

    public void Stop(bool writeDossier)
    {
        _ = writeDossier;
        try
        {
            _rpc?.Dispose();
        }
        catch
        {
            // ignore
        }

        _rpc = null;
        _eventHandlers.Clear();
        KillTree();
        try
        {
            _process?.Dispose();
        }
        catch
        {
            // ignore
        }

        _process = null;
        lock (_stderrLock)
        {
            _stderr.Clear();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Stop(writeDossier: false);
        }
    }

    private void DispatchProgress(WorkerEnvelope envelope)
    {
        if (!_eventHandlers.TryGetValue(envelope.Id, out var handler))
        {
            return;
        }

        try
        {
            handler(envelope);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP worker event handler failed.");
        }
    }

    private void AppendStderr(string line)
    {
        lock (_stderrLock)
        {
            _stderr.AppendLine(line);
            const int maxChars = 32 * 1024;
            if (_stderr.Length > maxChars)
            {
                _stderr.Remove(0, _stderr.Length - maxChars);
            }
        }
    }

    private sealed class CallbackTarget(OpenTapWorkerProcess owner)
    {
        [JsonRpcMethod("progress")]
        public void Progress(WorkerEnvelope envelope)
            => owner.DispatchProgress(envelope);
    }
}
