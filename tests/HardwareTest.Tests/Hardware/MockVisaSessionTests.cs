using HardwareTest.Core.Hardware;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class MockVisaSessionTests
{
    [Fact]
    public async Task Idn_and_overrides_are_returned()
    {
        var session = new MockVisaSession(
            "MOCK::1",
            new Dictionary<string, string> { ["FOO?"] = "BAR" });

        Assert.Contains("MOCK", await session.QueryAsync("*IDN?"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("BAR", await session.QueryAsync("FOO?"));
        Assert.Equal(string.Empty, await session.QueryAsync("UNKNOWN?"));
    }

    [Fact]
    public async Task Reset_completion_is_immediate_and_scripted_replies_can_override_it()
    {
        var session = new MockVisaSession("MOCK::1");
        await session.WriteAsync("*RST");
        Assert.Equal("1", await session.QueryAsync(" *opc? "));
        var scripted = new MockVisaSession("MOCK::1", new Dictionary<string, string> { ["*OPC?"] = "0" });
        Assert.Equal("0", await scripted.QueryAsync("*OPC?"));
    }

    [Fact]
    public async Task Read_returns_parseable_double()
    {
        var session = new MockVisaSession("MOCK::1");
        var raw = await session.QueryAsync("READ?");
        Assert.True(double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _));
    }

    [Fact]
    public async Task Write_and_query_honor_cancellation()
    {
        var session = new MockVisaSession("MOCK::1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.WriteAsync("*RST", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.QueryAsync("*IDN?", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.QueryAsync("*OPC?", cts.Token));
    }

    [Fact]
    public void Io_timeout_is_stored_on_the_session()
    {
        var session = new MockVisaSession("MOCK::1")
        {
            IoTimeoutMilliseconds = 30_000,
        };
        Assert.Equal(30_000, session.IoTimeoutMilliseconds);
    }
}
