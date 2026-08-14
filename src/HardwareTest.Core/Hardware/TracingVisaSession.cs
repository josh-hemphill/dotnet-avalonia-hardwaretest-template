using System.Diagnostics;
using System.Text;
using HardwareTest.Core.Logging;
using Serilog;

namespace HardwareTest.Core.Hardware;

/// Decorates an IVisaSession with structured timing and payload tracing.
public sealed class TracingVisaSession : IVisaSession
{
    private readonly IVisaSession _inner;
    private readonly ILogger _logger;
    private readonly VisaSessionGate _gate;

    public TracingVisaSession(IVisaSession inner, VisaSessionGate gate, ILogger? logger = null)
    {
        _inner = inner;
        _gate = gate;
        _logger = (logger ?? Log.ForContext<TracingVisaSession>())
            .ForContext("VisaResource", inner.ResourceName);
    }

    public string ResourceName => _inner.ResourceName;

    public int IoTimeoutMilliseconds
    {
        get => _inner.IoTimeoutMilliseconds;
        set => _inner.IoTimeoutMilliseconds = value;
    }

    public Task WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        return _gate.RunAsync(
            async ct =>
            {
                using var activity = TestTracing.StartVisa("Write", ResourceName);
                var sw = Stopwatch.StartNew();
                try
                {
                    await _inner.WriteAsync(command, ct).ConfigureAwait(false);
                    sw.Stop();
                    _logger.Debug(
                        "VISA WRITE {DurationMs}ms cmd={Command} hex={Hex}",
                        sw.Elapsed.TotalMilliseconds,
                        Truncate(command),
                        ToHexPreview(command));
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    _logger.Error(
                        ex,
                        "VISA WRITE failed after {DurationMs}ms cmd={Command}",
                        sw.Elapsed.TotalMilliseconds,
                        Truncate(command));
                    throw;
                }
            },
            cancellationToken);
    }

    public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        return _gate.RunAsync(
            async ct =>
            {
                using var activity = TestTracing.StartVisa("Query", ResourceName);
                var sw = Stopwatch.StartNew();
                try
                {
                    var response = await _inner.QueryAsync(command, ct).ConfigureAwait(false);
                    sw.Stop();
                    _logger.Debug(
                        "VISA QUERY {DurationMs}ms cmd={Command} response={Response} hex={Hex}",
                        sw.Elapsed.TotalMilliseconds,
                        Truncate(command),
                        Truncate(response),
                        ToHexPreview(response));
                    return response;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    _logger.Error(
                        ex,
                        "VISA QUERY failed after {DurationMs}ms cmd={Command}",
                        sw.Elapsed.TotalMilliseconds,
                        Truncate(command));
                    throw;
                }
            },
            cancellationToken);
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    private static string Truncate(string value, int max = 120)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.TrimEnd('\r', '\n');
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }

    private static string ToHexPreview(string value, int maxBytes = 32)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bytes = Encoding.ASCII.GetBytes(value);
        var take = Math.Min(bytes.Length, maxBytes);
        var sb = new StringBuilder(take * 3);
        for (var i = 0; i < take; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(bytes[i].ToString("X2"));
        }

        if (bytes.Length > maxBytes)
        {
            sb.Append(" …");
        }

        return sb.ToString();
    }
}
