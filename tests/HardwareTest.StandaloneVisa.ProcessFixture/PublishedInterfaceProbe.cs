using InstrumentComponents.Drivers.Catalog;
using InstrumentComponents.OpenTap;
using InstrumentComponents.Runtime;

public static class PublishedInterfaceProbe
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static int Run(FixtureBroker broker)
    {
        using (var handle = OpenTapDevices.Provider!.Open("MOCK::DMM", TimeSpan.FromMilliseconds(900), new DriverSelection("keysight.34461a")))
        {
            handle.Invoke(new ResetRequest()).IntoUnit();
            if (handle.Invoke(new QueryIdentityRequest()).IntoIdentity().Manufacturer != "fixture-id") throw new InvalidOperationException("Broker query was not dispatched.");
        }
        if (broker.Session is not { Closed: true, Timeout: 900, Writes: 1, Queries: 1 }) throw new InvalidOperationException("Broker session contract failed.");
        var instrument = new DmmInstrument { VisaAddress = "MOCK::DMM", DriverId = "keysight.34461a", IoTimeoutMilliseconds = 900 };
        instrument.Open();
        if (instrument.QueryIdentity().Manufacturer != "fixture-id") throw new InvalidOperationException("Published instrument identity was not dispatched.");
        if (instrument.Dmm.MeasureVoltageDc(null).Value != 1.25) throw new InvalidOperationException("Published instrument measurement was not dispatched.");
        instrument.Reset();
        instrument.Close();
        if (broker.Session is not { Closed: true, Timeout: 900 }) throw new InvalidOperationException("Published instrument lease was not closed.");
        Console.WriteLine("published-instrument-broker-measured-voltage-dc-1.25");
        Console.WriteLine("published-instrument-opened-with-explicit-driver-and-closed");
        broker.FailTimeout = true;
        try { OpenTapDevices.Provider!.Open("MOCK::DMM", TimeSpan.FromMilliseconds(900), new DriverSelection("keysight.34461a")); throw new InvalidOperationException("Setup should fail."); }
        catch (IOException error) when (error.Message == "timeout-setup") { }
        if (broker.Session is not { Closed: true }) throw new InvalidOperationException("Acquired broker lease leaked.");
        Console.WriteLine("managed-broker-bound-and-cleaned");
        return 0;
    }
}
