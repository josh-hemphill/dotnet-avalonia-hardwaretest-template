using System.Collections.Concurrent;
using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.BenchReferences;

/// Immutable profile files plus separate retirement markers under the bench's data directory.
public sealed class FileBenchReferenceStore : IBenchReferenceStore
{
    public const string DirectoryName = "bench-references";
    private readonly string _directory;
    private readonly string _retirements;
    private readonly IClock _clock;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CatalogGates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public FileBenchReferenceStore(string dataDirectory, IClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _directory = PathContainment.CombineUnderRoot(dataDirectory, DirectoryName);
        _retirements = PathContainment.CombineUnderRoot(_directory, "retired");
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<IReadOnlyList<BenchReferenceCatalogEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return [];
        var result = new List<BenchReferenceCatalogEntry>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            BenchReferenceProfile? profile = null;
            try
            {
                var id = Path.GetFileNameWithoutExtension(path);
                profile = await ReadProfileAsync(id, cancellationToken).ConfigureAwait(false);
                var retired = await ReadRetirementAsync(profile, cancellationToken).ConfigureAwait(false);
                result.Add(new BenchReferenceCatalogEntry { Path = path, Profile = profile, IsRetired = retired is not null, Retirement = retired });
            }
            catch (Exception ex) when (IsRecordError(ex))
            {
                result.Add(new BenchReferenceCatalogEntry { Path = path, Profile = profile, Error = ex.Message });
            }
        }
        return result;
    }

    public async Task<BenchReferenceProfile> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        var profile = await ReadProfileAsync(id, cancellationToken).ConfigureAwait(false);
        var retired = await ReadRetirementAsync(profile, cancellationToken).ConfigureAwait(false);
        if (retired is not null)
            throw new InvalidOperationException($"Reference '{profile.Name}' is retired: {retired.Reason}. Select or acquire another reference.");
        return BenchReferenceEvaluator.CloneProfile(profile);
    }

    public async Task SaveAsync(BenchReferenceProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var sourceError = BenchReferenceEvaluator.ProfileError(profile);
        if (sourceError is not null) throw new InvalidOperationException(sourceError);
        // Snapshot caller-owned collections before validation and serialization.
        var owned = BenchReferenceEvaluator.CloneProfile(profile);
        var error = BenchReferenceEvaluator.ProfileError(owned);
        if (error is not null) throw new InvalidOperationException(error);
        var path = PathFor(owned.Id);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(owned, AppJsonContext.Default.BenchReferenceProfile);
        using var lease = await LockCatalogAsync(cancellationToken).ConfigureAwait(false);
        await PublishNewAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task RetireAsync(string id, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        using var lease = await LockCatalogAsync(cancellationToken).ConfigureAwait(false);
        var profile = await ReadProfileAsync(id, cancellationToken).ConfigureAwait(false);
        var existing = await ReadRetirementAsync(profile, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            throw new InvalidOperationException($"Reference '{profile.Name}' is already retired.");
        var marker = new BenchReferenceRetirement
        {
            ReferenceId = profile.Id,
            ReferenceSha256 = profile.ContentSha256,
            RetiredAt = _clock.UtcNow.ToUniversalTime(),
            Reason = reason.Trim(),
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(marker, AppJsonContext.Default.BenchReferenceRetirement);
        await PublishNewAsync(RetirementPath(profile.Id), bytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BenchReferenceAcceptance> FreezeAsync(IReadOnlyList<BenchReferenceSelection> selections,
        BenchReferenceContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(context);
        if (selections.Count == 0) throw new InvalidOperationException("Choose the required reference inputs before Run.");
        var owned = selections.Select(selection => selection with { Rule = BenchReferenceEvaluator.CloneRule(selection.Rule) }).ToArray();
        var errors = BenchReferenceEvaluator.ValidateRules(owned.Select(selection => selection.Rule));
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        foreach (var selection in owned)
        {
            RequireId(selection.ReferenceId);
            if (selection.ExpectedRevision < 1 || !BenchReferenceIdentity.IsHash(selection.ExpectedSha256))
                throw new InvalidOperationException("Inspect the full reference identity and revision before accepting Run.");
        }
        var acceptedContext = context with { };
        using var lease = await LockCatalogAsync(cancellationToken).ConfigureAwait(false);
        var loaded = new List<(BenchReferenceSelection Selection, BenchReferenceProfile Profile)>();
        foreach (var selection in owned)
        {
            var profile = await ReadProfileAsync(selection.ReferenceId, cancellationToken).ConfigureAwait(false);
            if (await ReadRetirementAsync(profile, cancellationToken).ConfigureAwait(false) is not null)
                throw new InvalidOperationException($"Reference '{profile.Name}' is retired. Select or acquire another reference.");
            if (profile.Id != selection.ReferenceId || profile.Revision != selection.ExpectedRevision
                || !string.Equals(profile.ContentSha256, selection.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Reference '{profile.Name}' changed since selection. Reload, inspect and confirm the intended values before Run.");
            loaded.Add((selection, profile));
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Save/retirement cannot commit between these reads and this acceptance instant.
        var acceptedAt = _clock.UtcNow.ToUniversalTime();
        var uses = loaded.Select(item => BenchReferenceEvaluator.Freeze(item.Selection.Rule, item.Profile, acceptedContext, acceptedAt)).ToArray();
        return new BenchReferenceAcceptance { AcceptedAt = acceptedAt, Uses = Array.AsReadOnly(uses) };
    }

    public string PathFor(string id)
    {
        RequireId(id);
        return PathContainment.CombineUnderRoot(_directory, id + ".json");
    }

    private string RetirementPath(string id)
    {
        RequireId(id);
        return PathContainment.CombineUnderRoot(_retirements, id + ".json");
    }

    private async Task<BenchReferenceProfile> ReadProfileAsync(string id, CancellationToken cancellationToken)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) throw new FileNotFoundException("The selected bench reference is missing. Select or acquire it explicitly.", path);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        DocumentSchemaGate.ReadHeader(bytes, SchemaDocumentTypes.BenchReferenceProfile,
            SchemaVersions.BenchReferenceProfile, path);
        var profile = JsonSerializer.Deserialize(bytes, AppJsonContext.Default.BenchReferenceProfile)
            ?? throw new InvalidOperationException("Bench reference file is empty.");
        var error = BenchReferenceEvaluator.ProfileError(profile);
        if (error is not null) throw new InvalidOperationException(error);
        if (!string.Equals(profile.Id, id, StringComparison.Ordinal))
            throw new InvalidOperationException("Bench reference filename and stored identity disagree.");
        return profile;
    }

    private async Task<BenchReferenceRetirement?> ReadRetirementAsync(BenchReferenceProfile profile, CancellationToken cancellationToken)
    {
        var path = RetirementPath(profile.Id);
        if (!File.Exists(path)) return null;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        DocumentSchemaGate.ReadHeader(bytes, SchemaDocumentTypes.BenchReferenceRetirement,
            SchemaVersions.BenchReferenceRetirement, path);
        var marker = JsonSerializer.Deserialize(bytes, AppJsonContext.Default.BenchReferenceRetirement)
            ?? throw new InvalidOperationException("Reference retirement marker is empty.");
        if (marker.SchemaVersion != SchemaVersions.BenchReferenceRetirement
            || marker.ReferenceId != profile.Id || marker.ReferenceSha256 != profile.ContentSha256
            || marker.RetiredAt == default || string.IsNullOrWhiteSpace(marker.Reason))
            throw new InvalidOperationException("Reference retirement metadata is invalid; preserve it and correct the catalog before selection.");
        return marker;
    }

    private static void RequireId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new InvalidOperationException("Reference identity must be its complete stable ID, not a display name or path.");
    }

    private static bool IsRecordError(Exception ex)
        => ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException;

    private async Task<CatalogLease> LockCatalogAsync(CancellationToken cancellationToken)
    {
        var gate = CatalogGates.GetOrAdd(Path.GetFullPath(_directory), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            // This also excludes other application processes honoring the catalog protocol.
            var file = new FileStream(Path.Combine(_directory, ".catalog.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            return new CatalogLease(gate, file);
        }
        catch { gate.Release(); throw; }
    }

    private sealed class CatalogLease(SemaphoreSlim gate, FileStream file) : IDisposable
    {
        public void Dispose()
        {
            try { file.Dispose(); }
            finally { gate.Release(); }
        }
    }

    /// Same-directory rename is the publication point; overwrite is prohibited even after a race.
    private static async Task PublishNewAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pending = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, options: FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(pending, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
        }
    }
}
