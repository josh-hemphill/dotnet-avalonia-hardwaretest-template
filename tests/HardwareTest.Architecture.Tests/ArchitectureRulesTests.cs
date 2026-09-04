using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
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
    private const string FeatureFileSizeRule =
        "docs/platform-phases/phase-9-runboard-decomposition.md — feature files stay decomposed; split into a child ViewModel or a partial.";
    private const string SessionFacadeSplitRule =
        "docs/platform-phases/phase-14-session-facade-split.md — Feature ViewModels take focused IOpenTap* surfaces, not the aggregating IOpenTapSession.";
    private const string Phase22PluginIviRule =
        "docs/platform-phases/phase-22-visa-broker.md — plugins must not call Ivi.Visa / GlobalResourceManager; Core owns the broker.";
    private const string Phase23WorkerRule =
        "docs/platform-phases/phase-23-safety-opentap-worker.md — OpenTAP worker is Avalonia-free; no TapThread.Abort in the UI process.";
    private const string PlanValidateAvaloniaFree =
        "docs/adapting.md — HardwareTest.PlanValidate stays Avalonia-free and reuses Host plan-contract checks.";
    private const string Phase24SessionSplitRule =
        "docs/platform-phases/phase-24-session-decomposition.md — no static pause/interaction on StepRuntime; run state is per OpenTapRunContext.";
    private const string Phase25ClockRule =
        "docs/platform-phases/phase-25-clock-discipline.md — idle/retention/run-complete use IClock; Safety Stop must not wait on NTP.";

    private const int MaxFeatureFileLines = 600;

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
    public void OpenTapWorker_must_not_reference_Avalonia()
    {
        AssertNoForbiddenReference(
            typeof(global::HardwareTest.OpenTap.Worker.Program).Assembly,
            name => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase),
            Phase23WorkerRule);
    }

    [Fact]
    public void PlanValidate_must_not_reference_Avalonia()
    {
        AssertNoForbiddenReference(
            typeof(global::HardwareTest.PlanValidate.Program).Assembly,
            name => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase),
            PlanValidateAvaloniaFree);
    }

    [Fact]
    public void Source_must_not_call_TapThread_Abort()
    {
        var srcRoot = Path.Combine(FindRepoRoot(), "src");
        Assert.True(Directory.Exists(srcRoot), $"src root not found at '{srcRoot}'.");
        var offenders = Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path, srcRoot))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(file => file.Text.Contains("TapThread.Abort(", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(srcRoot, file.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{Phase23WorkerRule} TapThread.Abort( callers:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void StepRuntime_must_not_expose_static_pause_or_interaction()
    {
        var srcRoot = Path.Combine(FindRepoRoot(), "src");
        Assert.True(Directory.Exists(srcRoot), $"src root not found at '{srcRoot}'.");
        string[] members = ["WaitIfPaused", "RequestInteraction", "RequestOperatorAttention"];
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(path, srcRoot))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            if (text.Contains("static Action? WaitIfPaused", StringComparison.Ordinal)
                || text.Contains("static Func<OperatorInteractionRequest", StringComparison.Ordinal))
            {
                offenders.Add($"{Path.GetRelativePath(srcRoot, path)} contains static WaitIfPaused/RequestInteraction");
            }

            foreach (var member in members)
            {
                if (ContainsNonInterfaceStepRuntimeMember(text, member))
                {
                    offenders.Add($"{Path.GetRelativePath(srcRoot, path)} contains StepRuntime.{member}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{Phase24SessionSplitRule} Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Idle_retention_and_run_complete_must_not_use_wall_clock_UtcNow()
    {
        var repo = FindRepoRoot();
        string[] relativePaths =
        [
            Path.Combine("src", "HardwareTest.OpenTap.Host", "OperatorSession.cs"),
            Path.Combine("src", "HardwareTest.Core", "Settings", "OperatorSessionIdle.cs"),
            Path.Combine("src", "HardwareTest.Core", "Storage", "RunRetentionService.cs"),
            Path.Combine("src", "HardwareTest.Core", "Runs", "FileRunStore.cs"),
            Path.Combine("src", "HardwareTest", "Features", "RunTest", "OperatorSessionPanelViewModel.cs"),
            Path.Combine("src", "HardwareTest", "Features", "RunTest", "RunExecutionViewModel.cs"),
            Path.Combine("src", "HardwareTest.OpenTap.Host", "OpenTapRunContext.cs"),
            Path.Combine("src", "HardwareTest.OpenTap.Host", "OpenTapProgressResultListener.cs"),
            Path.Combine("src", "HardwareTest.OpenTap.Host", "Worker", "OpenTapWorkerClient.cs"),
            Path.Combine("src", "HardwareTest.Core", "Crash", "DanglingRunReconciler.cs"),
        ];

        var offenders = new List<string>();
        foreach (var relative in relativePaths)
        {
            var path = Path.Combine(repo, relative);
            Assert.True(File.Exists(path), $"Expected clock-disciplined file '{relative}'.");
            var text = File.ReadAllText(path);
            if (text.Contains("DateTime.UtcNow", StringComparison.Ordinal)
                || text.Contains("DateTimeOffset.UtcNow", StringComparison.Ordinal))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{Phase25ClockRule} Wall-clock UtcNow in idle/retention/run-complete:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Safety_stop_and_worker_kill_must_not_wait_on_NTP()
    {
        var repo = FindRepoRoot();
        string[] relativePaths =
        [
            Path.Combine("src", "HardwareTest.OpenTap.Host", "Worker", "OpenTapWorkerClient.cs"),
            Path.Combine("src", "HardwareTest.OpenTap.Host", "Worker", "OpenTapWorkerProcess.cs"),
            Path.Combine("src", "HardwareTest", "Features", "RunTest", "RunExecutionViewModel.cs"),
            Path.Combine("src", "HardwareTest", "Crash", "CrashHandler.cs"),
        ];
        string[] needles = ["INtpTimeSource", "ClockSkewDetector", "UdpNtpTimeSource", "NtpHost"];
        var offenders = new List<string>();
        foreach (var relative in relativePaths)
        {
            var path = Path.Combine(repo, relative);
            Assert.True(File.Exists(path), $"Expected safety-stop file '{relative}'.");
            var text = File.ReadAllText(path);
            foreach (var needle in needles)
            {
                if (text.Contains(needle, StringComparison.Ordinal))
                {
                    offenders.Add($"{relative} contains '{needle}'");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{Phase25ClockRule} Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// True when source mentions <c>StepRuntime.Member</c> but not <c>IStepRuntime.Member</c>.
    private static bool ContainsNonInterfaceStepRuntimeMember(string text, string member)
    {
        var needle = "StepRuntime." + member;
        var idx = 0;
        while ((idx = text.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            if (idx == 0 || text[idx - 1] != 'I')
            {
                return true;
            }

            idx += needle.Length;
        }

        return false;
    }

    [Fact]
    public void BasicPlugins_must_not_reference_Avalonia_or_ScottPlot()
    {
        AssertNoForbiddenDirectReference(
            typeof(AcquireVoltageStep).Assembly,
            IsAvaloniaOrScottPlot,
            PhaseIPresentation);
    }

    [Fact]
    public void MixinsPlugins_must_not_reference_Avalonia_or_ScottPlot()
    {
        AssertNoForbiddenDirectReference(
            typeof(AnnotationMixin).Assembly,
            IsAvaloniaOrScottPlot,
            PhaseIPresentation);
    }

    [Fact]
    public void BasicPlugins_must_not_reference_Core()
    {
        AssertNoForbiddenDirectReference(
            typeof(AcquireVoltageStep).Assembly,
            name => string.Equals(name, "HardwareTest.Core", StringComparison.Ordinal),
            "docs/adapting.md — HardwareTest Basic is the Editor authoring pack and must not pull Core (VISA broker stays in Plugins.Visa).");
    }

    [Theory]
    [InlineData(typeof(AppSettings))]
    [InlineData(typeof(OpenTapSession))]
    [InlineData(typeof(AcquireVoltageStep))]
    [InlineData(typeof(VisaDmmInstrument))]
    [InlineData(typeof(AnnotationMixin))]
    [InlineData(typeof(global::HardwareTest.MainWindow))]
    [InlineData(typeof(global::HardwareTest.OpenTap.Worker.Program))]
    [InlineData(typeof(global::HardwareTest.PlanValidate.Program))]
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
            typeof(ClockLastGoodRecord),
            typeof(StationIdnDocument),
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

    [Fact]
    public void Feature_source_files_stay_under_line_budget()
    {
        var featuresRoot = Path.Combine(FindRepoRoot(), "src", "HardwareTest", "Features");
        Assert.True(Directory.Exists(featuresRoot), $"Features root not found at '{featuresRoot}'.");

        var offenders = Directory.EnumerateFiles(featuresRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path, featuresRoot))
            .Select(path => (Path: path, Lines: File.ReadAllLines(path).Length))
            .Where(file => file.Lines > MaxFeatureFileLines)
            .OrderByDescending(file => file.Lines)
            .Select(file => $"{Path.GetRelativePath(featuresRoot, file.Path)} ({file.Lines} lines)")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{FeatureFileSizeRule} Over {MaxFeatureFileLines} lines:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Feature_viewmodels_do_not_depend_on_aggregating_IOpenTapSession()
    {
        var featuresRoot = Path.Combine(FindRepoRoot(), "src", "HardwareTest", "Features");
        Assert.True(Directory.Exists(featuresRoot), $"Features root not found at '{featuresRoot}'.");

        var offenders = Directory.EnumerateFiles(featuresRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path, featuresRoot))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(file => file.Text.Contains("IOpenTapSession", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(featuresRoot, file.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{SessionFacadeSplitRule} Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Plugin_source_must_not_use_Ivi_Visa()
    {
        var srcRoot = Path.Combine(FindRepoRoot(), "src");
        string[] pluginRoots =
        [
            Path.Combine(srcRoot, "HardwareTest.OpenTap.Plugins.Basic"),
            Path.Combine(srcRoot, "HardwareTest.OpenTap.Plugins.Visa"),
            Path.Combine(srcRoot, "HardwareTest.OpenTap.Plugins.Mixins"),
        ];

        string[] needles = ["Ivi.Visa", "GlobalResourceManager", "IviFoundation.Visa"];
        var offenders = new List<string>();
        foreach (var root in pluginRoots)
        {
            Assert.True(Directory.Exists(root), $"Plugin root not found at '{root}'.");
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly)))
            {
                if (IsBuildArtifact(path, root))
                {
                    continue;
                }

                var text = File.ReadAllText(path);
                foreach (var needle in needles)
                {
                    if (text.Contains(needle, StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetRelativePath(FindRepoRoot(), path)} contains '{needle}'");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{Phase22PluginIviRule} Offenders:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Authoring_plugin_package_xml_excludes_core()
    {
        var srcRoot = Path.Combine(FindRepoRoot(), "src");
        string[] packages =
        [
            Path.Combine(srcRoot, "HardwareTest.OpenTap.Plugins.Basic", "package.xml"),
            Path.Combine(srcRoot, "HardwareTest.OpenTap.Plugins.Mixins", "package.xml"),
        ];
        foreach (var path in packages)
        {
            Assert.True(File.Exists(path), path);
            var xml = File.ReadAllText(path);
            Assert.DoesNotContain("HardwareTest.Core.dll", xml, StringComparison.Ordinal);
            Assert.Contains("OpenTAP", xml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Template_program_package_xml_lists_plan_sidecar_and_depends_on_authoring_packs()
    {
        var path = Path.Combine(FindRepoRoot(), "plans", "opentap", "package.xml");
        Assert.True(File.Exists(path), path);
        var xml = File.ReadAllText(path);
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://opentap.io/schemas/package";
        Assert.Equal("HardwareTest Template Program", (string?)doc.Root?.Attribute("Name"));

        var files = doc.Descendants(ns + "File")
            .Select(e => (string?)e.Attribute("Path"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();
        Assert.Contains("sample.TapPlan", files);
        Assert.Contains("sample.program.json", files);
        Assert.Contains("template.program.json", files);
        Assert.Contains("program.schema.json", files);
        Assert.DoesNotContain(files, f => f!.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        var deps = doc.Descendants(ns + "PackageDependency")
            .Select(e => (string?)e.Attribute("Package"))
            .ToArray();
        Assert.Contains("OpenTAP", deps);
        Assert.Contains("HardwareTest Basic", deps);
        Assert.Contains("HardwareTest Mixins", deps);
        Assert.Equal(3, deps.Length);
        Assert.DoesNotContain("Expressions", deps);
        Assert.DoesNotContain(
            deps,
            d => d is not null && d.Contains("Visa", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("HardwareTest.Core", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("HardwareTest.OpenTap.Host", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Ivi.Visa", xml, StringComparison.Ordinal);
        // Manifest metadata only — Description may mention visa/SCPI; bundled TapPlans are not scanned.
        Assert.DoesNotContain("VisaAddress", xml, StringComparison.Ordinal);
    }

    private static readonly char[] PathSeparators =
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private static bool IsBuildArtifact(string path, string root)
        => Path.GetRelativePath(root, path)
            .Split(PathSeparators)
            .Any(segment => segment is "bin" or "obj");

    /// Walks up from the test output directory to the folder holding the solution file.
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

    /// Direct references only — Plugins.Visa may ProjectReference Core for IVisaBroker; Core's ScottPlot must not count as a plugin UI reference.
    private static void AssertNoForbiddenDirectReference(Assembly assembly, Func<string, bool> isForbidden, string rule)
    {
        var hits = assembly.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name) && isForbidden(name!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            hits.Length == 0,
            $"{rule} Assembly '{assembly.GetName().Name}' directly references forbidden: [{string.Join(", ", hits)}].");
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
