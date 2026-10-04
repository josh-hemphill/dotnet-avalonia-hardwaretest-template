using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringNumericFieldRecoveryTests
{
    [Theory]
    [InlineData("numerator", "0.5 1e-", "0.25 0.75")]
    [InlineData("denominator", "1 -", "1 -0.5")]
    [InlineData("period", "1e-", "0.25")]
    [InlineData("age", "1e-", "12")]
    public void IncompleteFieldsSaveReopenAndCorrectInOneUndoEntry(string field, string invalid, string corrected)
    {
        var root = Workspace();
        var vm = new AuthoringWorkspaceViewModel();
        var reopened = new AuthoringWorkspaceViewModel();
        try
        {
            vm.Open(root);
            vm.ApplyRecipe(AuthoringRecipeIds.TransferFunction);
            vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
            vm.StationHealthMaxAgeHours = "24";
            var id = vm.SelectedProgram.PlanId;
            var nodeId = vm.SelectedSequence!.NodeId;
            var baseline = TypedValue(vm, field);
            SetText(vm, field, invalid);
            Assert.Equal(invalid, Text(vm, field));
            Assert.Equal(baseline, TypedValue(vm, field));
            vm.SaveProgram(id);
            Assert.False(vm.HasUnsavedChanges);
            Assert.False(vm.CanPack);
            vm.StopRecovery();

            reopened.Open(root);
            reopened.SelectProgram(id);
            reopened.SelectSequence(reopened.SequenceItems.ToList().FindIndex(row => row.NodeId == nodeId));
            Assert.Equal(invalid, Text(reopened, field));
            Assert.Equal(baseline, TypedValue(reopened, field));
            var revision = reopened.SelectedDocument!.Revision;
            SetText(reopened, field, corrected);
            Assert.Equal(revision + 1, reopened.SelectedDocument.Revision);
            Assert.Equal(corrected, Text(reopened, field));
            Assert.Empty(reopened.SelectedProgram!.AuthoringState.IncompleteNumericText);
            reopened.Undo();
            Assert.Equal(invalid, Text(reopened, field));
            Assert.Equal(baseline, TypedValue(reopened, field));
            Assert.False(reopened.HasUnsavedChanges);
            reopened.Redo();
            Assert.Equal(corrected, Text(reopened, field));
            reopened.SaveProgram(id);
        }
        finally
        {
            vm.StopRecovery();
            reopened.StopRecovery();
        }
    }

    [Theory]
    [InlineData("numerator")]
    [InlineData("denominator")]
    [InlineData("period")]
    [InlineData("age")]
    public void ClearingTextKeepsRequiredFieldsIncompleteAndOptionalAgeClearsInOneEdit(string field)
    {
        var root = Workspace();
        var vm = new AuthoringWorkspaceViewModel();
        try
        {
            vm.Open(root);
            vm.ApplyRecipe(AuthoringRecipeIds.TransferFunction);
            vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
            vm.StationHealthMaxAgeHours = "24";
            var baseline = TypedValue(vm, field);
            SetText(vm, field, "1e-");
            var revision = vm.SelectedDocument!.Revision;
            SetText(vm, field, "");
            Assert.Equal(revision + 1, vm.SelectedDocument.Revision);
            if (field == "age") Assert.Empty(vm.SelectedProgram!.AuthoringState.IncompleteNumericText);
            else Assert.Equal("", Assert.Single(vm.SelectedProgram!.AuthoringState.IncompleteNumericText).Value);
            Assert.Equal(field == "age" ? "" : baseline, TypedValue(vm, field));
            vm.Undo();
            Assert.Equal("1e-", Text(vm, field));
            Assert.Equal(baseline, TypedValue(vm, field));
            vm.Redo();
            if (field == "age") Assert.Empty(vm.SelectedProgram!.AuthoringState.IncompleteNumericText);
            else Assert.Equal("", Assert.Single(vm.SelectedProgram!.AuthoringState.IncompleteNumericText).Value);
        }
        finally { vm.StopRecovery(); }
    }

    [Theory]
    [InlineData("numerator", "")]
    [InlineData("denominator", "")]
    [InlineData("period", "")]
    [InlineData("numerator", "  ")]
    [InlineData("denominator", "  ")]
    [InlineData("period", "  ")]
    public void BlankRequiredTfFieldsPersistExactlyAndBlockCompileUntilCorrected(string field, string blank)
    {
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        vm.ApplyRecipe(AuthoringRecipeIds.TransferFunction); vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        var id = vm.SelectedProgram.PlanId; var nodeId = vm.SelectedSequence!.NodeId;
        var baseline = TypedValue(vm, field);
        var path = vm.Workspace!.TapPlanPaths.Single(p => Path.GetFileNameWithoutExtension(p) == id);
        var compiledBytes = File.ReadAllBytes(path);
        SetText(vm, field, blank);
        Assert.Equal(blank, Text(vm, field)); Assert.Equal(baseline, TypedValue(vm, field));
        vm.SaveProgram(id);
        Assert.True(vm.HasUncompiledSources); Assert.False(vm.CanPack); Assert.Equal(compiledBytes, File.ReadAllBytes(path));
        vm.StopRecovery();
        var reopened = new AuthoringWorkspaceViewModel(); reopened.Open(root); reopened.SelectProgram(id);
        reopened.SelectSequence(reopened.SequenceItems.ToList().FindIndex(row => row.NodeId == nodeId));
        Assert.Equal(blank, Text(reopened, field)); Assert.Equal(baseline, TypedValue(reopened, field));
        Assert.Throws<AuthoringWorkspaceException>(() => reopened.Validate());
        SetText(reopened, field, baseline);
        Assert.Empty(reopened.SelectedProgram!.AuthoringState.IncompleteNumericText);
        reopened.Undo(); Assert.Equal(blank, Text(reopened, field)); Assert.Equal(baseline, TypedValue(reopened, field));
        reopened.Redo(); Assert.Empty(reopened.SelectedProgram!.AuthoringState.IncompleteNumericText);
        reopened.StopRecovery();
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    public void PositiveScalarFieldsPreserveNonFiniteAndNonPositiveInput(string text)
    {
        var root = Workspace();
        var vm = new AuthoringWorkspaceViewModel();
        try
        {
            vm.Open(root);
            vm.ApplyRecipe(AuthoringRecipeIds.TransferFunction);
            vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
            var period = TypedValue(vm, "period");
            vm.TfTsSeconds = text;
            vm.StationHealthMaxAgeHours = text;
            Assert.Equal(text, vm.TfTsSeconds);
            Assert.Equal(text, vm.StationHealthMaxAgeHours);
            Assert.Equal(period, TypedValue(vm, "period"));
            Assert.Null(vm.SelectedProgram!.Sidecar.StationHealthMaxAgeHours);
        }
        finally { vm.StopRecovery(); }
    }

    private static string Text(AuthoringWorkspaceViewModel vm, string field) => field switch
    {
        "numerator" => vm.TfNumerator,
        "denominator" => vm.TfDenominator,
        "period" => vm.TfTsSeconds,
        _ => vm.StationHealthMaxAgeHours,
    };

    private static void SetText(AuthoringWorkspaceViewModel vm, string field, string text)
    {
        switch (field)
        {
            case "numerator": vm.TfNumerator = text; break;
            case "denominator": vm.TfDenominator = text; break;
            case "period": vm.TfTsSeconds = text; break;
            default: vm.StationHealthMaxAgeHours = text; break;
        }
    }

    private static string TypedValue(AuthoringWorkspaceViewModel vm, string field)
    {
        var tf = (TransferFunctionAlgorithm)vm.SelectedMetric!.Source;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return field switch
        {
            "numerator" => string.Join(" ", tf.Numerator.Select(n => n.ToString(invariant))),
            "denominator" => string.Join(" ", tf.Denominator.Select(n => n.ToString(invariant))),
            "period" => tf.TsSeconds.ToString(invariant),
            _ => vm.SelectedProgram!.Sidecar.StationHealthMaxAgeHours?.ToString(invariant) ?? "",
        };
    }

    private static string Workspace()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        var root = Path.Combine(Path.GetTempPath(), "authoring-numeric-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, "plans", "opentap")))
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        return root;
    }
}
