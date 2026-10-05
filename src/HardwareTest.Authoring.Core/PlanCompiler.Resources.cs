using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    /// OpenTAP rejects missing resource types. Read the remaining step structure with
    /// those properties absent, retaining the original resource XML in the source DTO.
    /// No replacement resource is instantiated by this import path.
    private static string ResourceTypeId(string serializedType) => serializedType.StartsWith("emb:", StringComparison.Ordinal) ? serializedType[4..] : serializedType;

    private static TestPlan LoadPlanWithOpaqueResources(string path, IReadOnlySet<string> availableLibraryTypes)
    {
        var document = XDocument.Load(path);
        var opaque = document.Descendants().Where(element => element.Name.LocalName == "Instrument"
            && element.Attribute("type") is { } type
            && (!AuthoringInstrumentCatalog.TryGet(ResourceTypeId(type.Value), out _)
                || (AuthoringInstrumentCatalog.IsLibrary(ResourceTypeId(type.Value))
                    && !availableLibraryTypes.Contains(ResourceTypeId(type.Value))))).ToArray();
        var coldSteps = document.Descendants().Where(element => element.Name.LocalName == "TestStep"
            && element.Attribute("type") is { } type && AuthoringInstrumentCatalog.IsLibrary(ResourceTypeId(type.Value))
            && !AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetType(ResourceTypeId(type.Value), false) is not null)).ToArray();
        if (opaque.Length == 0 && coldSteps.Length == 0) return TestPlan.Load(path);
        return LoadSanitized(opaque);

        TestPlan LoadSanitized(XElement[] opaque)
        {
            foreach (var element in opaque) element.Remove();
            foreach (var step in coldSteps)
            {
                // A temporary structural carrier lets OpenTAP read sibling steps without the library.
                // Decompile uses the untouched original XML and identity, never this carrier as source.
                step.SetAttributeValue("type", typeof(TestGroupStep).FullName);
                step.RemoveNodes();
            }
            var temporary = Path.Combine(Path.GetTempPath(), "ht-resource-import-" + Guid.NewGuid().ToString("N") + ".TapPlan");
            try { document.Save(temporary); return TestPlan.Load(temporary); }
            finally { File.Delete(temporary); }
        }
    }

    private static bool IsColdLibraryCarrier(ITestStep step, IReadOnlyDictionary<string, XElement> xmlById)
        => step is TestGroupStep && xmlById.TryGetValue(step.Id.ToString(), out var original)
            && original.Attribute("type") is { } type && AuthoringInstrumentCatalog.IsLibrary(ResourceTypeId(type.Value));

    private static string ImportedInstrumentSlot(ITestStep step, IReadOnlyDictionary<string, XElement> xmlById)
    {
        var slot = InstrumentSlotName(step);
        if (!string.IsNullOrWhiteSpace(slot)) return slot;
        return xmlById.TryGetValue(step.Id.ToString(), out var xml)
            ? xml.Elements().FirstOrDefault(element => element.Name.LocalName == "Instrument")?
                .Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value ?? string.Empty
            : string.Empty;
    }
}
