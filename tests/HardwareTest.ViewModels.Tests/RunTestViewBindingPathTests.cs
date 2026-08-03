using System.Reflection;
using System.Text.RegularExpressions;
using HardwareTest.Features.RunTest;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Phase 9 guard: RunTestView binds through child paths, and these bindings are not compile-checked.
public sealed partial class RunTestViewBindingPathTests
{
    /// Templated item types whose bindings resolve against the item, not the page ViewModel.
    private static readonly string[] TemplateDataTypes =
    [
        "vm:ProgramItemViewModel",
        "vm:InteractionFieldViewModel",
        "vm:StepListItemViewModel",
        "vm:StageItemViewModel",
        "pres:PresentationTileViewModel",
    ];

    [Fact]
    public void Every_page_level_binding_resolves_on_RunTestViewModel()
    {
        var markup = File.ReadAllText(FindViewPath());
        var paths = PageLevelBindingRoots(markup).ToArray();

        // Guard against a silently-empty scrape making this assertion vacuous.
        Assert.True(paths.Length > 40, $"Expected the view to declare many bindings; found {paths.Length}.");
        Assert.Contains("StepDetail.DetailLines", paths);
        Assert.Contains("StepTree.StepListItems", paths);

        var unresolved = paths
            .Where(path => !Resolves(typeof(RunTestViewModel), path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unresolved.Length == 0,
            $"RunTestView.axaml binds paths that do not exist on RunTestViewModel: {string.Join(", ", unresolved)}");
    }

    /// Yields binding paths declared outside item DataTemplates, stripped of `!` and converter arguments.
    private static IEnumerable<string> PageLevelBindingRoots(string markup)
    {
        foreach (var region in SplitOffItemTemplates(markup))
        {
            foreach (Match match in BindingRegex().Matches(region))
            {
                var path = match.Groups["path"].Value.Trim().TrimStart('!');
                if (path.Length > 0 && !path.StartsWith('$') && !path.StartsWith('#'))
                {
                    yield return path;
                }
            }
        }
    }

    /// Drops each `<DataTemplate x:DataType="...item...">…</DataTemplate>` body so only page bindings remain.
    private static IEnumerable<string> SplitOffItemTemplates(string markup)
    {
        var remaining = markup;
        foreach (var dataType in TemplateDataTypes)
        {
            remaining = Regex.Replace(
                remaining,
                $"""<DataTemplate\s+x:DataType="{Regex.Escape(dataType)}".*?</DataTemplate>""",
                string.Empty,
                RegexOptions.Singleline);
        }

        yield return remaining;
    }

    private static bool Resolves(Type root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            var property = current.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
            {
                return false;
            }

            current = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }

        return true;
    }

    private static string FindViewPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src",
                "HardwareTest",
                "Features",
                "RunTest",
                "RunTestView.axaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"RunTestView.axaml not found above '{AppContext.BaseDirectory}'.");
    }

    [GeneratedRegex("""\{Binding\s+(?<path>[^,}]*)""")]
    private static partial Regex BindingRegex();
}
