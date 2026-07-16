using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// Mock DMM instrument for bring-up without a VISA runtime.
[Display("Mock DMM", Groups: ["HardwareTest"], Description: "Simulated DC voltmeter.")]
public sealed class MockDmmInstrument : Instrument
{
    private readonly object _sync = new();
    private bool _configured;
    private double _nextValue = 1.25;

    [Display("Resource Name", Order: 1)]
    public string ResourceName { get; set; } = "MOCK::INSTR0";

    public override void Open()
    {
        Log.Info("Opened mock DMM {0}", ResourceName);
        base.Open();
    }

    public override void Close()
    {
        Log.Info("Closed mock DMM {0}", ResourceName);
        base.Close();
    }

    public void Reset()
    {
        lock (_sync)
        {
            _configured = false;
            _nextValue = 1.25;
        }
    }

    public void ConfigureDcVolts()
    {
        lock (_sync)
        {
            _configured = true;
        }
    }

    public string QueryIdn() => $"MockDMM,{ResourceName},1.0";

    public double ReadVoltage()
    {
        lock (_sync)
        {
            if (!_configured)
            {
                ConfigureDcVolts();
            }

            // Mild variation so plots are non-flat.
            _nextValue += (_nextValue * 0.001) % 0.05;
            return _nextValue;
        }
    }

    public void OutputOff()
    {
        lock (_sync)
        {
            _configured = false;
        }
    }
}
