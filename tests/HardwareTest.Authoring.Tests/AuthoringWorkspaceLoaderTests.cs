using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringWorkspaceLoaderTests
{
    [Fact]
    public void Load_template_workspace_lists_top_level_tap_plans_without_fixtures()
    {
        var root = Path.Combine(FindRepoRoot(), "plans", "opentap");
        var workspace = AuthoringWorkspaceLoader.Load(root);

        Assert.False(workspace.IsReadOnly);
        Assert.Equal(AuthoringSchemaVersions.Manifest, workspace.Manifest.SchemaVersion);
        Assert.Equal("HardwareTest Template Program", workspace.Manifest.Package.Name);
        Assert.Equal(".", workspace.Manifest.PlansDirectory);
        Assert.Equal(
            ["OpenTAP", "HardwareTest Basic", "HardwareTest Mixins"],
            workspace.Manifest.Dependencies.Select(d => d.Package).ToArray());
        Assert.DoesNotContain(
            workspace.Manifest.Dependencies,
            d => d.Package.Contains("InstrumentComponents", StringComparison.OrdinalIgnoreCase));

        var names = workspace.TapPlanPaths
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .ToArray();
        Assert.Contains("sample.TapPlan", names);
        Assert.Contains("board-demo.TapPlan", names);
        Assert.DoesNotContain(names, n => n.Contains("hang-forever", StringComparison.OrdinalIgnoreCase));
        var expectedDir = Path.GetFullPath(root);
        Assert.All(workspace.TapPlanPaths, path => Assert.Equal(expectedDir, Path.GetDirectoryName(path)));
        Assert.Equal("recordings", workspace.Manifest.RecordingsDirectory);
    }

    [Fact]
    public void Blank_plans_directory_defaults_to_plans()
    {
        Assert.Equal(AuthoringManifest.DefaultPlansDirectory, AuthoringManifest.RelativePlansDirectory(null));
        Assert.Equal(AuthoringManifest.DefaultPlansDirectory, AuthoringManifest.RelativePlansDirectory("  "));
        Assert.Equal("custom", AuthoringManifest.RelativePlansDirectory(" custom "));
    }

    [Fact]
    public void Load_rejects_missing_manifest()
    {
        var dir = NewTempDir();
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringWorkspaceLoader.Load(dir));
        Assert.Contains("Missing authoring.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_unknown_property()
    {
        var dir = NewTempDir();
        WriteManifest(
            dir,
            """
            {
              "schemaVersion": 1,
              "displayName": "x",
              "plansDirectory": ".",
              "extraField": true
            }
            """);
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringWorkspaceLoader.Load(dir));
        Assert.Contains("Unknown property 'extraField'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_missing_schema_version()
    {
        var dir = NewTempDir();
        WriteManifest(
            dir,
            """
            {
              "displayName": "x",
              "plansDirectory": "."
            }
            """);
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringWorkspaceLoader.Load(dir));
        Assert.Contains("schemaVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_future_schema_is_read_only_and_save_fails()
    {
        var dir = NewTempDir();
        WriteManifest(
            dir,
            """
            {
              "schemaVersion": 999,
              "displayName": "future",
              "plansDirectory": ".",
              "futureOnly": "ok"
            }
            """);

        var workspace = AuthoringWorkspaceLoader.Load(dir);
        Assert.True(workspace.IsReadOnly);
        Assert.Equal(999, workspace.Manifest.SchemaVersion);

        var ex = Assert.Throws<AuthoringWorkspaceException>(
            () => AuthoringWorkspaceLoader.SaveManifest(dir, workspace.Manifest));
        Assert.Contains("future-schema", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_stamps_current_schema_and_round_trips()
    {
        var dir = NewTempDir();
        var manifest = new AuthoringManifest
        {
            DisplayName = "Round trip",
            PlansDirectory = ".",
            Package = new AuthoringPackageSpec
            {
                Name = "RoundTrip Programs",
                Version = "1.2.3",
            },
            Dependencies =
            [
                new AuthoringPackageDependency { Package = "OpenTAP", Version = "^9.32.2" },
            ],
            IncludeTui = true,
        };

        AuthoringWorkspaceLoader.SaveManifest(dir, manifest);
        var loaded = AuthoringWorkspaceLoader.Load(dir);
        Assert.Equal(AuthoringSchemaVersions.Manifest, loaded.Manifest.SchemaVersion);
        Assert.Equal("Round trip", loaded.Manifest.DisplayName);
        Assert.Equal("RoundTrip Programs", loaded.Manifest.Package.Name);
        Assert.Empty(loaded.TapPlanPaths);

        File.WriteAllText(Path.Combine(dir, "only.TapPlan"), "<TestPlan />");
        loaded = AuthoringWorkspaceLoader.Load(dir);
        Assert.Equal("only.TapPlan", Path.GetFileName(loaded.TapPlanPaths.Single()));
    }

    [Fact]
    public void Save_round_trips_workspace_catalogs()
    {
        var dir = NewTempDir();
        AuthoringWorkspaceLoader.SaveManifest(
            dir,
            new AuthoringManifest
            {
                DisplayName = "Catalogs",
                PlansDirectory = ".",
                Catalogs = new AuthoringWorkspaceCatalogs
                {
                    ReportKinds = ["traceability"],
                    ProgramKinds = ["incomingInspect"],
                    InstrumentSlotNames = ["SCOPE"],
                    RequiredFields = ["fixtureId"],
                },
            });
        var loaded = AuthoringWorkspaceLoader.Load(dir);
        Assert.Equal(["traceability"], loaded.Manifest.Catalogs!.ReportKinds);
        Assert.Equal(["incomingInspect"], loaded.Manifest.Catalogs.ProgramKinds);
        Assert.Equal(["SCOPE"], loaded.Manifest.Catalogs.InstrumentSlotNames);
        Assert.Equal(["fixtureId"], loaded.Manifest.Catalogs.RequiredFields);
    }

    [Fact]
    public void Save_does_not_write_future_schema_version()
    {
        var dir = NewTempDir();
        var ex = Assert.Throws<AuthoringWorkspaceException>(
            () => AuthoringWorkspaceLoader.SaveManifest(
                dir,
                new AuthoringManifest { SchemaVersion = 2, PlansDirectory = "." }));
        Assert.Contains("Cannot write", ex.Message, StringComparison.Ordinal);
    }

    private static void WriteManifest(string dir, string json)
        => File.WriteAllText(Path.Combine(dir, AuthoringWorkspaceLoader.ManifestFileName), json);

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("HardwareTest.slnx").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate HardwareTest.slnx above '{AppContext.BaseDirectory}'.");
    }
}
