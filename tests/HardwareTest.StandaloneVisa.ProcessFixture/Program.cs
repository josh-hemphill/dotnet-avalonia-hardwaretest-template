using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;

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

if (args is [var invalidMode, var invalidHome] && invalidMode is "--invalid-selected-metadata" or "--invalid-multiple-roots")
{
    if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name?.StartsWith("InstrumentComponents", StringComparison.Ordinal) == true))
        throw new InvalidOperationException("Metadata rejection fixture must begin in a cold process.");
    string[] directories = invalidMode == "--invalid-multiple-roots"
        ? ParseRoots(invalidHome)
        : [invalidHome];
    // Default logging uses the engine installation directory, not cwd; the test
    // runner isolates both this runtime and cwd from the selected roots.
    OpenTap.SessionLogs.Initialize(Path.Combine(Environment.CurrentDirectory, "rejection.log"));
    var pluginDirectories = OpenTap.PluginManager.DirectoriesToSearch.ToArray();
    var broker = new FixtureBroker();
    var catalog = new OpenTapHostCatalog(new AppSettings { OpenTapPluginDirectories = [.. directories] },
        Serilog.Log.Logger, broker, trustConfiguredPluginDirectories: true);
    try
    {
        catalog.EnsurePlugins();
    }
    catch (Exception error) when (error is IOException or InvalidOperationException or System.Xml.XmlException)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name?.StartsWith("InstrumentComponents", StringComparison.Ordinal) == true)
            || broker.Session is not null)
            throw new InvalidOperationException("Invalid selected metadata loaded an execution library or acquired its provider.", error);
        if (!pluginDirectories.SequenceEqual(OpenTap.PluginManager.DirectoriesToSearch))
            throw new InvalidOperationException("Rejected execution roots mutated plugin search directories.", error);
        Console.WriteLine("selected-metadata-refused-before-library-load: " + error.Message);
        return 0;
    }
    throw new InvalidOperationException("Invalid selected metadata was accepted or replaced by fallback.");
}

if (args is ["--selected-update", var home])
{
    var original = LoadOwnedLibrary(home);
    var originalMvid = original.ManifestModule.ModuleVersionId;
    File.Replace(Path.Combine(home, "replacement.dll"), Path.Combine(home, "InstrumentComponents.OpenTap.dll"), null);
    // Keep metadata honest: this case tests owned-origin replacement, not a stale declared hash.
    var metadataPath = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
    var metadata = System.Xml.Linq.XDocument.Load(metadataPath);
    metadata.Descendants().Single(element => element.Name.LocalName == "File" && (string?)element.Attribute("Path") == "InstrumentComponents.OpenTap.dll")
        .Elements().Single(element => element.Name.LocalName == "Hash").Value = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(Path.Combine(home, "InstrumentComponents.OpenTap.dll"))));
    metadata.Save(metadataPath);
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
    var original = LoadOwnedLibrary(updatedHome);
    var originalMvid = original.ManifestModule.ModuleVersionId;
    if (updateMode == "--loaded-update-after-reuse")
        new OpenTapHostCatalog(new AppSettings(), Serilog.Log.Logger, new FixtureBroker()).EnsurePlugins();
    File.Replace(Path.Combine(updatedHome, "replacement.dll"), Path.Combine(updatedHome, "InstrumentComponents.OpenTap.dll"), null);
    var catalog = new OpenTapHostCatalog(new AppSettings(), Serilog.Log.Logger, new FixtureBroker());
    try { catalog.EnsurePlugins(); throw new InvalidOperationException("Changed unselected loaded origin was accepted."); }
    catch (InvalidOperationException error) when (error.Message.Contains("no longer matches its source payload", StringComparison.Ordinal)) { }
    if (original.ManifestModule.ModuleVersionId != originalMvid) throw new InvalidOperationException("The original loaded code was replaced.");
    Console.WriteLine("unselected-loaded-origin-replacement-refused");
    return 0;
}

if (args is ["--external-same-mvid-update", var externalHome])
{
    var path = Path.Combine(externalHome, "InstrumentComponents.OpenTap.dll");
    var original = System.Reflection.Assembly.LoadFrom(path);
    var mvid = original.ManifestModule.ModuleVersionId;
    File.WriteAllBytes(Path.Combine(externalHome, "same-mvid.dll"), [.. File.ReadAllBytes(path), 1]);
    File.Replace(Path.Combine(externalHome, "same-mvid.dll"), path, null);
    using (var pe = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(path)))
    {
        var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        if (reader.GetGuid(reader.GetModuleDefinition().Mvid) != mvid) throw new InvalidOperationException("Fixture changed MVID.");
    }
    var catalog = new OpenTapHostCatalog(new AppSettings(), Serilog.Log.Logger, new FixtureBroker());
    try { catalog.EnsurePlugins(); throw new InvalidOperationException("Unverified external assembly was accepted."); }
    catch (InvalidOperationException error) when (error.Message.Contains("no verified load provenance", StringComparison.Ordinal)) { }
    Console.WriteLine("unverified-preloaded-library-refused");
    return 0;
}

if (args is ["--custom-mismatch", var mismatchedHome])
{
    _ = LoadOwnedLibrary(Environment.CurrentDirectory);
    new OpenTapHostCatalog(new AppSettings { OpenTapPluginDirectories = [mismatchedHome] }, Serilog.Log.Logger,
        new FixtureBroker(), trustConfiguredPluginDirectories: true).EnsurePlugins();
    throw new InvalidOperationException("A competing selected custom payload was accepted.");
}

if (args is ["--managed-custom-reuse", var customHome])
{
    var broker = new FixtureBroker();
    var settings = new AppSettings { OpenTapPluginDirectories = [customHome] };
    new OpenTapHostCatalog(settings, Serilog.Log.Logger, broker, trustConfiguredPluginDirectories: true).EnsurePlugins();
    var original = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "InstrumentComponents.OpenTap");
    new OpenTapHostCatalog(settings, Serilog.Log.Logger, broker, trustConfiguredPluginDirectories: true).EnsurePlugins();
    if (!ReferenceEquals(original, AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "InstrumentComponents.OpenTap")))
        throw new InvalidOperationException("The owned custom library was not reused.");
    Console.WriteLine("current-custom-owned-library-reused");
    return RunPublishedProbe(broker);
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
    var original = LoadOwnedLibrary(loadedHome);
    var originalLocation = original.Location;
    var broker = new FixtureBroker();
    var catalog = new OpenTapHostCatalog(new AppSettings(), Serilog.Log.Logger, broker);
    catalog.EnsurePlugins();
    var libraries = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name == "InstrumentComponents.OpenTap").ToArray();
    if (libraries.Length != 1 || !ReferenceEquals(original, libraries[0]) || libraries[0].Location != originalLocation)
        throw new InvalidOperationException("The already-loaded library was not reused.");
    Console.WriteLine("already-loaded-library-reused");
    return RunPublishedProbe(broker);
}

if (args is [var managedMode, var selected] && managedMode is "--managed" or "--managed-multiple-roots")
{
    if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name?.StartsWith("InstrumentComponents", StringComparison.Ordinal) == true))
        throw new InvalidOperationException("Managed boundary must begin without a loaded library.");
    var broker = new FixtureBroker();
    string[] directories = managedMode == "--managed-multiple-roots"
        ? ParseRoots(selected)
        : selected == "fallback" ? [] : [selected];
    var settings = new AppSettings { OpenTapPluginDirectories = [.. directories] };
    var catalog = new OpenTapHostCatalog(settings, Serilog.Log.Logger, broker, trustConfiguredPluginDirectories: true);
    catalog.EnsurePlugins();
    var loaded = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "InstrumentComponents.OpenTap");
    var loadedOrigin = OwnedLibraryDirectory(loaded);
    if (selected == "fallback" && !loadedOrigin.Contains("ht-library-execution-", StringComparison.Ordinal)) throw new InvalidOperationException("Execution library was not privately staged.");
    using (var archive = PublishedInstrumentComponents.OpenArchive())
    using (var zip = new System.IO.Compression.ZipArchive(archive))
    using (var expected = zip.GetEntry("InstrumentComponents.OpenTap.dll")!.Open())
    using (var actual = File.OpenRead(Path.Combine(loadedOrigin, "InstrumentComponents.OpenTap.dll")))
        if (!System.Security.Cryptography.SHA256.HashData(expected).SequenceEqual(System.Security.Cryptography.SHA256.HashData(actual)))
            throw new InvalidOperationException("Execution did not load the genuine published TAP payload.");
    return RunPublishedProbe(broker);
}
return 1;

static string[] ParseRoots(string json)
{
    using var document = System.Text.Json.JsonDocument.Parse(json);
    return document.RootElement.EnumerateArray().Select(element =>
        element.GetString() ?? throw new InvalidOperationException("Execution root must be a string.")).ToArray();
}

static int RunPublishedProbe(FixtureBroker broker)
{
    // Resolve the typed probe only after the owned loader has established both bases.
    // Main must not make a compiled call to a method containing library type tokens.
    var type = System.Reflection.Assembly.GetExecutingAssembly().GetType("PublishedInterfaceProbe")!;
    var method = type.GetMethod("Run", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    return (int)method.Invoke(null, [broker])!;
}

static System.Reflection.Assembly LoadOwnedLibrary(string home)
{
    var loader = typeof(OpenTapHostCatalog).Assembly.GetType("HardwareTest.OpenTap.Host.OwnedInstrumentLibrary")!;
    var load = loader.GetMethod("Load", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    _ = load.Invoke(null, [Path.Combine(home, "InstrumentComponents.dll")]);
    return (System.Reflection.Assembly)load.Invoke(null, [Path.Combine(home, "InstrumentComponents.OpenTap.dll")])!;
}

static string OwnedLibraryDirectory(System.Reflection.Assembly assembly)
{
    var loader = typeof(OpenTapHostCatalog).Assembly.GetType("HardwareTest.OpenTap.Host.OwnedInstrumentLibrary")!;
    return (string)loader.GetMethod("Directory", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [assembly])!;
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
