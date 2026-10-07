using System.Reflection;
using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Host;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class PublishedDmmBrokerTests
{
    [Fact]
    public void Published_dmm_identity_measurement_reset_and_close_use_current_broker_bridge()
    {
        var broker = new RecordingVisaBroker();
        var dmm = PublishedLibraryFixture.CreateDmm();
        var providerProperty = PublishedLibraryFixture.ProviderProperty;
        var previous = providerProperty.GetValue(null);
        try
        {
            Assert.True(InstrumentComponentsScpiIo.TryRegisterProvider(broker));
            dmm.Open();
            Assert.Equal("MOCK::INSTR0", broker.LastOpened);
            Assert.Equal(1, broker.OpenCount);
            var identity = PublishedLibraryFixture.Call(dmm, "QueryIdn")!;
            Assert.Equal("FAKE,Broker,SN-1,0", PublishedLibraryFixture.Call(identity, "FormatResponse"));
            var view = dmm.GetType().GetProperty("Dmm")!.GetValue(dmm)!;
            Assert.Equal(1.25, PublishedLibraryFixture.Call(view, "MeasureVoltageDc", new object?[] { null }));
            PublishedLibraryFixture.Call(dmm, "Reset");
            Assert.Contains("*IDN?", broker.LastSession!.Queries);
            Assert.Contains(broker.LastSession.Queries, command => command.Contains("MEAS", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("*RST", broker.LastSession.Writes);
            Assert.Contains("*OPC?", broker.LastSession.Queries);
            dmm.Close();
            Assert.True(broker.LastSession.Disposed);
        }
        finally
        {
            dmm.Close();
            providerProperty.SetValue(null, previous);
        }
    }

    [Theory]
    [InlineData(30_000, 30_000)]
    [InlineData(1, IviVisaSessionFactory.MinIoTimeoutMilliseconds)]
    [InlineData(int.MaxValue, IviVisaSessionFactory.MaxIoTimeoutMilliseconds)]
    public void Published_dmm_applies_and_clamps_timeout_to_broker_session(int requested, int expected)
    {
        var broker = new RecordingVisaBroker();
        var dmm = PublishedLibraryFixture.CreateDmm();
        dmm.GetType().GetProperty("IoTimeoutMilliseconds")!.SetValue(dmm, requested);
        var property = PublishedLibraryFixture.ProviderProperty;
        var previous = property.GetValue(null);
        try
        {
            Assert.True(InstrumentComponentsScpiIo.TryRegisterProvider(broker));
            dmm.Open();
            Assert.Equal(expected, broker.LastSession!.IoTimeoutMilliseconds);
            PublishedLibraryFixture.Call(dmm, "QueryIdn");
            Assert.Equal(expected, broker.LastSession.IoTimeoutMilliseconds);
            dmm.Close();
            Assert.True(broker.LastSession.Disposed);
        }
        finally
        {
            dmm.Close();
            property.SetValue(null, previous);
        }
    }

    [Fact]
    public void Published_dmm_without_provider_fails_closed()
    {
        var dmm = PublishedLibraryFixture.CreateDmm();
        var property = PublishedLibraryFixture.ProviderProperty;
        var previous = property.GetValue(null);
        try
        {
            property.SetValue(null, null);
            var error = Assert.Throws<InvalidOperationException>(dmm.Open);
            Assert.Contains("Provider", error.Message, StringComparison.Ordinal);
        }
        finally { property.SetValue(null, previous); }
    }

    [Fact]
    public void Published_dmm_blank_resource_fails_before_broker_open()
    {
        var broker = new RecordingVisaBroker();
        var dmm = PublishedLibraryFixture.CreateDmm();
        Assert.True(InstrumentResourceAccess.TrySetResource(dmm, "  "));
        var property = PublishedLibraryFixture.ProviderProperty;
        var previous = property.GetValue(null);
        try
        {
            Assert.True(InstrumentComponentsScpiIo.TryRegisterProvider(broker));
            Assert.Throws<InvalidOperationException>(dmm.Open);
            Assert.Equal(0, broker.OpenCount);
            dmm.Close();
        }
        finally { property.SetValue(null, previous); }
    }
}

internal static class PublishedLibraryFixture
{
    internal static Assembly Assembly => PublishedLibraryMetadataFixture.Assembly;

    internal static PropertyInfo ProviderProperty => Assembly.GetType("InstrumentComponents.OpenTap.OpenTapScpiIo", true)!.GetProperty("Provider")!;

    internal static Instrument CreateDmm()
    {
        var instrument = (Instrument)Activator.CreateInstance(Assembly.GetType("InstrumentComponents.OpenTap.DmmInstrument", true)!)!;
        Assert.True(InstrumentResourceAccess.TrySetResource(instrument, "MOCK::INSTR0"));
        return instrument;
    }

    internal static object? Call(object target, string name, params object?[] arguments)
        => target.GetType().GetMethod(name)!.Invoke(target, arguments);
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
    public int IoTimeoutMilliseconds { get; set; } = IviVisaSessionFactory.DefaultIoTimeoutMilliseconds;
    public List<string> Queries { get; } = [];
    public List<string> Writes { get; } = [];
    public bool Disposed { get; private set; }

    public Task WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Writes.Add(command);
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

        if (command.Trim().Equals("*OPC?", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult("1");
        }

        if (command.Contains("MEAS", StringComparison.OrdinalIgnoreCase))
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
