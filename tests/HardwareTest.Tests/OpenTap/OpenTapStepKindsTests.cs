using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

public sealed class OpenTapStepKindsTests
{
    [Fact]
    public void Recognizes_basic_and_library_identity_and_shutdown_type_names()
    {
        Assert.True(OpenTapStepKinds.IsIdentity(new IdentityCheckStep()));
        Assert.True(OpenTapStepKinds.IsSafeShutdown(new SafeShutdownStep()));
        Assert.True(OpenTapStepKinds.RequiresHardwareDut(new IdentityCheckStep()));
        Assert.False(OpenTapStepKinds.IsIdentity(new AcquireVoltageStep()));

        Assert.True(OpenTapStepKinds.MatchesAuthoringStepType(
            typeof(InstrumentComponents.OpenTap.IdentityQueryStep),
            "IdentityQueryStep",
            "IdentityCheckStep"));
        Assert.False(OpenTapStepKinds.MatchesAuthoringStepType(
            typeof(InstrumentComponents.OpenTap.IdentityQueryStep),
            "IdentityCheckStep"));
        Assert.True(OpenTapStepKinds.MatchesAuthoringStepType(
            typeof(InstrumentComponents.OpenTap.SafeShutdownStep),
            "SafeShutdownStep"));
        Assert.False(typeof(InstrumentComponents.OpenTap.IdentityQueryStep).IsAssignableTo(typeof(HardwareDut)));
        Assert.False(OpenTapStepKinds.MatchesAuthoringStepType(
            typeof(OtherVendor.SafeShutdownStep),
            "SafeShutdownStep"));
        Assert.DoesNotContain(
            typeof(OpenTapStepKindsTests).Assembly.GetTypes(),
            t => t.IsClass && !t.IsAbstract && typeof(ITestStep).IsAssignableFrom(t));
    }
}

public sealed class InstrumentComponentsScpiIoTests
{
    [Fact]
    public void CreateIo_write_query_and_timeout_go_through_the_visa_session()
    {
        var session = new RecordingVisaSession("MOCK::DMM");
        var io = (ITestScpiIo)InstrumentComponentsScpiIo.CreateIo(
            typeof(ITestScpiIo),
            session,
            TimeSpan.FromMilliseconds(2500));

        Assert.Equal(2500, session.IoTimeoutMilliseconds);
        io.Write("CONF:VOLT:DC");
        Assert.Equal("FAKE,Broker,SN-1,0", io.Query("*IDN?"));
        Assert.Equal(["CONF:VOLT:DC"], session.Writes);
        Assert.Equal(["*IDN?"], session.Queries);

        io.IoTimeout = TimeSpan.FromMilliseconds(800);
        Assert.Equal(800, session.IoTimeoutMilliseconds);
        io.Dispose();
        Assert.True(session.Disposed);
    }

    [Fact]
    public void CreateProvider_open_uses_the_visa_broker()
    {
        var broker = new RecordingVisaBroker();
        var provider = (ITestScpiIoProvider)InstrumentComponentsScpiIo.CreateProvider(
            typeof(ITestScpiIoProvider),
            broker);

        var io = provider.Open("MOCK::DMM", TimeSpan.FromMilliseconds(1500));
        Assert.Equal("MOCK::DMM", broker.LastOpened);
        Assert.Equal(1500, broker.LastSession!.IoTimeoutMilliseconds);
        Assert.Equal("FAKE,Broker,SN-1,0", io.Query("*IDN?"));
        io.Dispose();
        Assert.True(broker.LastSession.Disposed);
    }

    [Fact]
    public void TryRegisterProvider_is_false_when_the_library_pack_is_not_loaded()
    {
        Assert.False(InstrumentComponentsScpiIo.TryRegisterProvider(new RecordingVisaBroker()));
    }
}

public interface ITestScpiIo : IDisposable
{
    TimeSpan IoTimeout { get; set; }

    void Write(string command);

    string Query(string command);
}

public interface ITestScpiIoProvider
{
    ITestScpiIo Open(string visaAddress, TimeSpan ioTimeout);
}

file sealed class RecordingVisaBroker : IVisaBroker
{
    public string? LastOpened { get; private set; }
    public RecordingVisaSession? LastSession { get; private set; }

    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        LastOpened = resourceName;
        LastSession = new RecordingVisaSession(resourceName);
        return Task.FromResult<IVisaSession>(LastSession);
    }
}

file sealed class RecordingVisaSession : IVisaSession
{
    public RecordingVisaSession(string resourceName) => ResourceName = resourceName;

    public string ResourceName { get; }
    public int IoTimeoutMilliseconds { get; set; }
    public List<string> Writes { get; } = [];
    public List<string> Queries { get; } = [];
    public bool Disposed { get; private set; }

    public Task WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        Writes.Add(command);
        return Task.CompletedTask;
    }

    public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        Queries.Add(command);
        return Task.FromResult("FAKE,Broker,SN-1,0");
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
