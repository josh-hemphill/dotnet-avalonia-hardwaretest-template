using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
        "vm:LiveSeriesItemViewModel",
        "vm:ChartTimeWindow",
        "pres:PresentationTileViewModel",
    ];

    [Fact]
    public void Every_page_level_binding_resolves_on_RunTestViewModel()
    {
        var markup = ConcatRunBoardMarkup();
        var paths = PageLevelBindingRoots(markup).ToArray();

        // Guard against a silently-empty scrape making this assertion vacuous.
        Assert.True(paths.Length > 40, $"Expected the view to declare many bindings; found {paths.Length}.");
        Assert.Contains("Workspace.ShowSteps", paths);
        Assert.Contains("StepTree.StepListItems", paths);

        var unresolved = paths
            .Where(path => !Resolves(typeof(RunTestViewModel), path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unresolved.Length == 0,
            $"Run board AXAML binds paths that do not exist on RunTestViewModel: {string.Join(", ", unresolved)}");
    }

    private static string ConcatRunBoardMarkup()
    {
        var root = FindRunTestDirectory();
        return string.Concat(
            Directory.EnumerateFiles(root, "*.axaml")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string FindRunTestDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "HardwareTest", "Features", "RunTest");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"RunTest views not found above '{AppContext.BaseDirectory}'.");
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

    /// Item templates and typed container styles resolve bindings on their item, not the page.
    private static IEnumerable<string> SplitOffItemTemplates(string markup)
    {
        var document = XDocument.Parse("<Root>" + markup + "</Root>");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        document.Descendants().Where(element => element.Name.LocalName is "DataTemplate" or "Style"
            && TemplateDataTypes.Contains((string?)element.Attribute(xaml + "DataType"), StringComparer.Ordinal))
            .ToArray().Remove();
        yield return document.ToString(SaveOptions.DisableFormatting);
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

    [GeneratedRegex("""\{Binding\s+(?<path>[^,}]*)""")]
    private static partial Regex BindingRegex();
}
