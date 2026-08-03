using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// Shared OpenTAP instrument type for mock and VISA DMMs (keeps step properties strongly typed).
public abstract class HardwareDmm : Instrument, IDmmInstrument
{
    public abstract string QueryIdn();
    public abstract void ConfigureDcVolts();
    public abstract double ReadVoltage();
    public abstract void OutputOff();
    public abstract void Reset();
}
