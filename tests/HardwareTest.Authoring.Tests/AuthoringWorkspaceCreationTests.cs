using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringWorkspaceCreationTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "ht-create-workspace-" + Guid.NewGuid().ToString("N"));
    public AuthoringWorkspaceCreationTests() => Directory.CreateDirectory(_parent);
    private WorkspaceCreationRequest Request(string name = "created", WorkspaceTemplateKind template = WorkspaceTemplateKind.Empty)
        => new(Path.Combine(_parent, name), "Workspace", "Product tests") { Template = template, DeviceFamily = "board-x" };

    [Theory]
    [InlineData(WorkspaceTemplateKind.Empty)]
    [InlineData(WorkspaceTemplateKind.ProductVoltage)]
    [InlineData(WorkspaceTemplateKind.DemoVoltage)]
    public void Templates_create_standard_consistent_sources_and_stable_reopenable_drafts(WorkspaceTemplateKind kind)
    {
        var initializer = new AuthoringWorkspaceInitializer();
        var first = Request("first", kind); var second = Request("second", kind);
        var preview = initializer.Preview(first);
        Assert.False(Directory.Exists(first.Destination));
        Directory.CreateDirectory(second.Destination);
        File.WriteAllText(Path.Combine(second.Destination, "notes.txt"), "unrelated notes");
        initializer.Create(first); initializer.Create(second);
        Assert.Equal("unrelated notes", File.ReadAllText(Path.Combine(second.Destination, "notes.txt")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(first.Destination, "authoring.json")), File.ReadAllBytes(Path.Combine(second.Destination, "authoring.json")));
        foreach (var file in preview.Files) Assert.True(File.Exists(Path.Combine(first.Destination, file)), file);
        Assert.Empty(Directory.GetFiles(Path.Combine(first.Destination, "plans")));
        var loaded = AuthoringSourceWorkspaceLoader.Load(first.Destination);
        Assert.Equal("Product tests", loaded.Files.Manifest.Package.Name);
        Assert.Equal("Workspace", loaded.Files.Manifest.DisplayName);
        Assert.Equal(kind == WorkspaceTemplateKind.ProductVoltage, AuthoringInstrumentCatalog.DeclaresVisa(loaded.Files));
        if (kind == WorkspaceTemplateKind.ProductVoltage)
            Assert.Equal("^0.1.0", Assert.Single(loaded.Files.Manifest.Dependencies, package => package.Package == OpenTapHomeBootstrapper.VisaPackageName).Version);
        Assert.Equal(loaded.Files.Manifest.DisplayName, new AuthoringDocumentStore(first.Destination).LoadWorkspace().Document!.Manifest.DisplayName);
        if (kind == WorkspaceTemplateKind.Empty) Assert.Empty(loaded.Programs);
        else
        {
            var draft = Assert.Single(loaded.Programs);
            Assert.Equal("board-x", draft.Sidecar.DutFamily);
            Assert.Single(draft.Measure);
            if (kind == WorkspaceTemplateKind.ProductVoltage) Assert.Empty(draft.Instruments);
            else Assert.Contains("Mock", Assert.Single(draft.Instruments).TypeId);
            Assert.Equal(draft.Measure[0].NodeId, AuthoringSourceWorkspaceLoader.Load(first.Destination).Programs.Single().Measure[0].NodeId);
            Assert.True(new AuthoringDocumentStore(first.Destination).Load(draft.PlanId).Document!.RequiresCompilation);
        }
    }

    [Theory]
    [InlineData("authoring.json")]
    [InlineData("Authoring.JSON")]
    [InlineData("plans")]
    [InlineData("Authoring-Drafts")]
    [InlineData(".gitignore")]
    public void Conflicts_preserve_all_existing_bytes(string collision)
    {
        var request = Request(); Directory.CreateDirectory(request.Destination);
        var path = Path.Combine(request.Destination, collision); File.WriteAllText(path, "unrelated");
        Assert.Throws<IOException>(() => new AuthoringWorkspaceInitializer().Create(request));
        Assert.Equal("unrelated", File.ReadAllText(path)); Assert.Single(Directory.GetFileSystemEntries(request.Destination));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Late_failure_or_cancellation_removes_only_owned_publications_and_leaves_no_manifest(bool cancel)
    {
        var request = Request(template: WorkspaceTemplateKind.ProductVoltage); Directory.CreateDirectory(request.Destination);
        var unrelated = Path.Combine(request.Destination, "notes.txt"); File.WriteAllText(unrelated, "keep");
        using var cancellation = new CancellationTokenSource();
        var initializer = new AuthoringWorkspaceInitializer(path =>
        {
            if (Path.GetFileName(path) != "authoring.json") return;
            if (cancel) cancellation.Cancel();
            else throw new IOException("Injected publication failure");
        });
        if (cancel) Assert.Throws<OperationCanceledException>(() => initializer.Create(request, cancellation.Token));
        else Assert.Throws<IOException>(() => initializer.Create(request));
        Assert.Equal([unrelated], Directory.GetFileSystemEntries(request.Destination));
        Assert.Equal("keep", File.ReadAllText(unrelated));
    }

    [Fact]
    public void Final_conflict_and_external_changes_are_preserved_without_complete_manifest()
    {
        var request = Request(); Directory.CreateDirectory(request.Destination);
        var manifest = Path.Combine(request.Destination, "authoring.json");
        var initializer = new AuthoringWorkspaceInitializer(path =>
        {
            if (path != manifest) return;
            File.WriteAllText(Path.Combine(request.Destination, ".gitignore"), "externally changed");
            File.WriteAllText(manifest, "unrelated manifest");
            File.WriteAllText(Path.Combine(request.Destination, "plans", "unrelated.txt"), "new bytes");
        });
        Assert.Throws<IOException>(() => initializer.Create(request));
        Assert.Equal("unrelated manifest", File.ReadAllText(manifest));
        Assert.Equal("externally changed", File.ReadAllText(Path.Combine(request.Destination, ".gitignore")));
        Assert.Equal("new bytes", File.ReadAllText(Path.Combine(request.Destination, "plans", "unrelated.txt")));
        Assert.False(Directory.Exists(Path.Combine(request.Destination, "authoring-drafts")));
    }

    [Theory]
    [InlineData(".gitignore")]
    [InlineData("authoring-drafts/workspace.authoring.json")]
    public void Changed_owned_publications_abort_before_manifest_and_preserve_external_bytes(string changed)
    {
        var request = Request(); Directory.CreateDirectory(request.Destination);
        var altered = Path.Combine(request.Destination, changed);
        var initializer = new AuthoringWorkspaceInitializer(path =>
        {
            if (Path.GetFileName(path) == "authoring.json") File.WriteAllText(altered, "external bytes");
        });
        Assert.Throws<IOException>(() => initializer.Create(request));
        Assert.Equal("external bytes", File.ReadAllText(altered));
        Assert.False(File.Exists(Path.Combine(request.Destination, "authoring.json")));
    }

    [Fact]
    public void Links_and_case_equivalent_roots_are_rejected_without_writes()
    {
        var actual = Path.Combine(_parent, "actual"); Directory.CreateDirectory(actual);
        var link = Path.Combine(_parent, "link"); Directory.CreateSymbolicLink(link, actual);
        Assert.Throws<IOException>(() => new AuthoringWorkspaceInitializer().Create(Request("link")));
        Assert.Empty(Directory.GetFileSystemEntries(actual));
        Assert.Throws<IOException>(() => new AuthoringWorkspaceInitializer().Create(Request("ACTUAL")));
        Directory.Delete(link);
    }

    [Fact]
    public async Task Case_equivalent_creators_are_reserved_through_final_directory_publication()
    {
        var first = Request("Reserved"); var competing = Request("reserved");
        using var reachedCommit = new ManualResetEventSlim();
        using var releaseCommit = new ManualResetEventSlim();
        string? staging = null;
        var initializer = new AuthoringWorkspaceInitializer(_ => { }, path =>
        {
            staging = path;
            reachedCommit.Set();
            if (!releaseCommit.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test did not release publication.");
        });
        var publication = Task.Run(() => initializer.Create(first));
        try
        {
            Assert.True(reachedCommit.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(_parent, Path.GetDirectoryName(staging));
            Assert.False(Directory.Exists(first.Destination));
            Assert.Throws<IOException>(() => new AuthoringWorkspaceInitializer().Create(competing));
            Assert.False(Directory.Exists(competing.Destination));
        }
        finally { releaseCommit.Set(); }
        var workspace = await publication;
        Assert.Equal(first.Destination, workspace.Root);
        Assert.False(Directory.Exists(staging));
        Assert.Equal([first.Destination], Directory.GetDirectories(_parent));
    }

    [Fact]
    public void Failed_sibling_staging_cleans_only_its_private_directory()
    {
        var request = Request();
        var unrelated = Path.Combine(_parent, ".ht-workspace-stage-unrelated");
        Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "notes.txt"), "keep");
        var initializer = new AuthoringWorkspaceInitializer(_ => throw new IOException("Injected before new-root publication"));
        Assert.Throws<IOException>(() => initializer.Create(request));
        Assert.False(Directory.Exists(request.Destination));
        Assert.Equal([unrelated], Directory.GetDirectories(_parent));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(unrelated, "notes.txt")));
    }

    [Fact]
    public void Existing_destination_stages_inside_its_filesystem_and_cleans_failure()
    {
        var request = Request(); Directory.CreateDirectory(request.Destination);
        var unrelated = Path.Combine(request.Destination, "notes.txt"); File.WriteAllText(unrelated, "keep");
        var initializer = new AuthoringWorkspaceInitializer(_ =>
        {
            var stage = Assert.Single(Directory.GetDirectories(request.Destination, ".ht-workspace-stage-*"));
            Assert.Equal(request.Destination, Path.GetDirectoryName(stage));
            throw new IOException("Injected before first directory publication");
        });
        Assert.Throws<IOException>(() => initializer.Create(request));
        Assert.Equal([unrelated], Directory.GetFileSystemEntries(request.Destination));
        Assert.Equal("keep", File.ReadAllText(unrelated));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void New_root_post_move_load_failure_or_cancellation_rolls_back_only_owned_bytes(bool cancel, bool externalChanges)
    {
        var request = Request(template: WorkspaceTemplateKind.ProductVoltage);
        using var cancellation = new CancellationTokenSource();
        var reachedMovedRoot = false;
        var initializer = new AuthoringWorkspaceInitializer(_ => { }, afterRootMove: root =>
        {
            reachedMovedRoot = true;
            Assert.Equal(request.Destination, root);
            Assert.True(File.Exists(Path.Combine(root, "authoring.json")));
            if (externalChanges)
            {
                File.WriteAllText(Path.Combine(root, ".gitignore"), "external changed bytes");
                File.WriteAllText(Path.Combine(root, "notes.txt"), "foreign root bytes");
                File.WriteAllText(Path.Combine(root, "authoring-drafts", "notes.txt"), "foreign draft bytes");
            }
            if (cancel) cancellation.Cancel();
            else Directory.Delete(Path.Combine(root, "plans"));
        });
        if (cancel) Assert.Throws<OperationCanceledException>(() => initializer.Create(request, cancellation.Token));
        else Assert.Throws<AuthoringWorkspaceException>(() => initializer.Create(request));
        Assert.True(reachedMovedRoot);
        Assert.False(File.Exists(Path.Combine(request.Destination, "authoring.json")));
        Assert.Empty(Directory.GetDirectories(_parent, ".ht-workspace-stage-*"));
        if (!externalChanges) Assert.False(Directory.Exists(request.Destination));
        else
        {
            Assert.Equal("external changed bytes", File.ReadAllText(Path.Combine(request.Destination, ".gitignore")));
            Assert.Equal("foreign root bytes", File.ReadAllText(Path.Combine(request.Destination, "notes.txt")));
            Assert.Equal("foreign draft bytes", File.ReadAllText(Path.Combine(request.Destination, "authoring-drafts", "notes.txt")));
            Assert.Equal(3, Directory.GetFiles(request.Destination, "*", SearchOption.AllDirectories).Length);
            Assert.False(Directory.Exists(Path.Combine(request.Destination, "plans")));
        }
    }

    [Fact]
    public void Final_staging_corruption_aborts_without_publishing_a_new_root()
    {
        var request = Request();
        var initializer = new AuthoringWorkspaceInitializer(_ => { }, stage => Directory.Delete(Path.Combine(stage, "plans")));
        Assert.ThrowsAny<Exception>(() => initializer.Create(request));
        Assert.False(Directory.Exists(request.Destination));
        Assert.Empty(Directory.GetFileSystemEntries(_parent));
    }

    [Fact]
    public void Racing_preexisting_root_is_never_claimed_or_removed_when_move_fails()
    {
        var request = Request();
        var initializer = new AuthoringWorkspaceInitializer(_ => { }, _ =>
        {
            Directory.CreateDirectory(request.Destination);
            File.WriteAllText(Path.Combine(request.Destination, "notes.txt"), "racing root bytes");
        });
        Assert.Throws<IOException>(() => initializer.Create(request));
        Assert.Equal("racing root bytes", File.ReadAllText(Path.Combine(request.Destination, "notes.txt")));
        Assert.Equal([request.Destination], Directory.GetFileSystemEntries(_parent));
        Assert.Single(Directory.GetFileSystemEntries(request.Destination));
    }

    [Theory]
    [InlineData(WorkspaceTemplateKind.Empty)]
    [InlineData(WorkspaceTemplateKind.DemoVoltage)]
    public void Physical_package_is_an_explicit_setting_and_never_implies_hardware(WorkspaceTemplateKind kind)
    {
        var request = Request(template: kind) with { IncludeVisaPackage = true };
        new AuthoringWorkspaceInitializer().Create(request);
        var loaded = AuthoringSourceWorkspaceLoader.Load(request.Destination);
        Assert.True(AuthoringInstrumentCatalog.DeclaresVisa(loaded.Files));
        Assert.Equal("^0.1.0", Assert.Single(loaded.Files.Manifest.Dependencies, package => package.Package == OpenTapHomeBootstrapper.VisaPackageName).Version);
        if (kind == WorkspaceTemplateKind.Empty) Assert.Empty(loaded.Programs);
        else Assert.Contains("Mock", Assert.Single(Assert.Single(loaded.Programs).Instruments).TypeId);
    }

    [Theory]
    [InlineData("workspace.authoring.json", false)]
    [InlineData("workspace.authoring.json", true)]
    [InlineData("voltage.authoring.json", false)]
    public void Existing_root_rejects_changed_staged_sources_and_preserves_external_source_bytes(string sourceName, bool validCatalog)
    {
        var request = Request(template: WorkspaceTemplateKind.ProductVoltage); Directory.CreateDirectory(request.Destination);
        var notes = Path.Combine(request.Destination, "notes.txt"); File.WriteAllText(notes, "keep existing bytes");
        string? stage = null; string? source = null; string? changedBytes = null;
        var initializer = new AuthoringWorkspaceInitializer(_ =>
        {
            if (stage is not null) return;
            stage = Assert.Single(Directory.GetDirectories(request.Destination, ".ht-workspace-stage-*"));
            source = Path.Combine(stage, "authoring-drafts", sourceName);
            changedBytes = validCatalog ? File.ReadAllText(source).Replace("Workspace", "Changed catalog", StringComparison.Ordinal) : "externally invalid source JSON";
            File.WriteAllText(source, changedBytes);
        });
        Assert.ThrowsAny<Exception>(() => initializer.Create(request));
        Assert.False(File.Exists(Path.Combine(request.Destination, "authoring.json")));
        Assert.False(Directory.Exists(Path.Combine(request.Destination, "plans")));
        Assert.False(Directory.Exists(Path.Combine(request.Destination, "authoring-drafts")));
        Assert.Equal("keep existing bytes", File.ReadAllText(notes));
        Assert.Equal(changedBytes, File.ReadAllText(source!));
        Assert.Equal([source!], Directory.GetFiles(stage!, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Staging_failure_or_cancellation_preserves_only_foreign_or_changed_bytes(bool existingRoot, bool cancel, bool changeOwnedFile)
    {
        var request = Request(template: WorkspaceTemplateKind.ProductVoltage);
        if (existingRoot) Directory.CreateDirectory(request.Destination);
        using var cancellation = new CancellationTokenSource();
        string? stage = null; string? external = null;
        var initializer = new AuthoringWorkspaceInitializer(_ =>
        {
            stage = Assert.Single(Directory.GetDirectories(existingRoot ? request.Destination : _parent, ".ht-workspace-stage-*"));
            external = Path.Combine(stage, changeOwnedFile ? ".gitignore" : "foreign/notes.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(external)!);
            File.WriteAllText(external, "retain external staged bytes");
            if (cancel) cancellation.Cancel();
            else throw new IOException("Injected failure after external staging write");
        });
        if (cancel) Assert.Throws<OperationCanceledException>(() => initializer.Create(request, cancellation.Token));
        else Assert.Throws<IOException>(() => initializer.Create(request));
        Assert.False(File.Exists(Path.Combine(request.Destination, "authoring.json")));
        Assert.Equal("retain external staged bytes", File.ReadAllText(external!));
        Assert.Equal([external!], Directory.GetFiles(stage!, "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(stage!, "plans")));
        Assert.False(Directory.Exists(Path.Combine(stage!, "authoring-drafts")));
        if (!existingRoot) Assert.False(Directory.Exists(request.Destination));
        else Assert.Equal([stage!], Directory.GetFileSystemEntries(request.Destination));
    }

    [Fact]
    public void Empty_workspace_can_immediately_create_edit_save_and_reopen_an_incomplete_normal_document()
    {
        var request = Request(); new AuthoringWorkspaceInitializer().Create(request);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(request.Destination);
        vm.InitializePlan(new("new-plan") { DisplayName = "New plan", WorkspaceRoot = request.Destination });
        vm.DisplayName = "Edited draft";
        Assert.True(vm.HasUnsavedChanges);
        Assert.True(vm.SaveAll().Succeeded);
        vm.Open(request.Destination); vm.SelectProgram("new-plan");
        Assert.Equal("Edited draft", vm.SelectedProgram!.Sidecar.DisplayName);
        var source = new AuthoringDocumentStore(request.Destination).Load("new-plan").Document!;
        Assert.Equal("new-plan", source.PlanId);
        Assert.Equal("Edited draft", source.ToDraft().Sidecar.DisplayName);
        Assert.False(source.RequiresCompilation);
        var compiledPath = Path.Combine(request.Destination, "plans", "new-plan.TapPlan");
        Assert.True(File.Exists(compiledPath));
        Assert.Equal(source.PlanId, new PlanCompiler().Load(compiledPath).PlanId);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Theory]
    [InlineData("authoring.schema.json", false)]
    [InlineData("authoring-draft.schema.json", false)]
    [InlineData(".gitignore", false)]
    [InlineData("authoring.schema.json", true)]
    [InlineData("authoring-draft.schema.json", true)]
    [InlineData(".gitignore", true)]
    public void Construction_write_or_flush_failure_cleans_exclusively_created_partial_bytes(string failingFile, bool flushFailure)
    {
        var request = Request(); var publicationHookReached = false; var constructionFailureReached = false;
        var initializer = new AuthoringWorkspaceInitializer(_ => publicationHookReached = true, stagingWriter: (path, stream, bytes) =>
        {
            if (Path.GetFileName(path) != failingFile) { stream.Write(bytes.Span); stream.Flush(); return; }
            constructionFailureReached = true;
            stream.Write(bytes.Span[..(flushFailure ? bytes.Length : 7)]);
            stream.Flush();
            Assert.True(stream.Length > 0);
            throw new IOException(flushFailure ? "Injected construction flush failure" : "Injected partial construction write failure");
        });
        Assert.Throws<IOException>(() => initializer.Create(request));
        Assert.True(constructionFailureReached); Assert.False(publicationHookReached);
        Assert.False(Directory.Exists(request.Destination)); Assert.Empty(Directory.GetFileSystemEntries(_parent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Construction_failure_preserves_changed_or_foreign_bytes(bool changeCreatedFile)
    {
        var request = Request(); string? stage = null; string? external = null;
        var externalBytes = System.Text.Encoding.UTF8.GetBytes("external staged bytes");
        var initializer = new AuthoringWorkspaceInitializer(_ => throw new InvalidOperationException("Publication must not start"), stagingWriter: (path, stream, bytes) =>
        {
            stage = Path.GetDirectoryName(path);
            stream.Write(bytes.Span[..7]); stream.Flush();
            if (changeCreatedFile) { external = path; stream.Position = 0; stream.Write(externalBytes); stream.Flush(); }
            else { external = Path.Combine(stage!, "notes.txt"); File.WriteAllBytes(external, externalBytes); }
            throw new IOException("Injected construction failure after external write");
        });
        Assert.Throws<IOException>(() => initializer.Create(request));
        Assert.False(Directory.Exists(request.Destination));
        Assert.Equal(externalBytes, File.ReadAllBytes(external!));
        Assert.Equal([external!], Directory.GetFiles(stage!, "*", SearchOption.AllDirectories));
    }

    public void Dispose() => Directory.Delete(_parent, recursive: true);
}
