using System.Text.Json;
using System.Text.RegularExpressions;

namespace HardwareTest.Authoring;

/// Stages normal authoring sources, then publishes without replacing any destination.
public sealed partial class AuthoringWorkspaceInitializer
{
    private readonly Action<string>? _beforePublish;
    private readonly Action<string>? _beforeRootMove;
    private readonly Action<string>? _afterRootMove;
    private readonly Action<string, Stream, ReadOnlyMemory<byte>>? _stagingWriter;
    public AuthoringWorkspaceInitializer() { }
    internal AuthoringWorkspaceInitializer(Action<string> beforePublish, Action<string>? beforeRootMove = null, Action<string>? afterRootMove = null,
        Action<string, Stream, ReadOnlyMemory<byte>>? stagingWriter = null)
    {
        _beforePublish = beforePublish;
        _beforeRootMove = beforeRootMove;
        _afterRootMove = afterRootMove;
        _stagingWriter = stagingWriter;
    }

    public WorkspaceCreationPreview Preview(WorkspaceCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        if (Path.GetDirectoryName(root) is not { } parent || !Directory.Exists(parent))
            throw new ArgumentException("Choose a destination with an existing parent folder.");
        ValidateText(request.DisplayName, "workspace display name");
        ValidateText(request.PackageName, "package name");
        if (!PackageVersion().IsMatch(request.PackageVersion)) throw new ArgumentException("Enter a package version such as 0.1.0.");
        var platforms = request.PackageOs.Split(',', StringSplitOptions.TrimEntries);
        if (platforms.Length == 0 || platforms.Any(platform => platform is not ("Windows" or "Linux" or "MacOS")) || platforms.Distinct().Count() != platforms.Length)
            throw new ArgumentException("Choose comma-separated Windows, Linux, or MacOS package platforms.");
        var template = AuthoringWorkspaceTemplates.All.SingleOrDefault(item => item.Kind == request.Template)
            ?? throw new ArgumentException("Choose a supported workspace template.");
        var manifest = new AuthoringManifest
        {
            SchemaVersion = AuthoringSchemaVersions.Manifest,
            Schema = "./authoring.schema.json",
            DisplayName = request.DisplayName.Trim(),
            Package = new() { Name = request.PackageName.Trim(), Version = request.PackageVersion, Os = string.Join(",", platforms) },
            Dependencies = template.RequiredPackages.Select(package => new AuthoringPackageDependency { Package = package.Package, Version = package.Version }).ToList(),
            IncludeTui = request.IncludeTui
        };
        if (request.IncludeLibraryPackage && !manifest.Dependencies.Any(p => p.Package == AuthoringInstrumentCatalog.LibraryPackage))
            manifest.Dependencies.Add(new() { Package = AuthoringInstrumentCatalog.LibraryPackage, Version = "^0.1.0" });
        PlanInitializationResult? plan = null;
        if (request.Template != WorkspaceTemplateKind.Empty)
            plan = new AuthoringPlanInitializer().Construct(new(request.PlanId)
            {
                DisplayName = request.PlanId,
                DeviceFamily = request.DeviceFamily,
                RequireSerial = request.RequireSerial,
                StartingPoint = template.IsDemo ? PlanStartingPoint.DemoVoltageTask : PlanStartingPoint.Empty,
                UseTemplateHardware = template.IsDemo,
                IncludeTemplateMeasurement = template.IsDemo
            });
        List<string> files = ["authoring.schema.json", "authoring-draft.schema.json", ".gitignore", "authoring-drafts/workspace.authoring.json"];
        if (plan is not null) files.Add($"authoring-drafts/{plan.Draft.PlanId}.authoring.json");
        files.Add(AuthoringWorkspaceLoader.ManifestFileName);
        ValidateDestinations(root, files);
        return new(root, manifest, template, files, plan);
    }

    public AuthoringWorkspace Create(WorkspaceCreationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        // This method is synchronous: ownership stays on its acquiring thread through publication.
        using var ownership = AuthoringPublicationOwnership.Acquire(root, cancellationToken);
        var preview = Preview(request);
        // Existing roots may be mount points or have read-only parents. Stage on their
        // filesystem; new roots use a sibling so the final directory move stays atomic.
        var stagingParent = Directory.Exists(preview.Destination) ? preview.Destination : Path.GetDirectoryName(preview.Destination)!;
        var staging = WorkspacePath(stagingParent, ".ht-workspace-stage-" + Guid.NewGuid().ToString("N"));
        var pathComparer = AuthoringDocumentStore.PathComparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var ownedFiles = new Dictionary<string, byte[]>(pathComparer);
        var ownedDirectories = new List<string>();
        var stagedFiles = new Dictionary<string, byte[]>(pathComparer);
        var stagedDirectories = new List<string>();
        void RecordStagedFile(string relative)
        {
            var path = WorkspacePath(staging, relative);
            stagedFiles.Add(path, File.ReadAllBytes(new AuthoringDocumentStore(staging).ValidatePath(path)));
        }
        void WriteStagedFile(string relative, byte[] bytes)
        {
            var path = new AuthoringDocumentStore(staging).ValidatePath(WorkspacePath(staging, relative));
            // Disable buffering so Dispose cannot add bytes after a failed-write snapshot.
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1);
            stagedFiles.Add(path, []); // Only the successful exclusive open claims this file.
            try
            {
                if (_stagingWriter is null) stream.Write(bytes);
                else _stagingWriter(path, stream, bytes);
                stagedFiles[path] = bytes;
                stream.Flush(true);
            }
            catch
            {
                try
                {
                    var length = stream.Length;
                    // Never adopt observed foreign contents: retain the intended prefix only.
                    if (length <= bytes.Length) stagedFiles[path] = bytes[..(int)length];
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                throw;
            }
        }
        try
        {
            Directory.CreateDirectory(staging);
            stagedDirectories.Add(staging);
            Directory.CreateDirectory(WorkspacePath(staging, "plans"));
            stagedDirectories.Add(WorkspacePath(staging, "plans"));
            Directory.CreateDirectory(WorkspacePath(staging, "authoring-drafts"));
            stagedDirectories.Add(WorkspacePath(staging, "authoring-drafts"));
            WriteStagedFile("authoring.schema.json", ReadSchema("authoring.schema.json"));
            WriteStagedFile("authoring-draft.schema.json", ReadSchema("authoring-draft.schema.json"));
            WriteStagedFile(".gitignore", System.Text.Encoding.UTF8.GetBytes(".authoring/\n*.bak\n*.saving\n*.creating\n"));
            new AuthoringDocumentStore(staging).SaveWorkspace(preview.Manifest);
            RecordStagedFile("authoring-drafts/workspace.authoring.json");
            if (preview.Plan is { } plan)
            {
                var document = AuthoringDocumentDto.FromDraft(plan.Draft);
                document.RequiresCompilation = true;
                new AuthoringDocumentStore(staging).CreateNew(document, cancellationToken);
                RecordStagedFile($"authoring-drafts/{plan.Draft.PlanId}.authoring.json");
            }
            AuthoringWorkspaceLoader.SaveManifest(staging, preview.Manifest);
            RecordStagedFile(AuthoringWorkspaceLoader.ManifestFileName);
            // Load the same source representation the application will open before publication.
            _ = AuthoringSourceWorkspaceLoader.Load(staging);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDestinations(preview.Destination, preview.Files);
            if (!Directory.Exists(preview.Destination))
            {
                _beforePublish?.Invoke(preview.Destination);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateDestinations(preview.Destination, preview.Files);
                _beforeRootMove?.Invoke(staging);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateOwnedPublications(staging, stagedFiles, stagedDirectories);
                _ = AuthoringSourceWorkspaceLoader.Load(staging);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateDestinations(preview.Destination, preview.Files);
                Directory.Move(staging, preview.Destination);
                // Claim only the successful move, before any final load can fail.
                ownedDirectories.Add(preview.Destination);
                ownedDirectories.AddRange(stagedDirectories.Skip(1).Select(path => WorkspacePath(preview.Destination, Path.GetRelativePath(staging, path))));
                foreach (var file in stagedFiles)
                    ownedFiles.Add(WorkspacePath(preview.Destination, Path.GetRelativePath(staging, file.Key)), file.Value);
                stagedFiles.Clear(); stagedDirectories.Clear();
                _afterRootMove?.Invoke(preview.Destination);
                cancellationToken.ThrowIfCancellationRequested();
                var workspace = AuthoringSourceWorkspaceLoader.Load(preview.Destination).Files;
                ValidateOwnedPublications(preview.Destination, ownedFiles, ownedDirectories);
                cancellationToken.ThrowIfCancellationRequested();
                return workspace;
            }
            // Move whole staged directories exclusively; never claim a preexisting directory.
            foreach (var relative in new[] { "plans", "authoring-drafts" })
            {
                var destination = WorkspacePath(preview.Destination, relative);
                _beforePublish?.Invoke(destination);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateDestinations(preview.Destination, preview.Files, ownedDirectories);
                ValidateOwnedPublications(staging, stagedFiles, stagedDirectories);
                ValidateOwnedPublications(preview.Destination, ownedFiles, ownedDirectories);
                if (ownedDirectories.Count == 0) _ = AuthoringSourceWorkspaceLoader.Load(staging);
                var source = WorkspacePath(staging, relative);
                var contents = stagedFiles.Where(file => AuthoringDocumentStore.SamePath(Path.GetDirectoryName(file.Key)!, source, isDirectory: true)).ToArray();
                Directory.Move(source, destination);
                ownedDirectories.Add(destination);
                stagedDirectories.RemoveAll(path => AuthoringDocumentStore.SamePath(path, source, isDirectory: true));
                foreach (var file in contents)
                {
                    ownedFiles.Add(WorkspacePath(destination, Path.GetFileName(file.Key)), file.Value);
                    stagedFiles.Remove(file.Key);
                }
            }
            foreach (var relative in preview.Files.Where(file => !file.StartsWith("authoring-drafts/", StringComparison.Ordinal)))
            {
                var destination = WorkspacePath(preview.Destination, relative);
                _beforePublish?.Invoke(destination);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateDestinations(preview.Destination, [relative], ownedDirectories);
                ValidateOwnedPublications(staging, stagedFiles, stagedDirectories);
                ValidateOwnedPublications(preview.Destination, ownedFiles, ownedDirectories);
                var source = WorkspacePath(staging, relative);
                var bytes = stagedFiles[source];
                File.Move(source, destination);
                ownedFiles.Add(destination, bytes);
                stagedFiles.Remove(source);
            }
            var published = AuthoringSourceWorkspaceLoader.Load(preview.Destination).Files;
            ValidateOwnedPublications(staging, stagedFiles, stagedDirectories);
            ValidateOwnedPublications(preview.Destination, ownedFiles, ownedDirectories);
            cancellationToken.ThrowIfCancellationRequested();
            return published;
        }
        catch
        {
            CleanupOwned(preview.Destination, ownedFiles, ownedDirectories);
            throw;
        }
        finally { CleanupOwned(staging, stagedFiles, stagedDirectories); }
    }

    private static void CleanupOwned(string root, IReadOnlyDictionary<string, byte[]> files, IReadOnlyList<string> directories)
    {
        foreach (var file in files.Reverse())
        {
            try
            {
                var path = new AuthoringDocumentStore(root).ValidatePath(file.Key);
                if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(file.Value)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var directory in directories.Reverse())
        {
            try { Directory.Delete(new AuthoringDocumentStore(root).ValidatePath(directory)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ValidateDestinations(string root, IReadOnlyList<string> files, IReadOnlyList<string>? ownedDirectories = null)
    {
        var store = new AuthoringDocumentStore(root);
        foreach (var path in new[] { root }.Concat(new[] { "plans", "authoring-drafts" }.Select(relative => WorkspacePath(root, relative))).Concat(files.Select(relative => WorkspacePath(root, relative))))
        {
            store.ValidatePath(path);
            for (string? cursor = path; cursor is not null; cursor = Path.GetDirectoryName(cursor))
            {
                var parent = Path.GetDirectoryName(cursor);
                // Creation requires the root leaf's existing spelling, even on Windows.
                // Native ancestor aliases and owned-file identity still use SamePath.
                if (parent is not null && Directory.Exists(parent) && Directory.EnumerateFileSystemEntries(parent).Any(entry =>
                    string.Equals(Path.GetFileName(entry), Path.GetFileName(cursor), StringComparison.OrdinalIgnoreCase)
                    && (!AuthoringDocumentStore.SamePath(entry, cursor, isDirectory: true)
                        || (AuthoringDocumentStore.SamePath(cursor, root, isDirectory: true)
                            && !string.Equals(Path.GetFileName(entry), Path.GetFileName(cursor), StringComparison.Ordinal)))))
                    throw new IOException($"Case-equivalent destination exists: {cursor}");
            }
            if (AuthoringDocumentStore.SamePath(path, root, isDirectory: true)) { if (File.Exists(root)) throw new IOException("Workspace destination is a file."); continue; }
            if (ownedDirectories?.Any(directory => AuthoringDocumentStore.SamePath(directory, path, isDirectory: true)) == true)
            {
                if (!Directory.Exists(path)) throw new IOException($"Created workspace directory changed: {path}");
                continue;
            }
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException($"Workspace destination already exists: {path}");
        }
    }

    internal static void ValidateOwnedPublications(string root, IReadOnlyDictionary<string, byte[]> files, IReadOnlyList<string> directories)
    {
        var store = new AuthoringDocumentStore(root);
        foreach (var directory in directories)
        {
            if (!Directory.Exists(store.ValidatePath(directory)) || Directory.EnumerateFileSystemEntries(directory).Any(entry => !files.Keys.Any(path => AuthoringDocumentStore.SamePath(path, entry))
                && !directories.Any(path => AuthoringDocumentStore.SamePath(path, entry, isDirectory: true))))
                throw new IOException($"Created workspace directory changed: {directory}");
        }
        foreach (var file in files)
        {
            var path = store.ValidatePath(file.Key);
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(file.Value))
                throw new IOException($"Created workspace file changed before manifest publication: {file.Key}");
        }
    }

    private static string WorkspacePath(params string[] segments) => Path.GetFullPath(Path.Combine(segments));

    private static byte[] ReadSchema(string name)
    {
        using var source = typeof(AuthoringWorkspaceInitializer).Assembly.GetManifestResourceStream("HardwareTest.Authoring." + name)
            ?? throw new InvalidOperationException("Workspace schema resource is missing.");
        using var buffer = new MemoryStream(); source.CopyTo(buffer);
        return buffer.ToArray();
    }
    private static void ValidateText(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl)) throw new ArgumentException($"Enter a {label} without control characters.");
    }
    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?(?:\+[A-Za-z0-9.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageVersion();
}
