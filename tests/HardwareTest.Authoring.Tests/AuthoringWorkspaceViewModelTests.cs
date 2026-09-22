using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringWorkspaceViewModelTests
{
    [Fact]
    public void Workspace_presence_changes_after_open()
    {
        var vm = new AuthoringWorkspaceViewModel();
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);

        Assert.False(vm.HasWorkspace);
        vm.Open(Path.Combine(FindRepoRoot(), "plans", "opentap"));

        Assert.True(vm.HasWorkspace);
        Assert.Contains(nameof(vm.HasWorkspace), notifications);
    }

    [Fact]
    public void Open_template_lists_sample_program()
    {
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(Path.Combine(FindRepoRoot(), "plans", "opentap"));

        Assert.Contains(vm.Programs, p => p.PlanId == "sample");
        Assert.NotEmpty(vm.MeasureTree);
        Assert.Contains(vm.MeasureTree, line => line.Contains("VDC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sidecar_save_round_trips_display_name()
    {
        var root = CopyTemplateWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.SelectProgram("sample");
        vm.DisplayName = "CLI sidecar round-trip";
        vm.SaveSidecar();

        var reloaded = new AuthoringWorkspaceViewModel();
        reloaded.Open(root);
        reloaded.SelectProgram("sample");
        Assert.Equal("CLI sidecar round-trip", reloaded.DisplayName);
    }

    [Fact]
    public void Validation_requires_saving_edited_sidecar()
    {
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(CopyTemplateWorkspace());
        vm.SelectProgram("sample");
        vm.DisplayName = "Edited sample";

        Assert.True(vm.HasUnsavedChanges);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        vm.SaveSidecar();
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Sidecar_save_does_not_clear_unsaved_plan_edits()
    {
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(CopyTemplateWorkspace());
        vm.SelectProgram("sample");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);

        Assert.True(vm.HasUnsavedChanges);
        vm.SaveSidecar();
        Assert.True(vm.HasUnsavedChanges);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
    }

    [Fact]
    public void Saving_one_program_keeps_other_program_dirty()
    {
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(CopyTemplateWorkspace());
        vm.CreateProgram("new-program");
        vm.SelectProgram("sample");
        vm.DisplayName = "Edited sample";
        vm.SaveSidecar();

        Assert.True(vm.HasUnsavedChanges);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
    }

    [Fact]
    public void Validate_surfaces_contract_findings_when_sidecar_missing()
    {
        var root = CopyTemplateWorkspace();
        File.Delete(Path.Combine(root, "sample.program.json"));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        var report = vm.Validate(strict: true);
        Assert.True(report.HasErrors);
        Assert.Contains(vm.Findings, f => f.Severity == PlanContractSeverity.Error);
        Assert.Contains(vm.Findings, f => f.Code.Contains("SIDECAR", StringComparison.OrdinalIgnoreCase)
                                         || f.Message.Contains("sidecar", StringComparison.OrdinalIgnoreCase));
        var row = Assert.Single(vm.FindingRows, f => f.Code == PlanContractValidator.Codes.SidecarMissing);
        Assert.Equal("sample", row.ProgramId);
        Assert.True(row.CanOpenProgram);
        Assert.Equal("Plan-wide", row.Location);
    }

    [Fact]
    public void Findings_identify_each_affected_program()
    {
        var root = CopyTemplateWorkspace();
        File.Delete(Path.Combine(root, "sample.program.json"));
        File.Delete(Path.Combine(root, "board-demo.program.json"));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.Validate();

        Assert.Contains(vm.FindingRows, row => row.ProgramId == "sample" && row.CanOpenProgram);
        Assert.Contains(vm.FindingRows, row => row.ProgramId == "board-demo" && row.CanOpenProgram);
    }

    [Fact]
    public void Open_failure_is_reportable_without_closing_the_session()
    {
        var vm = new AuthoringWorkspaceViewModel();
        var missing = Path.Combine(Path.GetTempPath(), "ht-missing-" + Guid.NewGuid().ToString("N"));
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => vm.Open(missing));
        vm.ReportError(ex.Message);
        Assert.Equal(ex.Message, vm.Error);
        Assert.Null(vm.Workspace);
    }

    private static string CopyTemplateWorkspace()
    {
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        var dest = Path.Combine(Path.GetTempPath(), "ht-authoring-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        }

        return dest;
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
