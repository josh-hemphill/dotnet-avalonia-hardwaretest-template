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
        Assert.True(OpenTapStepKinds.IsIdentity(new IdentityQueryStep()));
        Assert.True(OpenTapStepKinds.IsSafeShutdown(new SafeShutdownStep()));
        Assert.True(OpenTapStepKinds.IsSafeShutdown(new LibraryStubs.SafeShutdownStep()));
        Assert.True(OpenTapStepKinds.IsPresentationExempt(new IdentityQueryStep()));
        Assert.True(OpenTapStepKinds.IsPresentationExempt(new LibraryStubs.SafeShutdownStep()));
        Assert.True(OpenTapStepKinds.RequiresHardwareDut(new IdentityCheckStep()));
        Assert.False(OpenTapStepKinds.RequiresHardwareDut(new IdentityQueryStep()));
        Assert.False(OpenTapStepKinds.IsIdentity(new AcquireVoltageStep()));
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

public sealed class IdentityQueryStep : TestStep
{
    public override void Run()
    {
    }
}

file static class LibraryStubs
{
    public sealed class SafeShutdownStep : TestStep
    {
        public override void Run()
        {
        }
    }
}

file sealed class RecordingVisaBroker : IVisaBroker
{
    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
        => Task.FromResult<IVisaSession>(new RecordingVisaSession(resourceName));
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
