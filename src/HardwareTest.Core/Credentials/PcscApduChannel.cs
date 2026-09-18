namespace HardwareTest.Core.Credentials;

/// IApduChannel over an open PC/SC card handle.
internal sealed class PcscApduChannel : IApduChannel
{
    private readonly nint _card;
    private readonly int _protocol;

    public PcscApduChannel(nint card, int protocol)
    {
        _card = card;
        _protocol = protocol;
    }

    public byte[]? Transmit(byte[] command) => PcscNative.Transmit(_card, _protocol, command);
}
