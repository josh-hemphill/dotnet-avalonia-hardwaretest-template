namespace HardwareTest.Core.Hardware;

/// Exclusive bench operations that must not overlap: mode swap, run, Instruments *IDN?*.
public enum BenchOperation
{
    ModeSwap,
    Run,
    IdQuery,
}

/// Fail-closed lock. TryEnter returns immediately when another operation holds the bench.
public interface IBenchOperationCoordinator
{
    BenchOperation? Current { get; }

    bool TryEnter(BenchOperation operation, out IDisposable? lease, out string statusMessage);
}

public sealed class BenchOperationCoordinator : IBenchOperationCoordinator
{
    private readonly object _sync = new();
    private BenchOperation? _current;

    public BenchOperation? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool TryEnter(BenchOperation operation, out IDisposable? lease, out string statusMessage)
    {
        lock (_sync)
        {
            if (_current is { } held)
            {
                lease = null;
                statusMessage = BusyMessage(operation, held);
                return false;
            }

            _current = operation;
            lease = new Lease(this);
            statusMessage = string.Empty;
            return true;
        }
    }

    private void Exit()
    {
        lock (_sync)
        {
            _current = null;
        }
    }

    internal static string BusyMessage(BenchOperation requested, BenchOperation held)
    {
        var heldText = held switch
        {
            BenchOperation.ModeSwap => "a VISA mode switch",
            BenchOperation.Run => "a run",
            BenchOperation.IdQuery => "an Instruments query",
            _ => "another bench operation",
        };

        return requested switch
        {
            BenchOperation.ModeSwap =>
                $"Cannot switch VISA mode while {heldText} is in progress.",
            BenchOperation.Run =>
                $"Cannot start a run while {heldText} is in progress.",
            BenchOperation.IdQuery =>
                $"Cannot query *IDN? while {heldText} is in progress.",
            _ => $"Bench is busy ({heldText}).",
        };
    }

    private sealed class Lease(BenchOperationCoordinator owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            owner.Exit();
        }
    }
}
