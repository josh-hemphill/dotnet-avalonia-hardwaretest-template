using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using HardwareTest.Core.Settings;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host.Worker;

/// Raised when the worker process died or the IPC channel closed — not a protocol Ok:false error.
public sealed class OpenTapWorkerProcessException : InvalidOperationException
{
    public OpenTapWorkerProcessException(string message)
        : base(message)
    {
    }
}

/// Owns the OpenTAP worker child process and NDJSON stdin/stdout.
public sealed class OpenTapWorkerProcess : IDisposable
{
    public const string ExecutableName = "HardwareTest.OpenTap.Worker";

    private readonly ILogger _logger;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<long, PendingRequest> _pending = new();
    private readonly StringBuilder _stderr = new();
    private readonly object _stderrLock = new();
    private Process? _process;
    private StreamWriter? _stdin;
    private CancellationTokenSource? _readerCts;
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

        throw new FileNotFoundException(
            $"OpenTAP worker executable not found at '{path}'.",
            path);
    }

    public void EnsureStarted(AppSettings settings)
    {
        if (IsAlive)
        {
            return;
        }

        Start(settings);
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
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
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
        process.Exited += (_, _) => FailAllPending("OpenTAP worker process exited.");

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start OpenTAP worker '{exe}'.");
        }

        process.BeginErrorReadLine();
        _process = process;
        _stdin = process.StandardInput;
        _readerCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadStdout(process, _readerCts.Token), _readerCts.Token);

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
        if (!IsAlive)
        {
            throw new OpenTapWorkerProcessException("OpenTAP worker is not running.");
        }

        var id = Interlocked.Increment(ref _nextId);
        var pending = new PendingRequest(onEvent);
        _pending[id] = pending;
        // Cancelling this token only abandons the IPC wait; it does not abort the worker.
        // Run/runSelection must pass CancellationToken.None and stop via Abort.
        using var reg = cancellationToken.Register(() => pending.TryCancel());

        var envelope = new WorkerEnvelope
        {
            Id = id,
            Kind = WorkerProtocol.KindRequest,
            Method = method,
            Ok = true,
        };
        if (payload is not null && payloadType is not null)
        {
            envelope.Payload = WorkerProtocol.SerializePayload(payload, payloadType);
        }

        try
        {
            Write(envelope);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        return await pending.Completion.Task.ConfigureAwait(false);
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
        FailAllPending("OpenTAP worker stopped.");
        _readerCts?.Cancel();
        KillTree();
        try
        {
            _stdin?.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _process?.Dispose();
        }
        catch
        {
            // ignore
        }

        _stdin = null;
        _process = null;
        _readerCts?.Dispose();
        _readerCts = null;
        lock (_stderrLock)
        {
            _stderr.Clear();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop(writeDossier: false);
    }

    private void Write(WorkerEnvelope envelope)
    {
        var line = WorkerProtocol.FormatLine(envelope);
        lock (_writeLock)
        {
            var stdin = _stdin ?? throw new InvalidOperationException("OpenTAP worker stdin is closed.");
            stdin.WriteLine(line);
            stdin.Flush();
        }
    }

    private void ReadStdout(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = process.StandardOutput.ReadLine();
                if (line is null)
                {
                    FailAllPending("OpenTAP worker stdout closed.");
                    return;
                }

                if (!WorkerProtocol.TryParseLine(line, out var envelope))
                {
                    continue;
                }

                Dispatch(envelope);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            FailAllPending("OpenTAP worker stdout ended.");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP worker stdout reader failed.");
            FailAllPending(ex.Message);
        }
    }

    private void Dispatch(WorkerEnvelope envelope)
    {
        if (!_pending.TryGetValue(envelope.Id, out var pending))
        {
            return;
        }

        if (string.Equals(envelope.Kind, WorkerProtocol.KindEvent, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                pending.OnEvent?.Invoke(envelope);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "OpenTAP worker event handler failed.");
            }

            return;
        }

        if (!_pending.TryRemove(envelope.Id, out _))
        {
            return;
        }

        pending.Completion.TrySetResult(envelope);
    }

    private void FailAllPending(string message)
    {
        foreach (var id in _pending.Keys.ToArray())
        {
            if (!_pending.TryRemove(id, out var pending))
            {
                continue;
            }

            pending.Completion.TrySetException(new OpenTapWorkerProcessException(message));
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

    private sealed class PendingRequest(Action<WorkerEnvelope>? onEvent)
    {
        public TaskCompletionSource<WorkerEnvelope> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Action<WorkerEnvelope>? OnEvent { get; } = onEvent;

        public void TryCancel()
            => Completion.TrySetCanceled();
    }
}
