using System.Text;
using System.Text.Json;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class PlanCompilerPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ht-persistence-" + Guid.NewGuid().ToString("N"));

    public PlanCompilerPersistenceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Save_restores_original_bytes_when_sidecar_replacement_fails_after_plan_changed()
    {
        var path = PlanPath();
        var sidecarPath = PlanCompiler.SidecarPath(path);
        var tapBytes = Encoding.UTF8.GetBytes("original plan bytes\r\n");
        var sidecarBytes = Encoding.UTF8.GetBytes("{ \"displayName\": \"original\" }\r\n");
        File.WriteAllBytes(path, tapBytes);
        File.WriteAllBytes(sidecarPath, sidecarBytes);
        var failure = new IOException("Injected final sidecar replacement failure.");
        var replacements = new List<string>();
        var compiler = new PlanCompiler(null, (source, destination) =>
        {
            replacements.Add(destination);
            if (destination == sidecarPath)
            {
                Assert.NotEqual(tapBytes, File.ReadAllBytes(path));
                Assert.Contains("replacement", File.ReadAllText(path), StringComparison.Ordinal);
                Assert.Equal(sidecarBytes, File.ReadAllBytes(sidecarPath));
                throw failure;
            }

            File.Move(source, destination, overwrite: true);
        });

        Assert.Same(failure, Assert.Throws<IOException>(() => compiler.Save(Draft(), path)));

        Assert.Equal([path, sidecarPath], replacements);
        Assert.Equal(tapBytes, File.ReadAllBytes(path));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(sidecarPath));
        AssertNoTemps(path);
    }

    [Fact]
    public void Save_retains_durable_preimages_and_original_error_when_restoration_fails()
    {
        var path = PlanPath();
        var sidecarPath = PlanCompiler.SidecarPath(path);
        var tapBytes = Encoding.UTF8.GetBytes("recoverable original plan\r\n");
        var sidecarBytes = Encoding.UTF8.GetBytes("recoverable original sidecar\r\n");
        File.WriteAllBytes(path, tapBytes);
        File.WriteAllBytes(sidecarPath, sidecarBytes);
        var failure = new IOException("Original sidecar replacement failure.");
        var compiler = new PlanCompiler(null, (source, destination) =>
        {
            if (destination == sidecarPath)
            {
                File.Delete(path);
                Directory.CreateDirectory(path);
                File.WriteAllText(sidecarPath, "partial replacement");
                throw failure;
            }
            File.Move(source, destination, overwrite: true);
        });

        Assert.Same(failure, Assert.Throws<IOException>(() => compiler.Save(Draft(), path)));

        var backups = Assert.IsType<string[]>(failure.Data["AuthoringRecoveryBackups"]);
        Assert.Equal(2, backups.Length);
        Assert.Equal(tapBytes, File.ReadAllBytes(backups.Single(p => p.StartsWith(path + ".", StringComparison.Ordinal))));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(backups.Single(p => p.StartsWith(sidecarPath + ".", StringComparison.Ordinal))));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(sidecarPath));
        Assert.NotEmpty(Assert.IsType<Exception[]>(failure.Data["AuthoringRollbackErrors"]));
        Assert.Contains("Restore", Assert.IsType<string>(failure.Data["AuthoringRecoveryAction"]), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_directory, "*.restoring"));
        AssertNoTemps(path);
    }

    [Fact]
    public void Save_removes_new_plan_when_final_sidecar_replacement_fails()
    {
        var path = PlanPath();
        var sidecarPath = PlanCompiler.SidecarPath(path);
        var failure = new IOException("Injected final sidecar replacement failure.");
        var compiler = new PlanCompiler(null, (source, destination) =>
        {
            if (destination == sidecarPath)
            {
                Assert.True(File.Exists(path));
                Assert.False(File.Exists(sidecarPath));
                throw failure;
            }

            File.Move(source, destination, overwrite: true);
        });

        Assert.Same(failure, Assert.Throws<IOException>(() => compiler.Save(Draft(), path)));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(sidecarPath));
        AssertNoTemps(path);
    }

    [Fact]
    public void Save_leaves_original_bytes_when_plan_replacement_fails()
    {
        var path = PlanPath();
        var sidecarPath = PlanCompiler.SidecarPath(path);
        var tapBytes = Encoding.UTF8.GetBytes("original plan");
        var sidecarBytes = Encoding.UTF8.GetBytes("original sidecar");
        File.WriteAllBytes(path, tapBytes);
        File.WriteAllBytes(sidecarPath, sidecarBytes);
        var failure = new IOException("Injected first replacement failure.");
        var compiler = new PlanCompiler(null, (source, destination) =>
        {
            Assert.Equal(path, destination);
            Assert.True(File.Exists(source));
            Assert.True(File.Exists(sidecarPath + ".saving"));
            throw failure;
        });

        Assert.Same(failure, Assert.Throws<IOException>(() => compiler.Save(Draft(), path)));

        Assert.Equal(tapBytes, File.ReadAllBytes(path));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(sidecarPath));
        AssertNoTemps(path);
    }

    [Fact]
    public void SaveSidecar_preserves_original_bytes_when_replacement_fails()
    {
        var path = PlanPath();
        var sidecarPath = PlanCompiler.SidecarPath(path);
        var originalBytes = Encoding.UTF8.GetBytes("{ \"displayName\": \"original\" }\r\n");
        File.WriteAllBytes(sidecarPath, originalBytes);
        var failure = new IOException("Injected sidecar replacement failure.");
        var compiler = new PlanCompiler(null, (source, destination) =>
        {
            Assert.Equal(sidecarPath, destination);
            Assert.Equal(ExpectedSidecar(Draft().Sidecar), File.ReadAllBytes(source));
            Assert.Equal(originalBytes, File.ReadAllBytes(destination));
            throw failure;
        });

        Assert.Same(failure, Assert.Throws<IOException>(() => compiler.SaveSidecar(path, Draft().Sidecar)));

        Assert.Equal(originalBytes, File.ReadAllBytes(sidecarPath));
        Assert.False(File.Exists(path));
        AssertNoTemps(path);
    }

    [Fact]
    public void SaveSidecar_preserves_original_bytes_when_temp_cannot_be_written()
    {
        var path = PlanPath();
        var sidecarPath = PlanCompiler.SidecarPath(path);
        var originalBytes = Encoding.UTF8.GetBytes("original sidecar bytes\r\n");
        File.WriteAllBytes(sidecarPath, originalBytes);
        Directory.CreateDirectory(sidecarPath + ".saving");
        var replacementRan = false;
        var compiler = new PlanCompiler(null, (_, _) => replacementRan = true);

        Assert.ThrowsAny<Exception>(() => compiler.SaveSidecar(path, Draft().Sidecar));

        Assert.False(replacementRan);
        Assert.Equal(originalBytes, File.ReadAllBytes(sidecarPath));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(sidecarPath + ".saving"));
        Assert.True(Directory.Exists(sidecarPath + ".saving"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Temp_cleanup_failure_does_not_hide_replacement_failure(bool sidecarOnly)
    {
        var path = PlanPath();
        var sidecarPath = PlanCompiler.SidecarPath(path);
        var failure = new IOException("Primary replacement failure.");
        var compiler = new PlanCompiler(null, (source, destination) =>
        {
            if (destination == sidecarPath)
            {
                File.Delete(source);
                Directory.CreateDirectory(source);
                throw failure;
            }

            File.Move(source, destination, overwrite: true);
        });

        var actual = Assert.Throws<IOException>(() =>
        {
            if (sidecarOnly)
            {
                compiler.SaveSidecar(path, Draft().Sidecar);
            }
            else
            {
                compiler.Save(Draft(), path);
            }
        });

        Assert.Same(failure, actual);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(sidecarPath));
        Assert.False(File.Exists(path + ".saving"));
        Assert.True(Directory.Exists(sidecarPath + ".saving"));
    }

    [Fact]
    public void Save_keeps_established_sidecar_serialization_and_loadable_plan()
    {
        var path = PlanPath();
        var draft = Draft();
        var compiler = new PlanCompiler();
        var originalSidecar = ExpectedSidecar(draft.Sidecar);

        compiler.Save(draft, path);

        Assert.Equal(originalSidecar, ExpectedSidecar(draft.Sidecar));
        var persistedSidecar = PlanCompiler.CloneSidecar(draft.Sidecar);
        AuthoringCleanup.SyncSidecar(persistedSidecar, draft.Cleanup);
        Assert.Equal(ExpectedSidecar(persistedSidecar), File.ReadAllBytes(PlanCompiler.SidecarPath(path)));
        Assert.Equal("replacement", compiler.Load(path).Sidecar.DisplayName);
        AssertNoTemps(path);
    }

    [Fact]
    public void SaveSidecar_keeps_established_serialization_and_plan_bytes()
    {
        var path = PlanPath();
        var tapBytes = Encoding.UTF8.GetBytes("untouched plan bytes");
        File.WriteAllBytes(path, tapBytes);
        File.WriteAllText(PlanCompiler.SidecarPath(path), "old sidecar");
        var sidecar = Draft().Sidecar;

        new PlanCompiler().SaveSidecar(path, sidecar);

        Assert.Equal(ExpectedSidecar(sidecar), File.ReadAllBytes(PlanCompiler.SidecarPath(path)));
        Assert.Equal(tapBytes, File.ReadAllBytes(path));
        AssertNoTemps(path);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string PlanPath() => Path.Combine(_directory, "atomic.TapPlan");

    private static ProgramDraft Draft()
    {
        var draft = AuthoringRecipeCatalog.Apply(
            AuthoringRecipeCatalog.CreateProgram("atomic"), AuthoringRecipeIds.Acquire);
        draft.Sidecar.DisplayName = "replacement";
        return draft;
    }

    private static byte[] ExpectedSidecar(ProgramSidecar sidecar)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar));

    private static void AssertNoTemps(string path)
    {
        Assert.False(Path.Exists(path + ".saving"));
        Assert.False(Path.Exists(PlanCompiler.SidecarPath(path) + ".saving"));
    }
}
