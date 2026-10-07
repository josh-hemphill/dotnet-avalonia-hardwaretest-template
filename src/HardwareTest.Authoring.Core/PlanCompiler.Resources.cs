using System.Xml.Linq;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    /// OpenTAP rejects missing resource types. Read the remaining step structure with
    /// those properties absent, retaining the original resource XML in the source DTO.
    /// No replacement resource is instantiated by this import path.
    private static TestPlan LoadPlanWithOpaqueResources(string path)
    {
        try { return TestPlan.Load(path); }
        catch (TestPlan.PlanLoadException)
        {
            var document = XDocument.Load(path);
            var opaque = document.Descendants().Where(element => element.Name.LocalName == "Instrument"
                && element.Attribute("type") is { } type
                && !AuthoringInstrumentCatalog.TryGet(type.Value, out _)).ToArray();
            if (opaque.Length == 0) throw;
            foreach (var element in opaque) element.Remove();
            var temporary = Path.Combine(Path.GetTempPath(), "ht-resource-import-" + Guid.NewGuid().ToString("N") + ".TapPlan");
            try
            {
                document.Save(temporary);
                return TestPlan.Load(temporary);
            }
            finally { File.Delete(temporary); }
        }
    }

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
