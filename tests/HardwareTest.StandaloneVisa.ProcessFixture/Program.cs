using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using InstrumentComponents.OpenTap;
using InstrumentComponents.Scpi;

if (args is ["--metadata", var gate])
{
    var catalog = new OpenTapHostCatalog(new AppSettings(), Serilog.Log.Logger,
        gate == "no-broker" ? null : new FixtureBroker(), includeVisaAdapter: gate == "no-broker");
    catalog.EnsurePlugins();
    if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name?.StartsWith("InstrumentComponents", StringComparison.Ordinal) == true))
        throw new InvalidOperationException("Metadata boundary acquired an execution library.");
    var binder = typeof(OpenTapHostCatalog).Assembly.GetType("HardwareTest.OpenTap.Host.InstrumentComponentsScpiIo")!;
    if ((bool)binder.GetMethod("TryRegisterProvider")!.Invoke(null, [new FixtureBroker()])!)
        throw new InvalidOperationException("An absent library unexpectedly acquired a provider.");
    if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name?.StartsWith("InstrumentComponents", StringComparison.Ordinal) == true))
        throw new InvalidOperationException("Absent-provider registration unexpectedly loaded the library.");
    Console.WriteLine("metadata-boundary-isolated");
    return 0;
}

if (args is ["--selected-update", var home])
{
    var original = System.Reflection.Assembly.LoadFrom(Path.Combine(home, "InstrumentComponents.OpenTap.dll"));
    var originalMvid = original.ManifestModule.ModuleVersionId;
    File.Replace(Path.Combine(home, "replacement.dll"), original.Location, null);
    var catalog = new OpenTapHostCatalog(new AppSettings { OpenTapPluginDirectories = [home] },
        Serilog.Log.Logger, new FixtureBroker(), trustConfiguredPluginDirectories: true);
    try { catalog.EnsurePlugins(); throw new InvalidOperationException("Changed selected origin was accepted."); }
    catch (InvalidOperationException error) when (error.Message.Contains("no longer matches its source payload", StringComparison.Ordinal)) { }
    if (original.ManifestModule.ModuleVersionId != originalMvid) throw new InvalidOperationException("The original loaded code was replaced.");
    Console.WriteLine("selected-origin-replacement-refused");
    return 0;
}

if (args is [var updateMode, var updatedHome] && updateMode is "--loaded-update" or "--loaded-update-after-reuse")
{
    var original = System.Reflection.Assembly.LoadFrom(Path.Combine(updatedHome, "InstrumentComponents.OpenTap.dll"));
    var originalMvid = original.ManifestModule.ModuleVersionId;
    if (updateMode == "--loaded-update-after-reuse")
        new OpenTapHostCatalog(new AppSettings(), Serilog.Log.Logger, new FixtureBroker()).EnsurePlugins();
    File.Replace(Path.Combine(updatedHome, "replacement.dll"), original.Location, null);
    var catalog = new OpenTapHostCatalog(new AppSettings(), Serilog.Log.Logger, new FixtureBroker());
    try { catalog.EnsurePlugins(); throw new InvalidOperationException("Changed unselected loaded origin was accepted."); }
    catch (InvalidOperationException error) when (error.Message.Contains("no longer matches its source payload", StringComparison.Ordinal)) { }
    if (original.ManifestModule.ModuleVersionId != originalMvid) throw new InvalidOperationException("The original loaded code was replaced.");
    Console.WriteLine("unselected-loaded-origin-replacement-refused");
    return 0;
}

if (args is ["--loaded-unsupported", var unsupportedHome])
{
    System.Reflection.Assembly.LoadFrom(Path.Combine(unsupportedHome, "replacement.dll"));
    var catalog = new OpenTapHostCatalog(new AppSettings(), Serilog.Log.Logger, new FixtureBroker());
    try { catalog.EnsurePlugins(); throw new InvalidOperationException("Unsupported loaded library was accepted."); }
    catch (InvalidOperationException error) when (error.Message.Contains("current 0.1.1 broker-managed execution contract", StringComparison.Ordinal)) { }
    Console.WriteLine("unsupported-loaded-library-refused");
    return 0;
}

if (args is ["--loaded", var loadedHome])
{
    if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name?.StartsWith("InstrumentComponents", StringComparison.Ordinal) == true))
        throw new InvalidOperationException("Loaded-library fixture must begin in a cold process.");
    var original = System.Reflection.Assembly.LoadFrom(Path.Combine(loadedHome, "InstrumentComponents.OpenTap.dll"));
    var originalLocation = original.Location;
    var broker = new FixtureBroker();
    var catalog = new OpenTapHostCatalog(new AppSettings(), Serilog.Log.Logger, broker);
    catalog.EnsurePlugins();
    var libraries = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name == "InstrumentComponents.OpenTap").ToArray();
    if (libraries.Length != 1 || !ReferenceEquals(original, libraries[0]) || libraries[0].Location != originalLocation)
        throw new InvalidOperationException("The already-loaded library was not reused.");
    Console.WriteLine("already-loaded-library-reused");
    return PublishedInterfaceProbe.Run(broker);
}

if (args is ["--managed", var selected])
{
    if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name?.StartsWith("InstrumentComponents", StringComparison.Ordinal) == true))
        throw new InvalidOperationException("Managed boundary must begin without a loaded library.");
    var broker = new FixtureBroker();
    var settings = new AppSettings { OpenTapPluginDirectories = selected == "fallback" ? [] : [selected] };
    var catalog = new OpenTapHostCatalog(settings, Serilog.Log.Logger, broker, trustConfiguredPluginDirectories: true);
    catalog.EnsurePlugins();
    var loaded = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "InstrumentComponents.OpenTap");
    if (selected == "fallback" && !loaded.Location.Contains("ht-library-execution-", StringComparison.Ordinal)) throw new InvalidOperationException("Execution library was not privately staged.");
    using (var archive = PublishedInstrumentComponents.OpenArchive())
    using (var zip = new System.IO.Compression.ZipArchive(archive))
    using (var expected = zip.GetEntry("InstrumentComponents.OpenTap.dll")!.Open())
    using (var actual = File.OpenRead(loaded.Location))
        if (!System.Security.Cryptography.SHA256.HashData(expected).SequenceEqual(System.Security.Cryptography.SHA256.HashData(actual)))
            throw new InvalidOperationException("Execution did not load the genuine published TAP payload.");
    return PublishedInterfaceProbe.Run(broker);
}
return 1;

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

internal sealed class FixtureBroker : IVisaBroker
{
    public FixtureSession? Session { get; private set; }
    public bool FailTimeout { get; set; }
    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Session = new FixtureSession(resourceName, FailTimeout);
        return Task.FromResult<IVisaSession>(Session);
    }
}

internal sealed class FixtureSession(string address, bool failTimeout) : IVisaSession
{
    public string ResourceName => address;
    public int Timeout { get; private set; }
    public int IoTimeoutMilliseconds { get => Timeout; set { if (failTimeout) throw new IOException("timeout-setup"); Timeout = value; } }
    public int Writes { get; private set; }
    public int Queries { get; private set; }
    public bool Closed { get; private set; }
    public Task WriteAsync(string command, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); Writes++; return Task.CompletedTask; }
    public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); Queries++; return Task.FromResult("fixture-id"); }
    public ValueTask DisposeAsync() { Closed = true; return ValueTask.CompletedTask; }
}
