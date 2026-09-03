namespace HardwareTest.Core.Credentials;

/// Sends one ISO 7816 APDU and returns the response including SW1/SW2.
internal interface IApduChannel
{
    byte[]? Transmit(byte[] command);
}
