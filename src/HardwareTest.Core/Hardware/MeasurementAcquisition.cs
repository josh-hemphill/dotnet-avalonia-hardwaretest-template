using System.Globalization;
using System.Runtime.CompilerServices;

namespace HardwareTest.Core.Hardware;

/// Reads measurements from a VISA session into a fixed-capacity ring buffer.
public sealed class MeasurementAcquisition
{
    private readonly double[] _values;
    private readonly DateTimeOffset[] _timestamps;
    private int _count;
    private int _writeIndex;
    private readonly object _sync = new();

    public MeasurementAcquisition(int capacity = 4096)
    {
        if (capacity < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
        _values = new double[capacity];
        _timestamps = new DateTimeOffset[capacity];
    }

    public int Capacity { get; }
    public string Channel { get; private set; } = "CH1";

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    public void Reset(string channel)
    {
        lock (_sync)
        {
            Channel = channel;
            _count = 0;
            _writeIndex = 0;
            Array.Clear(_values);
            Array.Clear(_timestamps);
        }
    }

    public void Add(DateTimeOffset timestamp, double value)
    {
        lock (_sync)
        {
            _values[_writeIndex] = value;
            _timestamps[_writeIndex] = timestamp;
            _writeIndex = (_writeIndex + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }
        }
    }

    public MeasurementSample[] Snapshot()
    {
        lock (_sync)
        {
            if (_count == 0)
            {
                return Array.Empty<MeasurementSample>();
            }

            var result = new MeasurementSample[_count];
            var start = _count == Capacity ? _writeIndex : 0;
            for (var i = 0; i < _count; i++)
            {
                var idx = (start + i) % Capacity;
                result[i] = new MeasurementSample(Channel, _timestamps[idx], _values[idx]);
            }

            return result;
        }
    }

    public void CopyYs(Span<double> destination)
    {
        lock (_sync)
        {
            var n = Math.Min(destination.Length, _count);
            var start = _count == Capacity ? _writeIndex : 0;
            for (var i = 0; i < n; i++)
            {
                destination[i] = _values[(start + i) % Capacity];
            }
        }
    }

    public async IAsyncEnumerable<MeasurementSample> AcquireAsync(
        IVisaSession session,
        string channel,
        int sampleCount,
        int intervalMs,
        string? queryCommand = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        Engine.IRunControl? runControl = null)
    {
        Reset(channel);
        var command = queryCommand ?? "READ?";
        for (var i = 0; i < sampleCount; i++)
        {
            if (runControl is not null)
            {
                await runControl.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var raw = await session.QueryAsync(command, cancellationToken).ConfigureAwait(false);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                value = double.NaN;
            }

            var sample = new MeasurementSample(channel, DateTimeOffset.UtcNow, value);
            Add(sample.Timestamp, sample.Value);
            yield return sample;

            if (intervalMs > 0 && i < sampleCount - 1)
            {
                await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
