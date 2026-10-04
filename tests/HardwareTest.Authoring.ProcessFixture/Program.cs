using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using HardwareTest.Authoring;

// A real process harness delegates all work to the production child protocol.
// Release files let tests stop at deterministic boundaries without production delay flags.
if (args.Length == 2 && args[0] == AuthoringOperationHost.Switch)
    return AuthoringOperationHost.Run(args[1]);
if (args.Length == 2 && args[0] == "--operation-owner")
{
    var fixture = Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet"
        ? Assembly.GetExecutingAssembly().Location : Environment.ProcessPath!;
    using var coordinator = new AuthoringOperationCoordinator(AuthoringChildProcessRunner.ForExecutable(fixture));
    await coordinator.RunAsync(AuthoringOperationKind.Bootstrap, args[1]);
    return 0;
}
if (args.Length == 1 && args[0] == "--descendant")
{
    await Task.Delay(Timeout.Infinite);
    return 0;
}
var requestPath = args[^1];
using var request = JsonDocument.Parse(File.ReadAllBytes(requestPath));
var root = request.RootElement.GetProperty("workspaceRoot").GetString()!;
var owned = request.RootElement.GetProperty("ownedRoot").GetString()!;
File.WriteAllText(Path.Combine(root, "fixture-child.json"), Environment.ProcessId + "\n" + owned);
Console.WriteLine("child " + Environment.ProcessId);
if (File.Exists(Path.Combine(root, "fixture-spawn")))
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--descendant");
    using var descendant = Process.Start(start)!;
    File.WriteAllText(Path.Combine(root, "fixture-descendant"), descendant.Id.ToString());
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
    File.WriteAllText(Path.Combine(root, "fixture-prepared"), "ready");
    while (!File.Exists(Path.Combine(root, "fixture-release")) && !File.Exists(Path.Combine(owned, "fixture-release"))) await Task.Delay(20);
}
return exitCode;
