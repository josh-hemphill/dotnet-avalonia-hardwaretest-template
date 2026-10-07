using System.Text.Json;
using System.Xml.Linq;

namespace HardwareTest.Authoring;

/// Compiles saved bytes in isolation and publishes only a fully checked build.
public static partial class AuthoringBuildService
{
    public const string ReceiptFileName = "authoring-build-receipt.json";
    public static AuthoringBuildResult Execute(AuthoringBuildRequest request, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        using var prepared = Prepare(request, cancellationToken);
        return Publish(prepared, outputDirectory, cancellationToken);
    }

    public static AuthoringPreparedBuild Prepare(AuthoringBuildRequest request, CancellationToken cancellationToken = default)
        => PrepareOwned(request, Path.Combine(Path.GetTempPath(), "authoring-build-" + Guid.NewGuid().ToString("N")), cancellationToken);

    internal static AuthoringPreparedBuild PrepareOwned(AuthoringBuildRequest request, string staging, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        Recheck(request);
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var tree in request.Trees.Where(t => t.Materialize))
                foreach (var file in tree.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = Path.Combine(staging, tree.StageRelativePath, file.RelativePath);
                    EnsureContained(staging, destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    Materialize(file, destination);
                }
            PrepareShellStage(staging, request.Trees);
            var root = Path.Combine(staging, "workspace");
            Directory.CreateDirectory(Path.Combine(root, "plans"));
            var captured = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(root, AuthoringWorkspaceLoader.ManifestFileName)), AuthoringJsonContext.Default.AuthoringManifest)!;
            captured.PlansDirectory = "plans";
            File.WriteAllText(Path.Combine(root, AuthoringWorkspaceLoader.ManifestFileName), JsonSerializer.Serialize(captured, AuthoringJsonContext.Default.AuthoringManifest));
            var workspace = AuthoringWorkspaceLoader.Load(root);
            for (var i = 0; i < workspace.Manifest.PluginProjects.Count; i++)
                workspace.Manifest.PluginProjects[i] = Path.Combine(staging, "plugins", i.ToString(),
                    Path.GetFileName(workspace.Manifest.PluginProjects[i]));
            for (var i = 0; i < workspace.Manifest.ShellAppProjects.Count; i++)
                workspace.Manifest.ShellAppProjects[i] = ShellStageProject(staging, request.Trees, request.WorkspaceRoot, workspace.Manifest.ShellAppProjects[i]);
            AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
            var store = new AuthoringDocumentStore(root);
            var workspaceSource = store.LoadWorkspace();
            if (workspaceSource.IsReadOnly || workspaceSource.Error is not null)
                throw new AuthoringWorkspaceException("BUILD_SOURCE: Workspace source is corrupt or unsupported.");
            // Verify the original catalogs before staging path rewrites.
            if (workspaceSource.Document is { } original)
            {
                ValidateSourceShape(workspaceSource.OriginalBytes, workspace: true);
                var capturedManifest = request.Trees.Single(t => t.StageRelativePath == "workspace").Files.Single().Bytes;
                var savedManifest = JsonSerializer.Deserialize(capturedManifest, AuthoringJsonContext.Default.AuthoringManifest)!;
                if (!AuthoringSourceExportGuard.WorkspaceCatalogMatches(original.Manifest, savedManifest))
                    throw new AuthoringWorkspaceException("BUILD_SOURCE: Workspace catalog conflicts with saved manifest.");
                store.SaveWorkspace(workspace.Manifest, original.Revision);
            }
            var home = new OpenTapHome(Path.Combine(staging, "home"));
            var tuiHome = new OpenTapHome(Path.Combine(staging,
                request.Trees.Any(t => t.StageRelativePath == "tui-home") ? "tui-home" : "home"));
            var compiler = new PlanCompiler(selectedHome: home);
            // Sources are validated before any compiled import; future/corrupt data never falls back.
            var sourceIds = store.ListDocumentIds();
            var allPaths = workspace.TapPlanPaths.Concat(sourceIds.Select(id => Path.Combine(root, "plans", id + ".TapPlan")))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
            workspace = workspace with { TapPlanPaths = allPaths };
            var included = allPaths.Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .Where(id => AuthoringBuildInclusion.Includes(workspace.Manifest, id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var excluded = allPaths.Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .Where(id => !included.Contains(id)).ToArray();
            foreach (var id in sourceIds.Where(id => !included.Contains(id))) File.Delete(store.GetDocumentPath(id));
            var sourceDocuments = sourceIds.Where(included.Contains).Select(id =>
            {
                var source = store.Load(id);
                if (!source.IsSuccess || source.IsReadOnly)
                    throw new AuthoringWorkspaceException($"BUILD_SOURCE: '{id}' is corrupt or unsupported. {source.Error}");
                ValidateSourceShape(source.OriginalBytes, workspace: false);
                return source.Document!;
            }).ToArray();
            var results = new List<AuthoringCompileResult>();
            var sources = new List<AuthoringBuildSource>();
            request.Options.Progress?.Invoke("Compile included saved programs");
            foreach (var id in included.Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(root, "plans", id + ".TapPlan");
                var document = sourceDocuments.SingleOrDefault(d => string.Equals(d.PlanId, id, StringComparison.OrdinalIgnoreCase));
                ProgramDraft draft;
                if (document is not null)
                {
                    sources.Add(new(id!, document.Revision, Hash(File.ReadAllBytes(store.GetDocumentPath(document.PlanId)))));
                    draft = document.ToDraft();
                    if (!AuthoringSequence.HasMeasurement(draft.Measure))
                        throw new AuthoringWorkspaceException($"BUILD_INCOMPLETE: Included source '{id}' needs a measurement before deployment.");
                    if (AuthoringFormulaDeployment.Project(draft).AuthoringState.IncompleteNumericText.Count > 0)
                        throw new AuthoringWorkspaceException($"BUILD_INCOMPLETE: Included source '{id}' contains incomplete numeric deployment input.");
                    if ((File.Exists(path) && document.CompiledPlanHash != AuthoringDocumentStore.ComputeHash(path))
                        || (File.Exists(PlanCompiler.SidecarPath(path)) && document.CompiledSidecarHash != AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path))))
                        throw new AuthoringWorkspaceException($"BUILD_SOURCE_CONFLICT: Compiled artifacts for '{id}' disagree with saved baseline. Explicitly reconcile before building.");
                    compiler.Save(draft, path);
                    document.RequiresCompilation = false;
                    document.CompiledPlanHash = AuthoringDocumentStore.ComputeHash(path);
                    document.CompiledSidecarHash = AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path));
                    store.Save(document); // Staging baseline only; original saved revision is untouched.
                }
                else
                {
                    sources.Add(new(id!, null, null));
                    draft = compiler.Load(path);
                }
                results.Add(new(id!, Hash(File.ReadAllBytes(path)), CompileMap(AuthoringFormulaDeployment.Project(draft), path), AuthoringFormulaDeployment.ExcludedNodes(draft)));
            }
            // Excluded source documents are not deployment inputs, and cannot trip the staged source guard.
            foreach (var document in sourceDocuments.Where(d => !included.Contains(d.PlanId)))
                File.Delete(store.GetDocumentPath(document.PlanId));
            var stageOutput = Path.Combine(staging, "output");
            PackPreflightReport? checks = null;
            var expectedManifest = JsonSerializer.Serialize(workspace.Manifest, AuthoringJsonContext.Default.AuthoringManifest);
            var stageInputs = PackageXmlRenderer.EnumeratePackFiles(workspace)
                .Select(file => Path.Combine(root, "plans", file)).Concat(workspace.Manifest.PluginProjects)
                .Where(File.Exists).ToDictionary(path => path, path => Hash(File.ReadAllBytes(path)), StringComparer.Ordinal);
            var stagedIdentity = CaptureStagedInputs(staging);
            void CheckStageInputs(bool afterPacking = false)
            {
                VerifyStagedInputs(staging, stagedIdentity, afterPacking);
                if (JsonSerializer.Serialize(workspace.Manifest, AuthoringJsonContext.Default.AuthoringManifest) != expectedManifest)
                    throw new AuthoringWorkspaceException("BUILD_STAGED_CHANGED: Manifest changed during required checks.");
                foreach (var input in stageInputs)
                    if (Hash(File.ReadAllBytes(input.Key)) != input.Value)
                        throw new AuthoringWorkspaceException("BUILD_STAGED_CHANGED: Package input changed during required checks.");
                foreach (var compiled in results)
                    if (Hash(File.ReadAllBytes(Path.Combine(root, "plans", compiled.PlanId + ".TapPlan"))) != compiled.PlanSha256)
                        throw new AuthoringWorkspaceException("BUILD_STAGED_CHANGED: Compiled staged plan changed after required checks.");
            }
            var options = new PackOptions
            {
                Home = home,
                TuiHome = tuiHome,
                Compat = request.Options.Compat,
                DotNetExecutable = request.Options.DotNetExecutable,
                BuildEnvironment = request.EnvironmentValues,
                CancellationToken = cancellationToken,
                Offline = request.Options.Offline,
                Progress = request.Options.Progress,
                PreflightCompleted = report =>
                {
                    checks = report with { Findings = Array.AsReadOnly(report.Findings.ToArray()) };
                    request.Options.PreflightCompleted?.Invoke(report with { Home = request.Options.Home, TuiHome = request.Options.TuiHome });
                    CheckStageInputs();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            };
            var manifest = WorkspacePacker.PackStaged(workspace, stageOutput, options);
            CheckStageInputs(afterPacking: true);
            if (manifest.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count)
                throw new AuthoringWorkspaceException("BUILD_OUTPUT_COLLISION: Multiple artifacts have the same destination.");
            var outputs = Directory.EnumerateFiles(stageOutput, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal).Select(p => new AuthoringBuildOutput(Path.GetRelativePath(stageOutput, p), Hash(File.ReadAllBytes(p)))).ToArray();
            var requiredChecks = checks!.Findings.Concat([
                new PackPreflightFinding("BUILD_COMPILE_PASS", $"Compiled or imported {results.Count} included saved programs; excluded programs were not packaged.", false),
                new PackPreflightFinding("BUILD_STRICT_VALIDATION_PASS", $"Strict contract validation passed for {checks.Contract!.Plans.Count} included plans.", false),
                new PackPreflightFinding(request.Options.Compat is null or TuiCompatChecker ? "BUILD_COMPATIBILITY_PASS" : "BUILD_COMPATIBILITY_INJECTED", "Plugin catalogs and in-process plan load/save checks passed under the recorded compatibility provider.", false),
                new PackPreflightFinding("BUILD_COMPAT_PROVIDER",
                request.Options.Compat is null or TuiCompatChecker ? "Production in-process TuiCompatChecker; external process integration is a separate fixture."
                    : $"Injected compatibility checker: {request.Options.Compat.GetType().FullName}; no external process evidence is implied.", false)]);
            var receipt = new AuthoringBuildReceipt(1, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
                included.Select(p => p!).Order(StringComparer.Ordinal).ToArray(), excluded, sources.AsReadOnly(), request.Inputs,
                manifest.Dependencies, requiredChecks.ToArray(), results.AsReadOnly(), Array.AsReadOnly(outputs), workspaceSource.Document?.Revision, request.Environment);
            File.WriteAllText(Path.Combine(stageOutput, ReceiptFileName), JsonSerializer.Serialize(receipt, AuthoringBuildJsonContext.Default.AuthoringBuildReceipt));
            cancellationToken.ThrowIfCancellationRequested();
            return new AuthoringPreparedBuild(request, staging, new(manifest, receipt));
        }
        catch { CleanupStaging(staging); throw; }
    }

    internal static void CleanupStaging(string staging)
    {
        try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Cleanup cannot turn a committed build into a reported failure. Preserve the owned tree for later removal.
            System.Diagnostics.Trace.TraceWarning($"Authoring staging cleanup failed for '{staging}': {error.Message}");
        }
    }

    public static AuthoringBuildResult Publish(AuthoringPreparedBuild prepared, string outputDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        lock (prepared)
        {
            if (prepared.Consumed) throw new InvalidOperationException("Prepared build has already been published or disposed.");
            cancellationToken.ThrowIfCancellationRequested();
            Recheck(prepared.Request);
            var artifacts = Path.Combine(prepared.StagingRoot, "output");
            var actualFiles = Directory.EnumerateFiles(artifacts, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(artifacts, path)).Order(StringComparer.Ordinal);
            var expectedFiles = prepared.Result.Receipt.Outputs.Select(o => o.Path).Append(ReceiptFileName).Order(StringComparer.Ordinal);
            if (!actualFiles.SequenceEqual(expectedFiles)
                || Hash(File.ReadAllBytes(Path.Combine(artifacts, ReceiptFileName))) != prepared.ReceiptSha256)
                throw new AuthoringWorkspaceException("BUILD_STAGED_CHANGED: Prepared artifact list or receipt changed.");
            foreach (var artifact in prepared.Result.Receipt.Outputs)
                if (Hash(File.ReadAllBytes(Path.Combine(artifacts, artifact.Path))) != artifact.Sha256)
                    throw new AuthoringWorkspaceException("BUILD_STAGED_CHANGED: Prepared artifact identity changed.");
            cancellationToken.ThrowIfCancellationRequested();
            prepared.Consumed = true;
            Publish(artifacts, Path.GetFullPath(outputDirectory), cancellationToken, validate: () => Recheck(prepared.Request));
            return prepared.Result;
        }
    }

    internal static IReadOnlyList<AuthoringCompileMapEntry> CompileMap(ProgramDraft draft, string path)
    {
        using var stream = File.OpenRead(path);
        return CompileMap(draft, stream);
    }

    internal static IReadOnlyList<AuthoringCompileMapEntry> CompileMap(ProgramDraft draft, Stream stream)
    {
        var supported = draft.Setup.Select(s => s.NodeId).Concat(Nodes(draft.Measure)).ToHashSet();
        var opaque = RawNodes(draft.Measure).ToHashSet();
        return Array.AsReadOnly(XDocument.Load(stream).Descendants().Where(e => e.Name.LocalName == "TestStep")
            .Select(e => Guid.TryParse((string?)e.Attribute("Id"), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).Select(id => new AuthoringCompileMapEntry(id,
                supported.Contains(id) && !opaque.Contains(id) ? id : null, opaque.Contains(id) ? "opaque" : supported.Contains(id) ? "authoring" : "generated")).ToArray());
        static IEnumerable<Guid> Nodes(IEnumerable<MeasureNode> nodes) => nodes.SelectMany(n => n is RepeatNode r
            ? new[] { n.NodeId }.Concat(Nodes(r.Children)) : new[] { n.NodeId });
        static IEnumerable<Guid> RawNodes(IEnumerable<MeasureNode> nodes) => nodes.SelectMany(n => n is RepeatNode r
            ? RawNodes(r.Children) : n is RawStepNode ? new[] { n.NodeId } : []);
    }
}
