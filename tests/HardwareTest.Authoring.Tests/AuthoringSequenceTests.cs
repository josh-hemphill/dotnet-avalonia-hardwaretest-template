using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringSequenceTests
{
    [Fact]
    public void Flatten_sections_setup_measure_cleanup_without_a_tree()
    {
        var draft = AuthoringRecipeCatalog.CreateProgram("seq");
        draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Prompt);
        draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Acquire);
        draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.MeanGte);
        draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Repeat);

        var rows = AuthoringSequence.Flatten(draft);
        Assert.Equal(
            new[]
            {
                SequenceSection.Setup,
                SequenceSection.Setup,
                SequenceSection.Setup,
                SequenceSection.Measure,
                SequenceSection.Measure,
                SequenceSection.Measure,
                SequenceSection.Measure,
                SequenceSection.Cleanup,
                SequenceSection.Cleanup,
            },
            rows.Select(row => row.Section).ToArray());
        Assert.Equal(AuthoringChrome.SetupHeader, rows[0].Label);
        Assert.False(rows[0].IsSelectable);
        Assert.Contains(rows, row => row.Kind == SequenceRowKind.Setup && row.Label == "Identity Check");
        Assert.Contains(rows, row => row.Kind == SequenceRowKind.Setup && row.Label == "Operator Prompt");
        var measureHeader = Assert.Single(rows, row => row is { Kind: SequenceRowKind.Header, Label: AuthoringChrome.MeasureHeader });
        Assert.True(rows.ToList().IndexOf(measureHeader) > rows.ToList().FindIndex(row => row.Kind == SequenceRowKind.Setup));
        var repeat = Assert.Single(rows, row => row.Kind == SequenceRowKind.Repeat);
        Assert.StartsWith("Repeat x", repeat.Label, StringComparison.Ordinal);
        var child = Assert.Single(rows, row => row.Kind == SequenceRowKind.Metric && row.Label.Contains("Mean GTE", StringComparison.Ordinal));
        Assert.True(child.IsSelectable);
        Assert.Equal(1, child.Depth);
        Assert.Equal(AuthoringSequence.IndentPerDepth, child.Indent);
        Assert.Equal(new[] { 1, 0 }, child.IndexPath);
        Assert.Contains(rows, row => row.Kind == SequenceRowKind.Cleanup && row.Label == "Safe Shutdown");
        Assert.DoesNotContain(rows, row => row.Kind == SequenceRowKind.Header && row.IsSelectable);
    }

    [Fact]
    public void MutateMeasure_updates_nested_child_only()
    {
        var acquire = AuthoringRecipeCatalog.Apply(
            AuthoringRecipeCatalog.CreateProgram("seq"),
            AuthoringRecipeIds.Acquire);
        var wrapped = AuthoringRecipeCatalog.Apply(acquire, AuthoringRecipeIds.Repeat);
        var mutated = AuthoringSequence.MutateMeasure(
            wrapped.Measure,
            [0, 0],
            node => node is MetricNode metric
                ? new MetricNode(metric.Metric with { ChannelKey = "nested" })
                : node);
        var repeat = Assert.IsType<RepeatNode>(Assert.Single(mutated));
        Assert.Equal("nested", Assert.IsType<MetricNode>(Assert.Single(repeat.Children)).Metric.ChannelKey);
    }

    [Fact]
    public void Chrome_copy_names_the_three_program_columns()
    {
        Assert.Equal("Sequence", AuthoringChrome.SequenceTitle);
        Assert.Contains("Setup", AuthoringChrome.SequencePurpose, StringComparison.Ordinal);
        Assert.Equal("Inspector", AuthoringChrome.InspectorTitle);
        Assert.Contains("not the whole program", AuthoringChrome.InspectorPurpose, StringComparison.Ordinal);
        Assert.Equal("Operator preview", AuthoringChrome.PreviewTitle);
        Assert.Equal("Program settings", AuthoringChrome.ProgramSettingsTitle);
        Assert.Equal("Remove selected", AuthoringChrome.RemoveSelectedTitle);
        Assert.Contains("Repeat unwraps its children", AuthoringChrome.RemoveSelectedPurpose, StringComparison.Ordinal);
        Assert.Equal("Remove program", AuthoringChrome.RemoveProgramTitle);
        Assert.Contains("Deletes its TapPlan and sidecar", AuthoringChrome.RemoveProgramPurpose, StringComparison.Ordinal);
        Assert.Equal("Measure — Acquire Voltage", new AuthoringRecipe("acquire", "Acquire Voltage", "Measure", "x").ListLabel);
    }

    [Fact]
    public void RemoveMeasure_drops_a_leaf_and_unwraps_repeat()
    {
        var acquire = AuthoringRecipeCatalog.Apply(
            AuthoringRecipeCatalog.CreateProgram("seq"),
            AuthoringRecipeIds.Acquire);
        var mean = AuthoringRecipeCatalog.Apply(acquire, AuthoringRecipeIds.MeanGte);
        var wrapped = AuthoringRecipeCatalog.Apply(mean, AuthoringRecipeIds.Repeat);
        var withoutChild = AuthoringSequence.RemoveMeasure(wrapped.Measure, [1, 0]);
        Assert.Equal(2, withoutChild.Count);
        Assert.IsType<MetricNode>(withoutChild[0]);
        Assert.Empty(Assert.IsType<RepeatNode>(withoutChild[1]).Children);

        var unwrapped = AuthoringSequence.RemoveMeasure(wrapped.Measure, [1]);
        Assert.Equal(2, unwrapped.Count);
        Assert.IsType<MetricNode>(unwrapped[0]);
        Assert.IsType<MetricNode>(unwrapped[1]);
        Assert.Equal("VDC", Assert.IsType<MetricNode>(unwrapped[0]).Metric.ChannelKey);
        Assert.Equal("VDC.mean", Assert.IsType<MetricNode>(unwrapped[1]).Metric.ChannelKey);
    }
}

public sealed class AuthoringSequenceViewModelTests
{
    [Fact]
    public void New_program_sequence_shows_identity_and_cleanup_while_measure_is_empty()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("empty-measure");
        Assert.Equal(AuthoringChrome.EmptyMeasureHint, vm.MeasureHint);
        Assert.Contains(vm.SequenceItems, row => row.Label == "Identity Check");
        Assert.Contains(vm.SequenceItems, row => row.Label == AuthoringChrome.MeasureHeader);
        Assert.DoesNotContain(vm.SequenceItems, row => row.Section == SequenceSection.Measure && row.Kind == SequenceRowKind.Metric);
        Assert.Contains(vm.SequenceItems, row => row.Kind == SequenceRowKind.Cleanup);
        Assert.Equal("Identity Check", vm.SelectedSequence?.Label);
        Assert.IsType<IdentitySetup>(vm.SelectedSetup);
    }

    [Fact]
    public void Selecting_repeat_child_edits_that_metric_not_the_parent_row()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("repeat-child");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        var acquire = Assert.IsType<MetricNode>(vm.SelectedProgram!.Measure[0]);
        var mean = Assert.IsType<MetricNode>(vm.SelectedProgram.Measure[1]);
        vm.ReplaceSelected(vm.SelectedProgram with
        {
            Measure = [new RepeatNode(2, [acquire, mean])],
        });

        var first = vm.SequenceItems.Single(row =>
            row.Kind == SequenceRowKind.Metric && row.Detail.Contains("VDC", StringComparison.Ordinal)
            && !row.Detail.Contains("VDC.mean", StringComparison.Ordinal));
        var second = vm.SequenceItems.Single(row =>
            row.Kind == SequenceRowKind.Metric && row.Detail.Contains("VDC.mean", StringComparison.Ordinal));
        Assert.True(second.IsSelectable);
        Assert.Equal(1, second.Depth);
        Assert.NotEqual(first.Key, second.Key);
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(second));
        Assert.Equal(SequenceRowKind.Metric, vm.SelectedSequence?.Kind);
        Assert.Equal(second.Key, vm.SelectedSequence?.Key);
        Assert.Equal("VDC.mean", vm.ChannelKey);

        vm.ChannelKey = "rail.mean";
        Assert.Equal(second.Key, vm.SelectedSequence?.Key);
        var repeat = Assert.IsType<RepeatNode>(Assert.Single(vm.SelectedProgram!.Measure));
        Assert.Equal("VDC", Assert.IsType<MetricNode>(repeat.Children[0]).Metric.ChannelKey);
        Assert.Equal("rail.mean", Assert.IsType<MetricNode>(repeat.Children[1]).Metric.ChannelKey);
        Assert.NotEqual("rail.mean", Assert.IsType<MetricNode>(repeat.Children[0]).Metric.ChannelKey);
    }

    [Fact]
    public void SelectSequence_on_a_header_snaps_to_the_nearest_selectable_row()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("header-snap");
        var setupHeader = vm.SequenceItems.ToList().FindIndex(row =>
            row.Kind == SequenceRowKind.Header && row.Section == SequenceSection.Setup);
        var identity = vm.SequenceItems.ToList().FindIndex(row => row.Label == "Identity Check");
        Assert.True(setupHeader >= 0);
        vm.SelectSequence(setupHeader);
        Assert.Equal(identity, vm.SelectedSequenceIndex);
        Assert.True(vm.SelectedSequence?.IsSelectable);
        Assert.Equal("Identity Check", vm.SelectedSequence?.Label);

        var cleanupHeader = vm.SequenceItems.ToList().FindIndex(row =>
            row.Kind == SequenceRowKind.Header && row.Section == SequenceSection.Cleanup);
        var shutdown = vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Cleanup);
        vm.SelectSequence(cleanupHeader);
        Assert.Equal(shutdown, vm.SelectedSequenceIndex);
        Assert.Equal(SequenceRowKind.Cleanup, vm.SelectedSequence?.Kind);

        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        cleanupHeader = vm.SequenceItems.ToList().FindIndex(row =>
            row.Kind == SequenceRowKind.Header && row.Section == SequenceSection.Cleanup);
        shutdown = vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Cleanup);
        vm.SelectSequence(cleanupHeader);
        Assert.Equal(shutdown, vm.SelectedSequenceIndex);
        Assert.True(vm.HasCleanupEditor);
    }

    [Fact]
    public void Remove_selected_sequence_inverts_add_recipe()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("remove-seq");
        vm.ApplyRecipe(AuthoringRecipeIds.Prompt);
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        vm.ApplyRecipe(AuthoringRecipeIds.Repeat);

        var prompt = vm.SequenceItems.Single(row => row.Label == "Operator Prompt");
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(prompt));
        Assert.True(vm.CanRemoveSelectedSequence);
        vm.RemoveSelectedSequence();
        Assert.DoesNotContain(vm.SequenceItems, row => row.Label == "Operator Prompt");
        Assert.Contains(vm.SequenceItems, row => row.Label == "Identity Check");

        var measureHeader = vm.SequenceItems.Single(row =>
            row.Kind == SequenceRowKind.Header && row.Section == SequenceSection.Measure);
        Assert.False(AuthoringSequence.CanRemove(measureHeader, vm.SelectedProgram));

        var nestedMean = vm.SequenceItems.Single(row =>
            row.Kind == SequenceRowKind.Metric && row.Detail.Contains("VDC.mean", StringComparison.Ordinal));
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(nestedMean));
        vm.RemoveSelectedSequence();
        var leftoverRepeat = Assert.IsType<RepeatNode>(vm.SelectedProgram!.Measure.Single(node => node is RepeatNode));
        Assert.Empty(leftoverRepeat.Children);

        var repeatRow = vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Repeat);
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(repeatRow));
        vm.RemoveSelectedSequence();
        Assert.DoesNotContain(vm.SelectedProgram!.Measure, node => node is RepeatNode);
        var leftoverAcquire = Assert.IsType<MetricNode>(Assert.Single(vm.SelectedProgram.Measure));
        Assert.Equal("VDC", leftoverAcquire.Metric.ChannelKey);

        var shutdown = vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Cleanup);
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(shutdown));
        Assert.True(vm.CanRemoveSelectedSequence);
        vm.RemoveSelectedSequence();
        Assert.False(vm.SelectedProgram!.Cleanup.IncludeSafeShutdown);
        Assert.False(vm.CanRemoveSelectedSequence);
        vm.RemoveSelectedSequence();
        Assert.False(vm.SelectedProgram.Cleanup.IncludeSafeShutdown);
    }

    [Fact]
    public void Remove_selected_program_drops_session_and_saved_files()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("keep-me");
        vm.CreateProgram("drop-unsaved");
        Assert.True(vm.CanRemoveSelectedProgram);
        vm.RemoveSelectedProgram();
        Assert.DoesNotContain(vm.Programs, program => program.PlanId == "drop-unsaved");
        Assert.Equal("keep-me", vm.SelectedProgram?.PlanId);

        vm.Apply();
        var tap = Directory.EnumerateFiles(vm.Workspace!.Root, "keep-me.TapPlan", SearchOption.AllDirectories).Single();
        var sidecar = PlanCompiler.SidecarPath(tap);
        Assert.True(File.Exists(sidecar));
        vm.RemoveSelectedProgram();
        Assert.Empty(vm.Programs);
        Assert.Null(vm.SelectedProgram);
        Assert.False(File.Exists(tap));
        Assert.False(File.Exists(sidecar));
        Assert.False(vm.CanRemoveSelectedProgram);
    }

    [Fact]
    public void Remove_selected_program_keeps_the_neighbor()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("alpha");
        vm.CreateProgram("beta");
        vm.CreateProgram("gamma");

        vm.SelectProgram("beta");
        vm.RemoveSelectedProgram();
        Assert.Equal(["alpha", "gamma"], vm.Programs.Select(program => program.PlanId).ToArray());
        Assert.Equal("gamma", vm.SelectedProgram?.PlanId);

        vm.RemoveSelectedProgram();
        Assert.Equal(["alpha"], vm.Programs.Select(program => program.PlanId).ToArray());
        Assert.Equal("alpha", vm.SelectedProgram?.PlanId);

        vm.RemoveSelectedProgram();
        Assert.Null(vm.SelectedProgram);
        Assert.Empty(vm.Programs);
    }

    [Fact]
    public void Remove_selected_program_throws_when_workspace_is_read_only()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-ro-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        File.WriteAllText(
            Path.Combine(dest, "authoring.json"),
            """
            {
              "schemaVersion": 999,
              "displayName": "future",
              "plansDirectory": "."
            }
            """);

        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(dest);
        Assert.True(vm.Workspace!.IsReadOnly);
        vm.CreateProgram("locked");
        Assert.False(vm.CanRemoveSelectedProgram);
        var ex = Assert.Throws<AuthoringWorkspaceException>(vm.RemoveSelectedProgram);
        Assert.Contains("read-only", ex.Message, StringComparison.Ordinal);
        Assert.Equal("locked", vm.SelectedProgram?.PlanId);
    }

    [Fact]
    public void Repeat_count_edits_the_selected_repeat_row()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("repeat-count");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.Repeat);
        var repeatRow = vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Repeat);
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(repeatRow));
        Assert.Equal("2", vm.RepeatCount);
        vm.RepeatCount = "4";
        Assert.Equal(4, Assert.IsType<RepeatNode>(Assert.Single(vm.SelectedProgram!.Measure)).Count);
    }

    private static AuthoringWorkspaceViewModel OpenEmpty()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-seq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        File.Copy(Path.Combine(src, "authoring.json"), Path.Combine(dest, "authoring.json"));
        var schema = Path.Combine(src, "authoring.schema.json");
        if (File.Exists(schema))
        {
            File.Copy(schema, Path.Combine(dest, "authoring.schema.json"));
        }

        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(dest);
        return vm;
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
