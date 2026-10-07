using System.IO.Compression;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringStandaloneClaimTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData("nested-library")]
    [InlineData("case-alias")]
    [InlineData("nested-wrapper")]
    [InlineData("alias-metadata")]
    [InlineData("malformed-library-metadata")]
    [InlineData("malformed-counterpart-metadata")]
    public async Task Undeclared_library_claims_anywhere_in_the_home_block_actual_TUI_launch_without_home_changes(string claim)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) Assert.Skip("Production launcher supports Linux and Windows.");
        var (workspace, home, plan) = PrepareMockHome();
        var custom = Path.Combine(home.Root, "Plugins", "Custom"); Directory.CreateDirectory(custom);
        if (claim == "nested-wrapper")
            CopyPayload(StandaloneVisaPackage.OpenArchive(), StandaloneVisaPackage.WrapperFileName, Path.Combine(custom, StandaloneVisaPackage.WrapperFileName));
        else if (claim is "nested-library" or "case-alias")
            CopyPayload(PublishedInstrumentComponents.OpenArchive(), "InstrumentComponents.OpenTap.dll",
                claim == "case-alias" ? Path.Combine(home.Root, "instrumentcomponents.opentap.dll") : Path.Combine(custom, "InstrumentComponents.OpenTap.dll"));
        else
        {
            var package = claim == "malformed-counterpart-metadata" ? StandaloneVisaPackage.PackageName : PublishedInstrumentComponents.PackageName;
            File.WriteAllText(Path.Combine(custom, "package.xml"), $"<Package Name=\"{package}\" Version=\"0.1.1\"><Files />" + (claim == "alias-metadata" ? "</Package>" : ""));
        }
        var before = Snapshot(home);
        await AssertBlocked(workspace, home, plan);
        AssertSnapshot(before, home);
    }

    [Theory]
    [InlineData("nested", "HardwareTest.OpenTap.StandaloneVisa.dll")]
    [InlineData("nested", "InstrumentComponents.OpenTap.Visa.dll")]
    [InlineData("nested", "InstrumentComponents.Visa.dll")]
    [InlineData("case", "HardwareTest.OpenTap.StandaloneVisa.dll")]
    [InlineData("case", "InstrumentComponents.OpenTap.Visa.dll")]
    [InlineData("case", "InstrumentComponents.Visa.dll")]
    [InlineData("metadata-nested", "")]
    [InlineData("metadata-file-case", "")]
    [InlineData("metadata-directory-case", "")]
    [InlineData("metadata-name-case", "")]
    public async Task Complete_prepared_home_rejects_standalone_payload_and_metadata_aliases_before_TUI_spawn(string alias, string file)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) Assert.Skip("Production launcher supports Linux and Windows.");
        if (OperatingSystem.IsWindows() && (alias is "case" or "metadata-file-case" or "metadata-directory-case"))
            Assert.Skip("Distinct case-alias entries require a case-sensitive filesystem.");
        var (workspace, home, plan) = PrepareMockHome();
        workspace.Manifest.Dependencies.Add(new() { Package = PublishedInstrumentComponents.PackageName, Version = PublishedInstrumentComponents.Version });
        new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true });
        Assert.True(StandaloneVisaReadiness.Assess(home).Available);
        workspace.Manifest.Dependencies.RemoveAll(dependency => dependency.Package.Equals(PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase));
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        var custom = Path.Combine(home.Root, "Plugins", "Custom"); Directory.CreateDirectory(custom);
        var metadata = Path.Combine(home.Root, "Packages", StandaloneVisaPackage.PackageName, "package.xml");
        if (alias is "nested" or "case")
            File.Copy(Path.Combine(home.Root, file), alias == "nested" ? Path.Combine(custom, file) : Path.Combine(home.Root, file.ToLowerInvariant()));
        else if (alias == "metadata-name-case")
        {
            var package = System.Xml.Linq.XDocument.Load(metadata);
            package.Root!.SetAttributeValue("Name", StandaloneVisaPackage.PackageName.ToLowerInvariant());
            package.Save(metadata);
        }
        else
        {
            var target = alias == "metadata-nested" ? Path.Combine(custom, "package.xml")
                : alias == "metadata-file-case" ? Path.Combine(Path.GetDirectoryName(metadata)!, "PACKAGE.XML")
                : Path.Combine(home.Root, "Packages", StandaloneVisaPackage.PackageName.ToLowerInvariant(), "package.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(metadata, target);
        }
        var before = Snapshot(home);
        await AssertBlocked(workspace, home, plan);
        AssertSnapshot(before, home);
    }

    [Theory]
    [InlineData("outside-link")]
    [InlineData("cycle")]
    public async Task Unsafe_claim_inspection_returns_actionable_readiness_without_following_links_or_changing_bytes(string kind)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Link fixtures require Linux; production Windows link privileges are environment dependent.");
        var (workspace, home, plan) = PrepareMockHome();
        var custom = Path.Combine(home.Root, "Plugins", "Custom"); Directory.CreateDirectory(custom);
        var before = Snapshot(home);
        var link = Path.Combine(custom, "linked");
        var target = kind == "cycle" ? home.Root : AuthoringBuildSnapshotTests.Temp();
        Directory.CreateSymbolicLink(link, target);
        var entries = Directory.GetFileSystemEntries(custom).Order(StringComparer.Ordinal).ToArray();
        await AssertBlocked(workspace, home, plan);
        Assert.Equal(target, new DirectoryInfo(link).LinkTarget);
        Assert.Equal(entries, Directory.GetFileSystemEntries(custom).Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(home.Root, path)));
    }

    [Fact]
    public async Task Unreadable_metadata_returns_unavailable_instead_of_escaping_into_VM_properties()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) Assert.Skip("Production launcher supports Linux and Windows.");
        var (workspace, home, plan) = PrepareMockHome();
        var custom = Path.Combine(home.Root, "Plugins", "Custom"); Directory.CreateDirectory(custom);
        var metadata = Path.Combine(custom, "package.xml");
        File.WriteAllText(metadata, "<Package Name=\"InstrumentComponents.OpenTap\" Version=\"0.1.1\"><Files /></Package>");
        var before = Snapshot(home);
        using (var held = new FileStream(metadata, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await AssertBlocked(workspace, home, plan);
        AssertSnapshot(before, home);
    }

    private static (AuthoringWorkspace Workspace, OpenTapHome Home, string Plan) PrepareMockHome()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root); workspace.Manifest.SchemaVersion = 2;
        workspace.Manifest.Package.Name = "Undeclared standalone claim control";
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var home = AuthoringBuildSnapshotTests.Home(workspace);
        File.WriteAllText(Path.Combine(home.Root, "FixtureTui.dll"), "TUI prerequisite presence fixture");
        Assert.False(StandaloneVisaReadiness.RequiresStandaloneReadiness(home));
        Assert.DoesNotContain(workspace.Manifest.Dependencies, dependency => dependency.Package.Equals(PublishedInstrumentComponents.PackageName, StringComparison.OrdinalIgnoreCase));
        return (workspace, home, Path.Combine(root, "sample.TapPlan"));
    }

    private static async Task AssertBlocked(AuthoringWorkspace workspace, OpenTapHome home, string plan)
    {
        Assert.True(StandaloneVisaReadiness.RequiresStandaloneReadiness(home));
        var assessment = StandaloneVisaReadiness.Assess(home); Assert.False(assessment.Available);
        Assert.DoesNotContain("optional", assessment.Reason!, StringComparison.OrdinalIgnoreCase);
        var blocker = AuthoringExternalTuiLauncher.Prerequisite(home.Root, plan, requiresInstrumentLibrary: false);
        Assert.False(string.IsNullOrWhiteSpace(blocker));
        Assert.True(blocker!.Contains("Prepare", StringComparison.OrdinalIgnoreCase) || blocker.Contains("repair", StringComparison.OrdinalIgnoreCase), blocker);
        var preferences = new AuthoringPreferencesStore(Path.Combine(workspace.Root, "preferences.json")); preferences.Load();
        var vm = new AuthoringWorkspaceViewModel(preferences: preferences) { OpenTapHomeOverride = home.Root }; vm.Open(workspace.Root);
        Assert.False(vm.RequiresInstrumentLibrary);
        Assert.DoesNotContain("optional", vm.StandaloneVisaReadinessText, StringComparison.OrdinalIgnoreCase);
        var started = false;
        var launcher = new AuthoringExternalTuiLauncher { TerminalStarted = _ => started = true };
        var error = await Assert.ThrowsAsync<AuthoringWorkspaceException>(() => launcher.LaunchAsync(home.Root, plan, requiresInstrumentLibrary: false));
        Assert.Equal(blocker, error.Message); Assert.False(started);
    }

    private static void CopyPayload(Stream archive, string entry, string target)
    {
        using (archive)
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Read))
        using (var source = zip.GetEntry(entry)!.Open())
        using (var destination = File.Create(target)) source.CopyTo(destination);
    }

    private static Dictionary<string, byte[]> Snapshot(OpenTapHome home) => Directory.EnumerateFiles(home.Root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);

    private static void AssertSnapshot(Dictionary<string, byte[]> before, OpenTapHome home)
    {
        var after = Snapshot(home);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }
}
