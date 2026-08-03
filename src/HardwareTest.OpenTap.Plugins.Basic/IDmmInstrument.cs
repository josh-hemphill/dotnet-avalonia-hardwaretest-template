namespace HardwareTest.OpenTap.Plugins.Basic;

/// Shared DMM surface used by HardwareTest measure/identity/safety steps.
public interface IDmmInstrument
{
    string QueryIdn();
    void ConfigureDcVolts();
    double ReadVoltage();
    void OutputOff();
    void Reset();
}
