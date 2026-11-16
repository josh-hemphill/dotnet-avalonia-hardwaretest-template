using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HardwareTest.Features.RunTest;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Guards bindings against each view or typed item context, including standalone task dialogs.
public sealed partial class RunTestViewBindingPathTests
{
    [Fact]
    public void Every_binding_resolves_on_its_declared_view_or_template_type()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var checkedPaths = new List<(string File, Type Type, string Path)>();
        foreach (var file in Directory.EnumerateFiles(FindRunTestDirectory(), "*.axaml"))
        {
            var root = XDocument.Load(file).Root!;
            Assert.NotNull(root.Attribute(xaml + "DataType"));
            var regions = root.DescendantsAndSelf().Where(element => element == root || IsTypedItem(element, xaml));
            foreach (var region in regions)
            {
                var declared = region.Attribute(xaml + "DataType")!.Value;
                var parts = declared.Split(':', 2);
                Assert.Equal(2, parts.Length);
                var namespaceName = region.GetNamespaceOfPrefix(parts[0])?.NamespaceName;
                Assert.StartsWith("using:", namespaceName);
                var type = typeof(RunTestViewModel).Assembly.GetType(namespaceName!["using:".Length..] + "." + parts[1]);
                Assert.NotNull(type);
                var ownMarkup = new XElement(region);
                ownMarkup.Descendants().Where(element => IsTypedItem(element, xaml)).ToArray().Remove();
                foreach (var path in BindingPaths(ownMarkup.ToString(SaveOptions.DisableFormatting)))
                    checkedPaths.Add((System.IO.Path.GetFileName(file), type, path));
            }
        }

        // These guards ensure both page and child-dialog/template contexts are actually checked.
        Assert.True(checkedPaths.Count > 40);
        Assert.Contains(checkedPaths, item => item.Type == typeof(RunTestViewModel) && item.Path == "Workspace.ShowSteps");
        Assert.Contains(checkedPaths, item => item.Type == typeof(RunTestViewModel) && item.Path == "StepTree.StepListItems");
        Assert.Contains(checkedPaths, item => item.Type == typeof(BenchReferencePanelViewModel) && item.Path == "Rules");
        Assert.Contains(checkedPaths, item => item.Type == typeof(BenchReferenceRuleRow) && item.Path == "AutomationName");
        Assert.Contains(checkedPaths, item => item.Type == typeof(BenchReferenceCatalogItem) && item.Path == "Label");
        var unresolved = checkedPaths.Where(item => !Resolves(item.Type, item.Path))
            .Select(item => $"{item.File}: {item.Type.Name}.{item.Path}").Distinct().OrderBy(item => item).ToArray();
        Assert.True(unresolved.Length == 0, $"Run board AXAML has unresolved bindings: {string.Join(", ", unresolved)}");
    }

    private static bool IsTypedItem(XElement element, XNamespace xaml)
        => element.Name.LocalName is "DataTemplate" or "Style" && element.Attribute(xaml + "DataType") is not null;

    private static string FindRunTestDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "HardwareTest", "Features", "RunTest");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"RunTest views not found above '{AppContext.BaseDirectory}'.");
    }

    private static IEnumerable<string> BindingPaths(string markup)
    {
        foreach (Match match in BindingRegex().Matches(markup))
        {
            var path = match.Groups["path"].Value.Trim().TrimStart('!');
            if (path.Length > 0 && !path.StartsWith('$') && !path.StartsWith('#')) yield return path;
        }
    }

    private static bool Resolves(Type root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            var property = current.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
            if (property is null) return false;
            current = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }
        return true;
    }

    [GeneratedRegex("""\{Binding\s+(?<path>[^,}]*)""")]
    private static partial Regex BindingRegex();
}
