using System.Reflection;
using Avalonia.Controls;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using Xunit;

namespace HardwareTest.Architecture.Tests;

/// Smoke-level layering gates from docs/platform-phases/phase-2-architecture-tests.md.
public sealed class ArchitectureRulesTests
{
    private const string ReadmeHardSeparation =
        "README.md hard separation — HardwareTest.Core / OpenTap.Host stay Avalonia-free.";
    private const string CoreOpenTapFree =
        "platform-roadmap.md — HardwareTest.Core stays Avalonia-free and OpenTAP-free.";
    private const string PhaseIPresentation =
        "docs/opentap-phases/phase-i-presentation-contract.md#locked-rules — plugins must not reference Avalonia/ScottPlot UI types.";
    private const string ApplianceNoDialog =
        "docs/opentap-platform.md#interaction-contract-avalonia-owned — no WinForms/WPF dialogs on appliance.";
    private const string SingleWindowRule =
        "docs/opentap-platform.md#interaction-contract-avalonia-owned — only MainWindow; operator flow stays in-panel.";
    private const string JsonContextRule =
        "Directory.Build.props JsonSerializerIsReflectionEnabledByDefault=false — every disk-persisted type must be in AppJsonContext.";

    [Fact]
    public void Core_must_not_reference_Avalonia()
    {
        AssertNoForbiddenReference(
            typeof(AppSettings).Assembly,
            name => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase),
            ReadmeHardSeparation);
    }

    [Fact]
    public void Core_must_not_reference_OpenTap()
    {
        AssertNoForbiddenReference(
            typeof(AppSettings).Assembly,
            name => name.StartsWith("OpenTap", StringComparison.OrdinalIgnoreCase),
            CoreOpenTapFree);
    }

    [Fact]
    public void OpenTapHost_must_not_reference_Avalonia()
    {
        AssertNoForbiddenReference(
            typeof(OpenTapSession).Assembly,
            name => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase),
            ReadmeHardSeparation);
    }

    [Fact]
    public void BasicPlugins_must_not_reference_Avalonia_or_ScottPlot()
    {
        AssertNoForbiddenReference(
            typeof(AcquireVoltageStep).Assembly,
            IsAvaloniaOrScottPlot,
            PhaseIPresentation);
    }

    [Fact]
    public void MixinsPlugins_must_not_reference_Avalonia_or_ScottPlot()
    {
        AssertNoForbiddenReference(
            typeof(AnnotationMixin).Assembly,
            IsAvaloniaOrScottPlot,
            PhaseIPresentation);
    }

    [Theory]
    [InlineData(typeof(AppSettings))]
    [InlineData(typeof(OpenTapSession))]
    [InlineData(typeof(AcquireVoltageStep))]
    [InlineData(typeof(AnnotationMixin))]
    [InlineData(typeof(global::HardwareTest.MainWindow))]
    public void Assemblies_must_not_reference_WinForms_or_Wpf(Type marker)
    {
        AssertNoForbiddenReference(
            marker.Assembly,
            name => name is "System.Windows.Forms" or "PresentationFramework",
            ApplianceNoDialog);
    }

    [Fact]
    public void HardwareTest_has_only_MainWindow_as_Window_subclass()
    {
        var windows = typeof(global::HardwareTest.MainWindow).Assembly
            .GetTypes()
            .Where(t => typeof(Window).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // Allowlist by name — adding a second Window is a deliberate edit to this test.
        string[] allowed = ["MainWindow"];
        Assert.True(
            windows.SequenceEqual(allowed),
            $"{SingleWindowRule} Found Window types: [{string.Join(", ", windows)}]. Allowed: [{string.Join(", ", allowed)}].");
    }

    [Fact]
    public void Core_public_surface_must_not_mention_OpenTap_types()
    {
        var core = typeof(AppSettings).Assembly;
        var offenders = new List<string>();

        foreach (var type in core.GetExportedTypes())
        {
            foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                foreach (var related in RelatedTypes(member))
                {
                    if (MentionsOpenTap(related))
                    {
                        offenders.Add($"{type.FullName}.{member.Name} → {related}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{CoreOpenTapFree} Public Core members mentioning OpenTap:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Persisted_types_must_be_registered_in_AppJsonContext()
    {
        Type[] roots =
        [
            typeof(AppSettings),
            typeof(UiState),
            typeof(TestRunRecord),
            typeof(SuiteRunRecord),
        ];

        var missing = new List<string>();
        foreach (var type in WalkPersistedGraph(roots))
        {
            if (AppJsonContext.Default.GetTypeInfo(type) is null)
            {
                missing.Add(type.FullName ?? type.Name);
            }
        }

        Assert.True(
            missing.Count == 0,
            $"{JsonContextRule} Missing registrations:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    private static bool IsAvaloniaOrScottPlot(string name)
        => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("ScottPlot", StringComparison.OrdinalIgnoreCase);

    private static void AssertNoForbiddenReference(Assembly assembly, Func<string, bool> isForbidden, string rule)
    {
        var hits = GetReferencedAssemblyNames(assembly)
            .Where(isForbidden)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            hits.Length == 0,
            $"{rule} Assembly '{assembly.GetName().Name}' references forbidden: [{string.Join(", ", hits)}].");
    }

    private static IEnumerable<string> GetReferencedAssemblyNames(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<Assembly>();
        queue.Enqueue(root);
        seen.Add(root.GetName().Name!);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var name in current.GetReferencedAssemblies())
            {
                if (string.IsNullOrWhiteSpace(name.Name) || !seen.Add(name.Name))
                {
                    continue;
                }

                yield return name.Name;

                // Only chase our product assemblies; framework load failures are ignored.
                if (!name.Name.StartsWith("HardwareTest", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    queue.Enqueue(Assembly.Load(name));
                }
                catch (FileNotFoundException)
                {
                    // Skip unloadable product refs.
                }
                catch (FileLoadException)
                {
                    // Skip unloadable product refs.
                }
                catch (BadImageFormatException)
                {
                    // Skip unloadable product refs.
                }
            }
        }
    }

    private static IEnumerable<Type> RelatedTypes(MemberInfo member)
    {
        switch (member)
        {
            case PropertyInfo property:
                yield return property.PropertyType;
                break;
            case FieldInfo field:
                yield return field.FieldType;
                break;
            case EventInfo evt when evt.EventHandlerType is not null:
                yield return evt.EventHandlerType;
                break;
            case MethodInfo method:
                yield return method.ReturnType;
                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                break;
        }
    }

    private static bool MentionsOpenTap(Type type)
    {
        type = Unwrap(type);
        if ((type.Namespace ?? string.Empty).StartsWith("OpenTap", StringComparison.Ordinal)
            || (type.FullName ?? string.Empty).Contains("OpenTap", StringComparison.Ordinal))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Any(MentionsOpenTap);
        }

        return false;
    }

    private static Type Unwrap(Type type)
    {
        if (type.IsByRef || type.IsPointer)
        {
            type = type.GetElementType() ?? type;
        }

        if (type.IsArray)
        {
            type = type.GetElementType() ?? type;
        }

        return type;
    }

    private static IEnumerable<Type> WalkPersistedGraph(IEnumerable<Type> roots)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>(roots);
        while (queue.Count > 0)
        {
            var type = Unwrap(queue.Dequeue());
            if (!seen.Add(type))
            {
                continue;
            }

            if (IsCorePersistedType(type))
            {
                yield return type;
            }

            if (type.IsGenericType)
            {
                foreach (var arg in type.GetGenericArguments())
                {
                    queue.Enqueue(arg);
                }
            }

            if (type.FullName?.StartsWith("HardwareTest.Core", StringComparison.Ordinal) != true)
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                queue.Enqueue(property.PropertyType);
            }
        }
    }

    private static bool IsCorePersistedType(Type type)
        => type.FullName?.StartsWith("HardwareTest.Core", StringComparison.Ordinal) == true
           && !type.IsInterface
           && type != typeof(void);
}
