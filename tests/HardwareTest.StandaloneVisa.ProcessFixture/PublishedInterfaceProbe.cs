using InstrumentComponents.OpenTap;

public static class PublishedInterfaceProbe
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static int Run(FixtureBroker broker)
    {
        using (var io = OpenTapScpiIo.Provider!.Open("MOCK::DMM", TimeSpan.FromMilliseconds(900)))
        {
            io.Write("CONF");
            if (io.Query("*IDN?") != "fixture-id") throw new InvalidOperationException("Broker query was not dispatched.");
        }
        if (broker.Session is not { Closed: true, Timeout: 900, Writes: 1, Queries: 1 }) throw new InvalidOperationException("Broker session contract failed.");
        // Exercise the genuine instrument lifecycle, including the published embedded
        // model registry. A fake broker still owns all I/O; no vendor manager opens.
        var instrument = new DmmInstrument { VisaAddress = "MOCK::DMM", IoTimeoutMilliseconds = 900 };
        instrument.Open();
        if (instrument.QueryIdn().FormatResponse() != "fixture-id,,,") throw new InvalidOperationException("Published instrument identity was not dispatched.");
        instrument.Reset();
        instrument.Close();
        if (broker.Session is not { Closed: true, Timeout: 900 }) throw new InvalidOperationException("Published instrument lease was not closed.");
        Console.WriteLine("published-instrument-opened-with-embedded-registry-and-closed");
        broker.FailTimeout = true;
        try { OpenTapScpiIo.Provider!.Open("MOCK::DMM", TimeSpan.FromMilliseconds(900)); throw new InvalidOperationException("Setup should fail."); }
        catch (IOException error) when (error.Message == "timeout-setup") { }
        if (broker.Session is not { Closed: true }) throw new InvalidOperationException("Acquired broker lease leaked.");
        Console.WriteLine("managed-broker-bound-and-cleaned");
        return 0;
    }
}
