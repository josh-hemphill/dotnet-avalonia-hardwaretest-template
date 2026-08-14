using HardwareTest.Core.Hardware;
using HardwareTest.Tests.Fixtures;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.InMemory;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class TracingVisaSessionTests
{
    [Fact]
    public async Task Logs_successful_write_and_query()
    {
        InMemorySink.Instance.Dispose();
        using var capture = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.InMemory()
            .CreateLogger();

        var gate = new VisaSessionGate();
        IVisaSession session = new TracingVisaSession(new MockVisaSession("MOCK::1"), gate, capture);
        await session.WriteAsync("*RST");
        var idn = await session.QueryAsync("*IDN?");

        Assert.Contains("MOCK", idn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(InMemorySink.Instance.LogEvents,
            e => e.Level == LogEventLevel.Debug && e.MessageTemplate.Text.Contains("VISA WRITE"));
        Assert.Contains(InMemorySink.Instance.LogEvents,
            e => e.Level == LogEventLevel.Debug && e.MessageTemplate.Text.Contains("VISA QUERY"));
        await session.DisposeAsync();
    }

    [Fact]
    public void Forwards_io_timeout_to_inner_session()
    {
        var inner = new MockVisaSession("MOCK::1");
        var session = new TracingVisaSession(inner, new VisaSessionGate());
        session.IoTimeoutMilliseconds = 12_000;
        Assert.Equal(12_000, inner.IoTimeoutMilliseconds);
        Assert.Equal(12_000, session.IoTimeoutMilliseconds);
    }

    [Fact]
    public async Task Logs_errors_from_inner_session()
    {
        InMemorySink.Instance.Dispose();
        using var capture = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.InMemory()
            .CreateLogger();

        var gate = new VisaSessionGate();
        IVisaSession session = new TracingVisaSession(new FailingVisaSession(), gate, capture);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.WriteAsync("X"));
        Assert.Contains(InMemorySink.Instance.LogEvents,
            e => e.Level == LogEventLevel.Error && e.MessageTemplate.Text.Contains("VISA WRITE failed"));
    }
}
