using System.Collections.Concurrent;
using HardwareTest.Core.Hardware;

namespace HardwareTest.Tests.Fixtures;

/// Records VISA call timing so gate concurrency can be asserted.
public sealed class SpyVisaSession : IVisaSession
{
    private readonly ConcurrentBag<string> _log = new();
    private int _active;
    private int _maxActive;
    private readonly object _sync = new();

    public SpyVisaSession(string resourceName = "MOCK::SPY", TimeSpan? writeDelay = null)
    {
        ResourceName = resourceName;
        WriteDelay = writeDelay ?? TimeSpan.FromMilliseconds(40);
    }

    public string ResourceName { get; }
    public TimeSpan WriteDelay { get; }
    public int MaxActive => _maxActive;
    public IReadOnlyList<string> Log => _log.ToArray();

    public async Task WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            _log.Add($"W:{command}:{DateTime.UtcNow:O}");
            await Task.Delay(WriteDelay, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    public async Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            _log.Add($"Q:{command}:{DateTime.UtcNow:O}");
            await Task.Delay(WriteDelay, cancellationToken).ConfigureAwait(false);
            return "1.0";
        }
        finally
        {
            Exit();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Enter()
    {
        lock (_sync)
        {
            _active++;
            if (_active > _maxActive)
            {
                _maxActive = _active;
            }
        }
    }

    private void Exit()
    {
        lock (_sync)
        {
            _active--;
        }
    }
}

public sealed class SpyVisaSessionFactory : IVisaSessionFactory
{
    public SpyVisaSession? LastSession { get; private set; }

    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        LastSession = new SpyVisaSession(resourceName);
        return Task.FromResult<IVisaSession>(LastSession);
    }
}

public sealed class FailingVisaSession : IVisaSession
{
    public string ResourceName => "MOCK::FAIL";

    public Task WriteAsync(string command, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("write failed");

    public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("query failed");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
