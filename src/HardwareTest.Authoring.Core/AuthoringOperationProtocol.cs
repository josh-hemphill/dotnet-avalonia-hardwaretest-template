using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public enum AuthoringOperationKind { Bootstrap, Validate, Pack }
public sealed record AuthoringOperationProgress(string Stage, string? Message = null);
public sealed record AuthoringOperationLog(string Stream, string Text);
public sealed record AuthoringOperationResult(string? Home, PlanContractBatchReport? Validation, AuthoringBuildResult? Build);
internal sealed record AuthoringChildRequest(int Version, string Id, AuthoringOperationKind Kind, string WorkspaceRoot,
    string OwnedRoot, string? Home, bool Offline, string? OfflinePackageSha256 = null);
internal sealed record AuthoringSnapshot(string Root, string Home, string TuiHome, string DotNetExecutable, bool Offline,
    IReadOnlyList<BuildInputTree> Trees, IReadOnlyList<AuthoringBuildEnvironmentIdentity> Environment)
{
    public static AuthoringSnapshot From(AuthoringBuildRequest request) => new(request.WorkspaceRoot,
        request.Options.Home!.Root, request.Options.TuiHome!.Root, request.Options.DotNetExecutable!, request.Options.Offline,
        request.Trees, request.Environment);
    public AuthoringBuildRequest ToRequest()
    {
        // The protocol carries environment hashes only; inherited credentials never enter result files.
        var request = new AuthoringBuildRequest(Root, new PackOptions
        {
            Home = new(Home),
            TuiHome = new(TuiHome),
            DotNetExecutable = DotNetExecutable,
            Offline = Offline
        }, Trees, AuthoringBuildService.CaptureEnvironment());
        if (!request.Environment.SequenceEqual(Environment))
            throw new AuthoringWorkspaceException("BUILD_ENVIRONMENT_CHANGED: Child and parent inherited environments differ.");
        return request;
    }
}
internal sealed record AuthoringChildResult(int Version, string Id, AuthoringOperationKind Kind,
    string OwnedRoot, AuthoringOperationResult? Result, AuthoringSnapshot? Snapshot, BuildInputTree? SelectedHome, BuildInputTree? ManifestIdentity, string? ReceiptHash, IReadOnlyList<AuthoringBuildEnvironmentIdentity> Environment, string? Error);

[JsonSerializable(typeof(AuthoringChildRequest))]
[JsonSerializable(typeof(AuthoringChildResult))]
[JsonSerializable(typeof(AuthoringOperationProgress))]
[JsonSerializable(typeof(AuthoringChildExit))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal partial class AuthoringOperationJsonContext : JsonSerializerContext;

/// Private headless authoring protocol. Results use an owned file; stdout and stderr are diagnostics only.
public static class AuthoringOperationChild
{
    public const string Switch = "--authoring-operation";
    public static int Run(string requestPath)
    {
        var request = JsonSerializer.Deserialize(File.ReadAllBytes(requestPath), AuthoringOperationJsonContext.Default.AuthoringChildRequest)
            ?? throw new InvalidDataException("Empty operation request.");
        if (request.Version != 1 || !Guid.TryParseExact(request.Id, "N", out _)
            || Path.GetFullPath(requestPath) != Path.Combine(Path.GetFullPath(request.OwnedRoot), "request.json"))
            throw new InvalidDataException("Unsupported operation request or ownership.");
        AuthoringChildResult response;
        BuildInputTree? selectedHome = null;
        BuildInputTree? manifestIdentity = null;
        try
        {
            manifestIdentity = AuthoringBuildService.CaptureTree(request.WorkspaceRoot, "workspace", false,
                path => Path.GetFileName(path) == AuthoringWorkspaceLoader.ManifestFileName);
            Stage("Prepare authoring environment");
            var workspace = AuthoringWorkspaceLoader.Load(request.WorkspaceRoot);
            var selectedRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(request.Home)
                ? Path.Combine(workspace.Root, OpenTapHomeBootstrapper.DefaultHomeRelativePath) : request.Home);
            var originalHome = AuthoringBuildService.CaptureTree(selectedRoot, "operation-home", true);
            selectedHome = originalHome with { Materialize = false, Files = originalHome.Files.Select(file => file with { Bytes = [] }).ToArray() };
            var isolatedHome = Path.Combine(request.OwnedRoot, "home");
            Directory.CreateDirectory(isolatedHome);
            foreach (var file in originalHome.Files)
            {
                var destination = Path.Combine(isolatedHome, file.RelativePath);
                AuthoringBuildService.EnsureContained(isolatedHome, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                AuthoringBuildService.Materialize(file, destination);
            }
            var importPath = Path.Combine(request.OwnedRoot, "import.TapPackage");
            if (request.OfflinePackageSha256 is not null && (request.Kind != AuthoringOperationKind.Bootstrap
                || AuthoringBuildService.Hash(File.ReadAllBytes(importPath)) != request.OfflinePackageSha256))
                throw new InvalidDataException("Offline import identity differs from captured request.");
            var home = new OpenTapHomeBootstrapper().BootstrapOwned(workspace, new BootstrapOptions
            {
                HomeDirectory = isolatedHome,
                Offline = request.Offline,
                OfflinePackagePath = request.OfflinePackageSha256 is null ? null : importPath
            });
            AuthoringOperationResult result;
            AuthoringSnapshot? snapshot = null;
            string? receiptHash = null;
            if (request.Kind == AuthoringOperationKind.Pack)
            {
                Stage("Capture saved bytes");
                var captured = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home, Offline = request.Offline, Progress = Stage });
                captured = new AuthoringBuildRequest(captured.WorkspaceRoot, captured.Options,
                    captured.Trees.Append(selectedHome).ToArray(), captured.EnvironmentValues);
                // Parent owns this tree and is responsible for disposal after the child exits.
                Stage("Compile, validate, check compatibility and package");
                var prepared = AuthoringBuildService.PrepareOwned(captured, Path.Combine(request.OwnedRoot, "prepared"));
                snapshot = AuthoringSnapshot.From(captured);
                receiptHash = prepared.ReceiptSha256;
                result = new(selectedRoot, null, prepared.Result);
            }
            else if (request.Kind == AuthoringOperationKind.Validate)
            {
                Stage("Validate saved plans");
                AuthoringSourceExportGuard.EnsureCurrent(workspace);
                var report = PlanContractValidator.Validate(workspace.TapPlanPaths,
                    new PlanContractOptions
                    {
                        Strict = true,
                        EnablePhysicalExecution = false,
                        Settings = new AppSettings { UseMockVisa = true, OpenTapPluginDirectories = [home.Root] },
                        TrustConfiguredPluginDirectories = true
                    });
                result = new(selectedRoot, report, null);
            }
            else if (request.Kind == AuthoringOperationKind.Bootstrap) result = new(selectedRoot, null, null);
            else throw new InvalidDataException("Unknown operation kind.");
            response = new(1, request.Id, request.Kind, request.OwnedRoot, result, snapshot, selectedHome, manifestIdentity, receiptHash, AuthoringBuildService.EnvironmentIdentity(AuthoringBuildService.CaptureEnvironment()), null);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            response = new(1, request.Id, request.Kind, request.OwnedRoot, null, null, null, null, null, [], error.Message);
        }
        File.WriteAllBytes(Path.Combine(request.OwnedRoot, "result.json"),
            JsonSerializer.SerializeToUtf8Bytes(response, AuthoringOperationJsonContext.Default.AuthoringChildResult));
        return response.Error is null ? 0 : 1;

        void Stage(string stage)
        {
            var path = Path.Combine(request.OwnedRoot, "progress.json");
            PublishProgress(path, JsonSerializer.SerializeToUtf8Bytes(new AuthoringOperationProgress(stage), AuthoringOperationJsonContext.Default.AuthoringOperationProgress));
        }
    }

    internal static void PublishProgress(string path, byte[] record)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, record);
        // The protocol has one publisher. ReplaceFile supports replacing an open
        // destination whose readers share deletion; MoveFileEx overwrite does not.
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
}
