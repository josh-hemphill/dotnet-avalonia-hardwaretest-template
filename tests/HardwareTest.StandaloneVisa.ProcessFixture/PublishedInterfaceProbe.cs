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
        broker.FailTimeout = true;
        try { OpenTapScpiIo.Provider!.Open("MOCK::DMM", TimeSpan.FromMilliseconds(900)); throw new InvalidOperationException("Setup should fail."); }
        catch (IOException error) when (error.Message == "timeout-setup") { }
        if (broker.Session is not { Closed: true }) throw new InvalidOperationException("Acquired broker lease leaked.");
        Console.WriteLine("managed-broker-bound-and-cleaned");
        return 0;
    }
}

