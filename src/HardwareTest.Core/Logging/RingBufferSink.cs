using System.Globalization;
using System.Text;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace HardwareTest.Core.Logging;

/// Bounded in-memory Serilog sink for crash log tails (not the rolling file).
public sealed class RingBufferSink : ILogEventSink
{
    public const int DefaultCapacity = 3000;
    private readonly object _gate = new();
    private readonly string[] _slots;
    private readonly int _capacity;
    private readonly MessageTemplateTextFormatter _formatter;
    private int _next;
    private int _count;

    public RingBufferSink(int capacity = DefaultCapacity)
    {
        _capacity = Math.Clamp(capacity, 8, 20_000);
        _slots = new string[_capacity];
        _formatter = new MessageTemplateTextFormatter(
            "[{Timestamp:O} {Level:u3}] ({TestRunId}) {Message:lj}{NewLine}{Exception}");
    }

    /// Process-wide sink registered by LoggingBootstrap (nullable until init).
    public static RingBufferSink? Shared { get; internal set; }

    public void Emit(LogEvent logEvent)
    {
        try
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            _formatter.Format(logEvent, writer);
            var line = writer.ToString().TrimEnd('\r', '\n');
            lock (_gate)
            {
                _slots[_next] = line;
                _next = (_next + 1) % _capacity;
                if (_count < _capacity)
                {
                    _count++;
                }
            }
        }
        catch
        {
            // Sink must never throw.
        }
    }

    /// Returns oldest→newest entries, optionally truncated to maxChars.
    public string DrainText(int maxChars = 256 * 1024)
    {
        try
        {
            string[] snapshot;
            int count;
            int next;
            lock (_gate)
            {
                count = _count;
                next = _next;
                snapshot = (string[])_slots.Clone();
            }

            var sb = new StringBuilder(Math.Min(maxChars, 64 * 1024));
            var start = count < _capacity ? 0 : next;
            for (var i = 0; i < count; i++)
            {
                var line = snapshot[(start + i) % _capacity];
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (sb.Length + line.Length + 1 > maxChars)
                {
                    break;
                }

                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                sb.Append(line);
            }

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }
}
