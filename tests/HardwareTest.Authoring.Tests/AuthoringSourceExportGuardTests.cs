using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringSourceExportGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-source-export-" + Guid.NewGuid().ToString("N"));
    public AuthoringSourceExportGuardTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "authoring.json"), """{"schemaVersion":2,"plansDirectory":"."}""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cli_and_core_pack_refuse_stale_or_uncompiled_saved_sources(bool externalChange)
    {
        var path = Path.Combine(_root, "sample.TapPlan");
        File.WriteAllText(path, "compiled baseline");
        File.WriteAllText(Path.Combine(_root, "sample.program.json"), "{}");
        var document = AuthoringDocumentDto.FromDraft(AuthoringRecipeCatalog.CreateProgram("sample"),
            compiledPlanHash: AuthoringDocumentStore.ComputeHash(path),
            compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)));
        document.RequiresCompilation = !externalChange;
        new AuthoringDocumentStore(_root).Save(document);
        if (externalChange) File.WriteAllText(path, "externally changed compiled file");
        var expected = externalChange ? "SOURCE_EXTERNAL_CONFLICT" : "SOURCE_COMPILE_REQUIRED";
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, AuthoringCli.Run(["--validate", _root], output, error));
        Assert.Contains(expected, error.ToString());
        var result = WorkspacePacker.Preflight(AuthoringWorkspaceLoader.Load(_root), new PackOptions { Offline = true });
        Assert.Contains(result.Findings, finding => finding.Code == expected && finding.IsError);
        Assert.Null(result.Contract);
        Assert.False(Directory.Exists(Path.Combine(_root, ".authoring", "opentap")));
    }

    [Fact]
    public void Source_without_a_compiled_plan_cannot_be_silently_omitted_from_export()
    {
        new AuthoringDocumentStore(_root).Save(AuthoringDocumentDto.FromDraft(AuthoringRecipeCatalog.CreateProgram("new-plan")));
        var result = WorkspacePacker.Preflight(AuthoringWorkspaceLoader.Load(_root), new PackOptions());
        Assert.Contains(result.Findings, finding => finding.Code == "SOURCE_COMPILE_REQUIRED");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
