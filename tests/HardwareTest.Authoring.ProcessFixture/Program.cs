using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using HardwareTest.Authoring;

// A real process harness delegates all work to the production child protocol.
// Release files let tests stop at deterministic boundaries without production delay flags.
if (args.Length == 2 && args[0] == AuthoringOperationHost.Switch)
    return AuthoringOperationHost.Run(args[1]);
if (args.Length == 2 && args[0] == "--create-held")
{
    var document = AuthoringDocumentDto.FromDraft(new AuthoringPlanInitializer().Construct(
        new PlanInitializationRequest("rail") { WorkspaceRoot = args[1] }).Draft);
    var validations = 0;
    new AuthoringDocumentStore(args[1]).CreateNew(document, validateWorkspace: () =>
    {
        if (++validations != 2) return;
        Console.WriteLine("validated"); Console.Out.Flush();
        if (Console.ReadLine() != "release") throw new IOException("Creation owner disconnected.");
    });
    return 0;
}
if (args.Length == 2 && args[0] == "--owned-library-lifecycle")
{
    if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name?.StartsWith("InstrumentComponents", StringComparison.Ordinal) == true))
        throw new InvalidOperationException("Owned lifecycle fixture must start cold.");
    var home = new OpenTapHome(args[1]);
    var adapters = AuthoringInstrumentCatalog.Discover(home);
    if (adapters.Count != 8) throw new InvalidOperationException("Cold catalog discovery failed.");
    foreach (var adapter in adapters)
    {
        var binding = new InstrumentRef("Device", adapter.TypeId, "TCPIP0::192.0.2.8::inst0::INSTR");
        var draft = new AuthoringPlanInitializer().Construct(new("lifecycle")
        { Home = home, Instruments = [binding], IdentityInstrumentSlot = "Device", IncludeSafeShutdown = true }).Draft;
        var path = Path.Combine(home.Root, "lifecycle.TapPlan");
        var compiler = new PlanCompiler(selectedHome: home);
        compiler.Save(draft, path);
        if (compiler.Load(path).Instruments.Single().TypeId != adapter.TypeId) throw new InvalidOperationException("Owned lifecycle serialization lost the device type.");
        var xml = File.ReadAllText(path);
        if (!xml.Contains("InstrumentComponents.OpenTap.IdentityQueryStep", StringComparison.Ordinal)
            || !xml.Contains("InstrumentComponents.OpenTap.SafeShutdownStep", StringComparison.Ordinal))
            throw new InvalidOperationException("Owned lifecycle serialization lost the generic steps.");
    }
    var original = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "InstrumentComponents.OpenTap");
    new HardwareTest.OpenTap.Host.OpenTapHostCatalog(new HardwareTest.Core.Settings.AppSettings(), Serilog.Log.Logger, new NeverOpenLifecycleBroker()).EnsurePlugins();
    if (!ReferenceEquals(original, AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "InstrumentComponents.OpenTap")))
        throw new InvalidOperationException("Execution did not reuse the owned catalog library.");
    Console.WriteLine("cold-owned-catalog-lifecycle-and-execution-reused");
    return 0;
}

if (args.Length == 3 && args[0] == "--cold-library-import")
{
    bool LibraryResident() => AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name?.StartsWith("InstrumentComponents", StringComparison.Ordinal) == true);
    if (LibraryResident()) throw new InvalidOperationException("Cold fixture started with a resident library.");
    var compiler = new PlanCompiler(selectedHome: new(Path.Combine(Path.GetDirectoryName(args[2])!, "missing-library-home")));
    var original = File.ReadAllBytes(args[1]);
    var draft = compiler.Load(args[1]);
    new AuthoringDocumentStore(Path.GetDirectoryName(args[2])!).SaveAtPath(args[2], AuthoringDocumentDto.FromDraft(draft));
    var source = File.ReadAllBytes(args[2]);
    try { compiler.Save(draft, args[1]); throw new InvalidOperationException("Unavailable resources were compiled."); }
    catch (AuthoringWorkspaceException error) when (error.Message.Contains("INSTRUMENT_UNAVAILABLE", StringComparison.Ordinal)) { }
    if (!original.SequenceEqual(File.ReadAllBytes(args[1])) || !source.SequenceEqual(File.ReadAllBytes(args[2])))
        throw new InvalidOperationException("Refused compilation changed original/source bytes.");
    if (LibraryResident()) throw new InvalidOperationException("Cold import unexpectedly loaded the library.");
    Console.WriteLine("cold-library-source-preserved");
    return 0;
}
if (args.Length == 2 && args[0] == "--external-edit")
{
    var compiler = new PlanCompiler();
    var draft = compiler.Load(args[1]);
    var changed = draft with { Setup = [new OperatorPromptSetup("External TUI return sentinel", "Externally changed compiled plan")] };
    compiler.Save(changed, args[1]);
    return 0;
}
if (args.Length == 2 && args[0] == "--partial-marker")
{
    var path = Path.Combine(args[1], "partial-marker");
    Publish(path, "complete\nowned-root");
    return 0;
}
// Windows console-launch probe captures literal paths and holds the actual owned child.
if (args.Length == 2 && args[0] == "tui")
{
    using var bytes = new MemoryStream();
    using (var json = new Utf8JsonWriter(bytes))
    {
        json.WriteStartObject();
        json.WriteString("assembly", Assembly.GetExecutingAssembly().Location);
        json.WriteString("workingDirectory", Environment.CurrentDirectory);
        json.WriteNumber("pid", Environment.ProcessId);
        json.WriteStartArray("arguments");
        foreach (var argument in args) json.WriteStringValue(argument);
        json.WriteEndArray(); json.WriteEndObject();
    }
    Publish(args[1] + ".argv.json", System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
    while (!File.Exists(args[1] + ".release")) await Task.Delay(20);
    return 0;
}
// Exit the session anchor first, then let tests independently release its root and leaf.
if (args.Length == 2 && args[0].StartsWith("--scope-", StringComparison.Ordinal))
{
    var directory = args[1];
    var role = args[0][8..];
    if (role == "anchor" && ScopeFixture.setsid() < 0) throw new InvalidOperationException("setsid failed");
    if (role is "anchor" or "root")
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(role == "anchor" ? "--scope-root" : "--scope-leaf");
        start.ArgumentList.Add(directory);
        using var next = Process.Start(start)!;
    }
    Publish(Path.Combine(directory, role + ".pid"), Environment.ProcessId.ToString());
    if (role == "anchor")
        while (!File.Exists(Path.Combine(directory, "anchor.release"))) await Task.Delay(20);
    else
        while (!File.Exists(Path.Combine(directory, role + ".release"))) await Task.Delay(20);
    return 0;
}
if (args.Length == 2 && args[0] == "--operation-owner")
{
    var fixture = Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet"
        ? Assembly.GetExecutingAssembly().Location : Environment.ProcessPath!;
    using var coordinator = new AuthoringOperationCoordinator(AuthoringChildProcessRunner.ForExecutable(fixture));
    await coordinator.RunAsync(AuthoringOperationKind.Bootstrap, args[1]);
    return 0;
}
if (args.Length is 1 or 2 && args[0] == "--descendant")
{
    using var heldFile = args.Length == 2 ? File.Open(args[1], FileMode.Create, FileAccess.ReadWrite, FileShare.None) : null;
    if (heldFile is not null) Publish(args[1] + ".ready", "");
    await Task.Delay(Timeout.Infinite);
    return 0;
}
var requestPath = args[^1];
using var request = JsonDocument.Parse(File.ReadAllBytes(requestPath));
var root = request.RootElement.GetProperty("workspaceRoot").GetString()!;
var owned = request.RootElement.GetProperty("ownedRoot").GetString()!;
Publish(Path.Combine(root, "fixture-child.json"), Environment.ProcessId + "\n" + owned);
Console.WriteLine("child " + Environment.ProcessId);
if (File.Exists(Path.Combine(root, "fixture-spawn")))
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--descendant");
    var heldPath = Path.Combine(owned, "descendant-held");
    start.ArgumentList.Add(heldPath);
    using var descendant = Process.Start(start)!;
    while (!File.Exists(heldPath + ".ready")) await Task.Delay(20);
    Publish(Path.Combine(root, "fixture-descendant"), descendant.Id.ToString());
}
if (File.Exists(Path.Combine(root, "fixture-root-exit"))) return 7;
if (File.Exists(Path.Combine(root, "fixture-flood")))
{
    for (var index = 0; index < 512; index++)
    {
        Console.Out.Write(new string('x', 1024));
        Console.Error.Write(new string('e', 1024));
    }
    Console.Out.Flush();
    Console.Error.Flush();
}
if (File.Exists(Path.Combine(root, "fixture-wait")))
    while (!File.Exists(Path.Combine(root, "fixture-release"))) await Task.Delay(20);
var exitCode = AuthoringOperationChild.Run(requestPath);
if (exitCode != 0) return exitCode;
if (File.Exists(Path.Combine(root, "fixture-corrupt-version")))
{
    var resultPath = Path.Combine(owned, "result.json");
    File.WriteAllText(resultPath, File.ReadAllText(resultPath).Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal));
}
foreach (var package in OpenTapHomeBootstrapper.ListInstalledPackages(new OpenTapHome(Path.Combine(owned, "home"))))
    Console.WriteLine("home-package:" + package.Name);
if (File.Exists(Path.Combine(root, "fixture-result-wait")))
{
    Publish(Path.Combine(root, "fixture-prepared"), "ready");
    while (!File.Exists(Path.Combine(root, "fixture-release")) && !File.Exists(Path.Combine(owned, "fixture-release"))) await Task.Delay(20);
}
return exitCode;

static void Publish(string path, string content)
{
    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        if (Path.GetFileName(path) == "partial-marker")
        {
            using (var writer = new StreamWriter(temporary))
            {
                writer.Write("complete"); writer.Flush();
                Console.WriteLine("partial"); Console.Out.Flush();
                if (Console.ReadLine() != "release") throw new IOException("Marker owner disconnected.");
                writer.Write("\nowned-root");
            }
        }
        else File.WriteAllText(temporary, content);
        File.Move(temporary, path, true);
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
}

internal static class ScopeFixture
{
    [DllImport("libc", SetLastError = true)]
    internal static extern int setsid();
}

internal sealed class NeverOpenLifecycleBroker : HardwareTest.Core.Hardware.IVisaBroker
{
    public Task<HardwareTest.Core.Hardware.IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Catalog and provider registration must not open a broker session.");
}
