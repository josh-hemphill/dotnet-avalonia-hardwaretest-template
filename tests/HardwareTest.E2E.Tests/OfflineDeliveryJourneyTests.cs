using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OfflineDependencyFixture;
using HardwareTest.OfflinePluginFixture;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Host.Worker;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.E2E.Tests;

/// Run with the standalone xUnit executable in the network-disabled sandbox.
/// Persistent artifacts let independent reviewers inspect the exact transferred program.
public sealed class OfflineDeliveryJourneyTests
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HT_OFFLINE_BENCH_JOURNEY"));

    [Fact(SkipUnless = nameof(Enabled), Skip = "Set HT_OFFLINE_BENCH_JOURNEY to a fresh evidence directory; run without network access.")]
    public async Task Authored_program_and_transitive_plugin_transfer_install_execute_and_report_without_source_caches()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("HT_OFFLINE_BENCH_JOURNEY")!);
        Assert.False(Directory.Exists(root), "Use a fresh evidence directory so prior artifacts cannot satisfy this journey.");
        Directory.CreateDirectory(root);
        var repository = Repository();
        var configuration = Path.GetRelativePath(Path.Combine(repository, "tests", "HardwareTest.E2E.Tests", "bin"), AppContext.BaseDirectory).Split(Path.DirectorySeparatorChar)[0];
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var baseline = Environment.GetEnvironmentVariable("HT_OFFLINE_OPERATOR_BASELINE")
            ?? Path.Combine(repository, "src", "HardwareTest", "bin", configuration, "net10.0", rid);
        var executableName = OperatingSystem.IsWindows() ? "HardwareTest.exe" : "HardwareTest";
        var baselineExe = Path.Combine(baseline, executableName);
        Assert.True(File.Exists(baselineExe));
        Assert.True(File.Exists(Path.Combine(baseline, "offline-bench-capability.json")));
        Assert.False(File.Exists(Path.Combine(baseline, "OfflineBench.DeliveryFixture.dll")));
        Assert.False(File.Exists(Path.Combine(baseline, "OfflineBench.ValueFixture.dll")));
        var originalRuntime = Snapshot(baseline);
        var priorData = Path.Combine(root, "original-station-data"); Directory.CreateDirectory(priorData);
        File.WriteAllText(Path.Combine(priorData, "credentials.json"), "Original station credential sentinel — must never be copied.");
        Directory.CreateDirectory(Path.Combine(priorData, "bench-references"));
        File.WriteAllText(Path.Combine(priorData, "bench-references", "golden.json"), "Original qualification sentinel.");
        var originalData = Snapshot(priorData);

        var workspaceRoot = Path.Combine(root, "authoring"); Directory.CreateDirectory(workspaceRoot);
        var manifest = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(repository, "plans", "opentap", "authoring.json")),
            AuthoringJsonContext.Default.AuthoringManifest)!;
        manifest.Dependencies.Add(new() { Package = "Offline Delivery Fixture", Version = "^1.0.0" });
        var bootstrapManifest = JsonSerializer.Deserialize(JsonSerializer.Serialize(manifest, AuthoringJsonContext.Default.AuthoringManifest),
            AuthoringJsonContext.Default.AuthoringManifest)!;
        bootstrapManifest.Dependencies.RemoveAll(dependency => dependency.Package == "Offline Delivery Fixture");
        File.WriteAllText(Path.Combine(workspaceRoot, "authoring.json"), JsonSerializer.Serialize(bootstrapManifest, AuthoringJsonContext.Default.AuthoringManifest));
        var authoringExe = Path.Combine(repository, "src", "HardwareTest.Authoring", "bin", configuration, "net10.0", rid,
            OperatingSystem.IsWindows() ? "HardwareTest.Authoring.exe" : "HardwareTest.Authoring");
        var home = new OpenTapHome(Path.Combine(workspaceRoot, ".authoring", "opentap"));
        var bootstrap = await Command(authoringExe, ["--bootstrap", workspaceRoot, "--offline", "--opentap-home", home.Root],
            Path.Combine(root, "authoring-workstation-config"));
        File.WriteAllText(Path.Combine(root, "authoring-preparation.log"), bootstrap.Output);
        Assert.True(bootstrap.Exit == 0, bootstrap.Output);
        File.WriteAllText(Path.Combine(workspaceRoot, "authoring.json"), JsonSerializer.Serialize(manifest, AuthoringJsonContext.Default.AuthoringManifest));
        var archiveFolder = Path.Combine(workspaceRoot, "manual-archives"); Directory.CreateDirectory(archiveFolder);
        Archive(archiveFolder, "Offline Delivery Fixture", typeof(OfflineProofStep).Assembly.Location,
            [("Offline Delivery Dependency", "^1.0.0"), ("OpenTAP", "^9.32.2")]);
        Archive(archiveFolder, "Offline Delivery Dependency", typeof(OfflineValue).Assembly.Location, []);

        const string planId = "offline-delivery-voltage";
        var freshData = Path.Combine(root, "new-station-data");
        var proofFile = Path.Combine(freshData, "executed-payload.txt");
        var step = new OfflineProofStep { Name = "Acquire offline delivery voltage", ProofPath = proofFile };
        var seed = new TestPlan(); seed.ChildTestSteps.Add(step);
        var rawPath = Path.Combine(root, "raw-source.TapPlan"); seed.Save(rawPath);
        var raw = XDocument.Load(rawPath).Descendants().Single(element => element.Name.LocalName == "TestStep");
        var draft = new ProgramDraft(planId, new ProgramSidecar { DisplayName = "Offline delivery voltage", SelectionIncludesCleanup = false },
            [new InstrumentRef("Offline mock DMM", typeof(MockDmmInstrument).FullName!, "MOCK::OFFLINE-DELIVERY")], [],
            [new RawStepNode(typeof(OfflineProofStep).FullName!, raw.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id }],
            new CleanupPolicy(true, ["Offline mock DMM"]));
        var planPath = Path.Combine(workspaceRoot, planId + ".TapPlan");
        new PlanCompiler([Path.GetDirectoryName(typeof(OfflineProofStep).Assembly.Location)!]).Save(draft, planPath);
        new AuthoringDocumentStore(workspaceRoot).Save(AuthoringDocumentDto.FromDraft(draft, revision: 1,
            compiledPlanHash: AuthoringDocumentStore.ComputeHash(planPath), compiledSidecarHash: AuthoringDocumentStore.ComputeHash(Path.ChangeExtension(planPath, ".program.json"))));
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var prepared = OfflineBenchBundleExporter.Export(workspace, planId, Path.Combine(root, "prepared"),
            new(OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture, archiveFolder, home.Root, DownloadMissing: false));
        var transferred = Path.Combine(root, "transferred-bundle"); Copy(prepared.Directory, transferred);
        var transferBefore = Snapshot(transferred);
        // The bench receives only the transferred bundle, not the authoring workspace/cache.
        Directory.Move(workspaceRoot, Path.Combine(root, "source-unavailable"));
        Assert.False(Directory.Exists(archiveFolder)); Assert.False(Directory.Exists(home.Root));

        var check = await Command(baselineExe, ["--check-offline-bundle", transferred], priorData);
        File.WriteAllText(Path.Combine(root, "check.log"), check.Output);
        Assert.True(check.Exit == 0, check.Output); Same(originalData, priorData);
        Assert.Contains("Loaded execution engine: " + OpenTapBuildInfo.ExecutionEngineIdentity, check.Output, StringComparison.Ordinal);
        var installation = Path.Combine(root, "new-operator");
        var install = await Command(baselineExe, ["--install-offline-bundle", transferred, "--destination", installation, "--data-directory", freshData], priorData);
        File.WriteAllText(Path.Combine(root, "install.log"), install.Output);
        Assert.True(install.Exit == 0, install.Output);
        var newExe = Path.Combine(installation, executableName);
        var config = await Command(newExe, ["--print-config"], priorData);
        File.WriteAllText(Path.Combine(root, "bound-startup.log"), config.Output);
        Assert.True(config.Exit == 0, config.Output); Assert.Contains(freshData, config.Output, StringComparison.Ordinal);
        var refused = await Command(newExe, ["--data-directory", priorData, "--print-config"], priorData);
        Assert.NotEqual(0, refused.Exit); Same(originalData, priorData);
        var installedPlan = Path.Combine(installation, "Programs", planId, planId + ".TapPlan");
        var catalog = Assert.Single(ProgramCatalog.Enumerate([Path.Combine(installation, "Programs")]), program => program.Id == planId);
        Assert.Equal(installedPlan, catalog.Path);
        Assert.Equal(prepared.Manifest.Files.Single(file => file.Path == $"Programs/{planId}/{planId}.TapPlan").Sha256,
            BenchProgramHandoff.Hash(File.ReadAllBytes(installedPlan)));
        var settings = new AppSettings { UseMockVisa = true, CrashEnabled = false, EmbedPlotsInReport = false, DataDirectory = freshData };
        var workerExe = Path.Combine(installation, OperatingSystem.IsWindows() ? "HardwareTest.OpenTap.Worker.exe" : "HardwareTest.OpenTap.Worker");
        using var worker = new OpenTapWorkerClient(settings, executablePath: workerExe);
        await worker.LoadPlanAsync(installedPlan);
        var outcome = await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(90));
        Assert.True(outcome.Result == RunResult.Passed, outcome.ErrorMessage);
        var proof = File.ReadAllLines(proofFile);
        Assert.Equal(Path.Combine(installation, "OfflineBench.DeliveryFixture.dll"), proof[0]);
        Assert.Equal(Path.Combine(installation, "OfflineBench.ValueFixture.dll"), proof[1]);
        Assert.Equal("1.25", proof[2]);
        Assert.Equal(Path.Combine(installation, "OpenTap.dll"), proof[3]);
        Assert.Equal(OpenTapBuildInfo.ExecutionEngineIdentity, proof[4]);
        var installedCheck = await Command(newExe, ["--check-offline-bundle", transferred], priorData);
        File.WriteAllText(Path.Combine(root, "installed-check.log"), installedCheck.Output);
        Assert.True(installedCheck.Exit == 0, installedCheck.Output);
        Assert.Contains("Loaded execution engine: " + proof[4], installedCheck.Output, StringComparison.Ordinal);
        var record = new TestRunRecord
        {
            RunId = outcome.RunId,
            PlanId = planId,
            PlanName = outcome.PlanName,
            DutSerial = "OFFLINE-MOCK-UNIT",
            OperatorName = "Offline delivery reviewer",
            StartedAt = outcome.StartedAt,
            CompletedAt = outcome.CompletedAt,
            Result = outcome.Result,
            ExecutionScope = RunExecutionScopes.FullSuite,
            Samples = outcome.Samples,
            Events = outcome.Events,
            Steps = outcome.Steps,
        };
        var runs = new FileRunStore(Path.Combine(freshData, "runs")); await runs.SaveAsync(record);
        using var reports = new TypstReportService(runs, settings);
        var pdf = await reports.GeneratePdfAsync(record); Assert.True(new FileInfo(pdf).Length > 1000);
        Assert.Equal(RunResult.Passed, (await runs.LoadAsync(record.RunId))!.Result);
        Same(originalRuntime, baseline); Same(originalData, priorData); Same(transferBefore, transferred);
        File.WriteAllText(Path.Combine(root, "journey-passed.txt"), $"Plan {planId}\nRun {record.RunId}\nReport {pdf}\nPlugin {proof[0]}\nDependency {proof[1]}\nValue {proof[2]}\nEngine {proof[3]}\nIdentity {proof[4]}\n");
    }

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "dirs.proj"))) return directory.FullName;
        throw new InvalidOperationException("The joined proof requires the built repository workspace.");
    }

    private static void Archive(string folder, string package, string assembly, (string Name, string Range)[] dependencies)
    {
        using var archive = ZipFile.Open(Path.Combine(folder, package + ".TapPackage"), ZipArchiveMode.Create);
        var metadata = new XElement("Package", new XAttribute("Name", package), new XAttribute("Version", "1.0.0"),
            new XAttribute("OS", "Windows,Linux"), new XAttribute("Architecture", "AnyCPU"),
            new XElement("Dependencies", dependencies.Select(value => new XElement("PackageDependency", new XAttribute("Package", value.Name), new XAttribute("Version", value.Range)))),
            new XElement("Files", new XElement("File", new XAttribute("Path", Path.GetFileName(assembly)))));
        using (var writer = new StreamWriter(archive.CreateEntry($"Packages/{package}/package.xml").Open())) writer.Write(metadata);
        archive.CreateEntryFromFile(assembly, Path.GetFileName(assembly));
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => BenchProgramHandoff.Hash(File.ReadAllBytes(path)), StringComparer.Ordinal);
    private static void Same(Dictionary<string, string> expected, string root)
    {
        var actual = Snapshot(root); Assert.Equal(expected.Count, actual.Count);
        foreach (var item in expected) Assert.Equal(item.Value, actual[item.Key]);
    }
    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
    }
    private static async Task<(int Exit, string Output)> Command(string executable, string[] arguments, string originalData)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["XDG_CONFIG_HOME"] = originalData;
        start.Environment["APPDATA"] = originalData;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        return (process.ExitCode, await output + await errors);
    }
}
