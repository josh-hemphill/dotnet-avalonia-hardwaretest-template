using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class VisaDmmInstrumentTests
{
    [Fact]
    public void Open_QueryIdn_and_ReadVoltage_go_through_injected_broker()
    {
        var broker = new RecordingVisaBroker();
        var dmm = new VisaDmmInstrument(broker)
        {
            VisaAddress = "MOCK::INSTR0",
            IoTimeoutMilliseconds = 500,
        };

        dmm.Open();
        Assert.Equal("MOCK::INSTR0", broker.LastOpened);
        Assert.Equal(1, broker.OpenCount);
        Assert.Equal("FAKE,Broker,SN-1,0", dmm.QueryIdn());
        Assert.Equal(1.25, dmm.ReadVoltage());
        Assert.Equal("*IDN?", broker.LastSession!.Queries[0]);
        Assert.Equal("READ?", broker.LastSession.Queries[1]);
        Assert.Equal(2, broker.LastSession.Queries.Count);

        dmm.Close();
        Assert.True(broker.LastSession.Disposed);
    }

    [Fact]
    public void Open_without_registered_broker_fails_closed()
    {
        var dmm = new VisaDmmInstrument { VisaAddress = "MOCK::INSTR0" };
        var ex = Assert.Throws<InvalidOperationException>(dmm.Open);
        Assert.Contains("IVisaBroker", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_VisaAddress_skips_broker_open()
    {
        var broker = new RecordingVisaBroker();
        var dmm = new VisaDmmInstrument(broker) { VisaAddress = "  " };
        dmm.Open();
        Assert.Equal(0, broker.OpenCount);
        dmm.Close();
    }
}

file sealed class RecordingVisaBroker : IVisaBroker
{
    public string? LastOpened { get; private set; }
    public int OpenCount { get; private set; }
    public RecordingVisaSession? LastSession { get; private set; }

    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastOpened = resourceName;
        OpenCount++;
        LastSession = new RecordingVisaSession(resourceName);
        return Task.FromResult<IVisaSession>(LastSession);
    }
}

file sealed class RecordingVisaSession : IVisaSession
{
    public RecordingVisaSession(string resourceName) => ResourceName = resourceName;

    public string ResourceName { get; }
    public List<string> Queries { get; } = [];
    public bool Disposed { get; private set; }

    public Task WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Queries.Add(command);
        if (command.Trim().Equals("*IDN?", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult("FAKE,Broker,SN-1,0");
        }

        if (command.Trim().Equals("READ?", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult("1.25");
        }

        return Task.FromResult(string.Empty);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
