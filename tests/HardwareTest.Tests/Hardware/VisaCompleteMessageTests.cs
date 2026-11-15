using System.Reflection;
using System.Text;
using HardwareTest.Core.Hardware;
using Ivi.Visa;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class VisaCompleteMessageTests
{
    [Fact]
    public async Task Query_preserves_complete_identity_with_spaces_and_uses_bounded_raw_IO()
    {
        var fake = SessionProxy.Create("Keysight Technologies,34461A,SN-1,1.0\r\n", ReadStatus.EndReceived);
        await using var session = new IviMessageVisaSession(fake);
        Assert.Equal("Keysight Technologies,34461A,SN-1,1.0", await session.QueryAsync("*IDN?"));
        var proxy = (SessionProxy)(object)fake;
        Assert.Equal("*IDN?\n", proxy.Command);
        Assert.Equal(1_048_577L, proxy.MaximumRead);
    }

    [Theory]
    [InlineData(ReadStatus.MaximumCountReached)]
    [InlineData(ReadStatus.Unknown)]
    public async Task Incomplete_message_is_rejected_instead_of_exposing_truncated_values(ReadStatus status)
    {
        var fake = SessionProxy.Create("1.25", status);
        await using var session = new IviMessageVisaSession(fake);
        await Assert.ThrowsAsync<IOException>(() => session.QueryAsync(":MEAS:VOLT:DC?"));
    }

    [Fact]
    public async Task Response_over_limit_is_rejected_before_parsing()
    {
        var fake = SessionProxy.Create(new string('1', 1_048_577), ReadStatus.EndReceived);
        await using var session = new IviMessageVisaSession(fake);
        await Assert.ThrowsAsync<IOException>(() => session.QueryAsync(":READ?"));
    }

    public class SessionProxy : DispatchProxy
    {
        private IMessageBasedRawIO _raw = null!;
        public string? Command;
        public long MaximumRead;
        public static IMessageBasedSession Create(string response, ReadStatus status)
        {
            var session = DispatchProxy.Create<IMessageBasedSession, SessionProxy>();
            var owner = (SessionProxy)(object)session;
            var raw = DispatchProxy.Create<IMessageBasedRawIO, RawProxy>();
            var proxy = (RawProxy)(object)raw;
            proxy.Owner = owner; proxy.Response = Encoding.UTF8.GetBytes(response); proxy.Status = status;
            owner._raw = raw;
            return session;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_ResourceName" => "MOCK::MESSAGE",
            "get_RawIO" => _raw,
            "Dispose" => null,
            _ => throw new InvalidOperationException("Unexpected VISA operation: " + method?.Name)
        };
    }
    public class RawProxy : DispatchProxy
    {
        public SessionProxy Owner = null!;
        public byte[] Response = [];
        public ReadStatus Status;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "Write") { Owner.Command = Encoding.UTF8.GetString((byte[])args![0]!); return null; }
            if (method?.Name == "Read" && args?.Length == 2)
            {
                Owner.MaximumRead = (long)args[0]!; args[1] = Status; return Response;
            }
            throw new InvalidOperationException("Unexpected raw VISA operation: " + method?.Name);
        }
    }
}
