using System.Text;
using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    internal static ProgramDraft? TryLoadImported(string path)
    {
        var original = File.ReadAllText(path);
        var xml = XDocument.Parse(original, LoadOptions.PreserveWhitespace);
        if (xml.Root?.Attribute(AuthoringMeasureSections.CompiledLayoutAttribute) is not null) return null;
        var roots = ImportedSteps(xml.Root?.Element("Steps")).ToArray();
        if (!roots.Any(IsPluginContainer)) return null;
        var nodes = roots.Select(step => new RawStepNode(SerializedStepType(step),
            step.ToString(SaveOptions.DisableFormatting))
        {
            NodeId = (Guid?)step.Attribute("Id") ?? throw new AuthoringWorkspaceException("Imported root step is missing its ID.")
        }).ToArray();
        return new ProgramDraft(Path.GetFileNameWithoutExtension(path), ReadSidecar(path), [], [], nodes,
            new CleanupPolicy(false, string.Empty)) { ImportedPlanXml = original };
    }

    private static bool IsPluginContainer(XElement element)
    {
        var type = SerializedStepType(element);
        return !type.StartsWith("OpenTap.", StringComparison.Ordinal)
            && !type.StartsWith("HardwareTest.OpenTap.Plugins.Basic.", StringComparison.Ordinal)
            && ImportedSteps(element.Element("ChildTestSteps")).Any();
    }

    internal static IEnumerable<XElement> ImportedSteps(XElement? container)
        => container?.Elements("TestStep") ?? [];

    internal static string SerializedStepType(XElement element)
        => ResourceTypeId((string?)element.Attribute("type") ?? string.Empty).Split(',')[0];

    private void SaveImported(ProgramDraft draft, string xml, string path)
    {
        AuthoringPluginSearch.Search(LibraryHome is { } home ? [.. _extraPluginDirectories, home.Root] : _extraPluginDirectories);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        WithProcedureSnapshot(xml, directory, draft.ImportedData, (temporary, plan) =>
        {
            foreach (var plugin in plan.ChildTestSteps.OfType<IProcedureAuthoring>()) plugin.ValidateAuthoring();
            return true;
        });
        Directory.CreateDirectory(directory);
        // An exclusive lease serializes procedure publications that may share declared assets.
        var lockPath = Path.Combine(directory, ".authoring-procedure.lock");
        AuthoringBuildService.EnsureContained(directory, AuthoringBuildService.ResolvedPath(lockPath, directory: false));
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        WritePlanAndSidecar(stream => stream.Write(Encoding.UTF8.GetBytes(xml)), path,
            CloneSidecar(draft.Sidecar), draft.ImportedData);
    }

    internal static T WithProcedureSnapshot<T>(string xml, string sourceDirectory, IReadOnlyDictionary<string, string> assets,
        Func<string, TestPlan, T> action)
    {
        var stage = Directory.CreateTempSubdirectory("hardwaretest-authoring-").FullName;
        var planPath = Path.Combine(stage, "procedure.TapPlan");
        try
        {
            File.WriteAllText(planPath, xml);
            var plan = ReadImportedForAuthoring(planPath, validate: false);
            var profiles = plan.ChildTestSteps.OfType<IProcedureAuthoring>().Select(plugin => plugin.EditorProfile).ToArray();
            foreach (var relative in profiles.SelectMany(profile => profile.DataFiles).Select(file => file.Path).Distinct(StringComparer.Ordinal))
            {
                var target = AuthoringProgramAssets.ResolvePath(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (assets.TryGetValue(relative, out var contents)) File.WriteAllText(target, contents);
                else
                {
                    var source = AuthoringProgramAssets.ResolvePath(sourceDirectory, relative);
                    if (!File.Exists(source)) throw new AuthoringWorkspaceException($"PROCEDURE_ASSET: Missing '{relative}'. Restore the data file and retry.");
                    File.Copy(source, target);
                }
            }
            return action(planPath, plan);
        }
        finally { Directory.Delete(stage, recursive: true); }
    }

    internal static TestPlan ReadImportedForAuthoring(string path, bool validate)
    {
        var xml = XDocument.Load(path);
        var roots = ImportedSteps(xml.Root?.Element("Steps")).ToArray();
        TestPlan plan;
        try
        {
            plan = TestPlan.Load(path);
            Compare(roots, plan.ChildTestSteps);
            AssertNoDialog(plan);
        }
        catch (Exception error) when (error is not AuthoringWorkspaceException)
        {
            throw new AuthoringWorkspaceException($"PROCEDURE_PLUGIN: Restore the packages required by '{Path.GetFileName(path)}' and reopen. {error.Message}", error);
        }
        if (validate)
            foreach (var plugin in plan.ChildTestSteps.OfType<IProcedureAuthoring>()) plugin.ValidateAuthoring();
        return plan;

        static void Compare(IEnumerable<XElement> original, IEnumerable<ITestStep> loaded)
        {
            using var enumerator = loaded.GetEnumerator();
            foreach (var expected in original)
            {
                var identity = (string?)expected.Attribute("Id");
                var type = SerializedStepType(expected);
                if (!enumerator.MoveNext() || !Guid.TryParse(identity, out var id) || enumerator.Current.Id != id
                    || enumerator.Current.GetType().FullName != type)
                    throw new AuthoringWorkspaceException($"PROCEDURE_PLUGIN: Restore the plugin for {type} ({identity}) before exporting this procedure.");
                Compare(ImportedSteps(expected.Element("ChildTestSteps")), enumerator.Current.ChildTestSteps);
            }
            if (enumerator.MoveNext()) throw new AuthoringWorkspaceException("PROCEDURE_LAYOUT: The installed plugin changed the imported step tree.");
        }
    }
}
