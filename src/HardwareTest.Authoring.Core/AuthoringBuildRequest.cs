using System.Collections.ObjectModel;

namespace HardwareTest.Authoring;

/// A saved build owns its bytes; neither mutable manifests nor editor drafts are retained.
public sealed class AuthoringBuildRequest
{
    internal AuthoringBuildRequest(string root, PackOptions options, IReadOnlyList<BuildInputTree> trees, IReadOnlyDictionary<string, string?> environment)
    {
        WorkspaceRoot = root;
        EnvironmentValues = new ReadOnlyDictionary<string, string?>(environment.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        Environment = AuthoringBuildService.EnvironmentIdentity(EnvironmentValues);
        Options = new PackOptions
        {
            Home = options.Home,
            TuiHome = options.TuiHome,
            Compat = options.Compat,
            DotNetExecutable = options.DotNetExecutable,
            Offline = options.Offline,
            PreflightCompleted = options.PreflightCompleted
        };
        Trees = trees;
        Inputs = new ReadOnlyCollection<AuthoringBuildInput>(trees.SelectMany(t => t.Files.Select(f =>
            new AuthoringBuildInput(f.Path, f.Hash, f.Target))).ToArray());
    }
    public string WorkspaceRoot { get; }
    public IReadOnlyList<AuthoringBuildInput> Inputs { get; }
    public IReadOnlyList<AuthoringBuildEnvironmentIdentity> Environment { get; }
    internal IReadOnlyDictionary<string, string?> EnvironmentValues { get; }
    internal PackOptions Options { get; }
    internal IReadOnlyList<BuildInputTree> Trees { get; }
}

public sealed record AuthoringBuildInput(string Path, string Sha256, string ResolvedPath);
public sealed record AuthoringBuildOutput(string Path, string Sha256);
public sealed record AuthoringBuildSource(string PlanId, long? SavedRevision, string? SourceSha256);
public sealed record AuthoringCompileMapEntry(Guid StepId, Guid? NodeId, string Kind);
public sealed record AuthoringCompileResult
{
    public AuthoringCompileResult(string planId, string planSha256, IEnumerable<AuthoringCompileMapEntry> sourceMap)
    { PlanId = planId; PlanSha256 = planSha256; SourceMap = Array.AsReadOnly(sourceMap.ToArray()); }
    public string PlanId { get; }
    public string PlanSha256 { get; }
    public IReadOnlyList<AuthoringCompileMapEntry> SourceMap { get; }
}
public sealed record AuthoringBuildReceipt
{
    public AuthoringBuildReceipt(int schemaVersion, string buildId, DateTimeOffset completedAt,
        IEnumerable<string> includedPlans, IEnumerable<string> excludedPlans,
        IEnumerable<AuthoringBuildSource> sources, IEnumerable<AuthoringBuildInput> inputs,
        IEnumerable<ShipDependency> dependencies, IEnumerable<PackPreflightFinding> requiredChecks,
        IEnumerable<AuthoringCompileResult> compilation, IEnumerable<AuthoringBuildOutput> outputs, long? workspaceSavedRevision = null, IEnumerable<AuthoringBuildEnvironmentIdentity>? environment = null)
    {
        SchemaVersion = schemaVersion; BuildId = buildId; CompletedAt = completedAt; WorkspaceSavedRevision = workspaceSavedRevision;
        IncludedPlans = Array.AsReadOnly(includedPlans.ToArray()); ExcludedPlans = Array.AsReadOnly(excludedPlans.ToArray());
        Sources = Array.AsReadOnly(sources.ToArray()); Inputs = Array.AsReadOnly(inputs.ToArray());
        Dependencies = Array.AsReadOnly(dependencies.ToArray()); RequiredChecks = Array.AsReadOnly(requiredChecks.ToArray());
        Compilation = Array.AsReadOnly(compilation.ToArray()); Outputs = Array.AsReadOnly(outputs.ToArray());
        Environment = Array.AsReadOnly((environment ?? []).ToArray());
    }
    public int SchemaVersion { get; }
    public long? WorkspaceSavedRevision { get; }
    public string BuildId { get; }
    public DateTimeOffset CompletedAt { get; }
    public IReadOnlyList<string> IncludedPlans { get; }
    public IReadOnlyList<string> ExcludedPlans { get; }
    public IReadOnlyList<AuthoringBuildSource> Sources { get; }
    public IReadOnlyList<AuthoringBuildInput> Inputs { get; }
    public IReadOnlyList<ShipDependency> Dependencies { get; }
    public IReadOnlyList<PackPreflightFinding> RequiredChecks { get; }
    public IReadOnlyList<AuthoringCompileResult> Compilation { get; }
    public IReadOnlyList<AuthoringBuildOutput> Outputs { get; }
    public IReadOnlyList<AuthoringBuildEnvironmentIdentity> Environment { get; }
}
public sealed record AuthoringBuildResult
{
    public AuthoringBuildResult(ShipManifest manifest, AuthoringBuildReceipt receipt)
    {
        Manifest = manifest with
        {
            Files = Array.AsReadOnly(manifest.Files.ToArray()),
            Dependencies = Array.AsReadOnly(manifest.ResolvedDependencies.ToArray())
        };
        Receipt = receipt;
    }
    public ShipManifest Manifest { get; }
    public AuthoringBuildReceipt Receipt { get; }
}
internal sealed record BuildInputFile(string Path, string RelativePath, string Target, string Hash, byte[] Bytes);
internal sealed record BuildInputTree(string Root, string StageRelativePath, bool Recursive,
    IReadOnlyList<BuildInputFile> Files, IReadOnlyList<string> Links, bool Materialize = true);

/// Owns isolated prepared artifacts until publication or disposal. No live paths or editable buffers are exposed.
public sealed class AuthoringPreparedBuild : IDisposable
{
    internal AuthoringPreparedBuild(AuthoringBuildRequest request, string stagingRoot, AuthoringBuildResult result)
    {
        Request = request; StagingRoot = stagingRoot; Result = result;
        ReceiptSha256 = AuthoringBuildService.Hash(File.ReadAllBytes(Path.Combine(stagingRoot, "output", AuthoringBuildService.ReceiptFileName)));
    }
    internal AuthoringBuildRequest Request { get; }
    internal string StagingRoot { get; }
    internal string ReceiptSha256 { get; }
    internal bool Consumed { get; set; }
    public AuthoringBuildResult Result { get; }
    public void Dispose()
    {
        lock (this)
        {
            Consumed = true;
            AuthoringBuildService.CleanupStaging(StagingRoot);
        }
    }
}
