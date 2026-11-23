using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public sealed partial class ProcedureEditorSession
{
    private IEnumerable<AuthoringHardwareDefinition> HardwareChoices(ProcedureField definition)
        => _hardware.Where(item => string.Equals(item.TypeId, definition.ReferenceTable, StringComparison.Ordinal));

    private string XmlFieldValue(XElement step, ProcedureField field)
    {
        var element = FindXml(step, field.Key);
        if (field.Kind != ProcedureFieldKind.Instrument) return element?.Value ?? Default(field);
        return HardwareChoices(field).FirstOrDefault(option => option.Name == element?.Element("Name")?.Value
            && option.Address == element?.Element("Address")?.Value)?.Id.ToString("D") ?? "";
    }

    private void BindInstrument(XElement step, ProcedureField field, string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return;
        var profile = HardwareChoices(field).SingleOrDefault(option => option.Id.ToString("D") == profileId)
            ?? throw new AuthoringWorkspaceException("Select a station profile compatible with this instrument.");
        var resource = FindXml(step, field.Key);
        if (resource is null || PlanCompiler.SerializedStepType(resource) != profile.TypeId)
            throw new AuthoringWorkspaceException($"'{field.Label}' must bind '{profile.TypeId}'.");
        var prefix = field.Key + "/";
        var settings = _steps[PlanCompiler.SerializedStepType(step)].Fields.Where(setting => setting.Key.StartsWith(prefix, StringComparison.Ordinal));
        var values = new List<(string Key, string Value)> { ("Name", profile.Name), ("Address", profile.Address) };
        foreach (var setting in settings)
        {
            var key = setting.Key[prefix.Length..];
            if (key is not ("Name" or "Address") && profile.Settings.TryGetValue(key, out var text))
                values.Add((key, Display(Parse(setting, text))));
        }
        foreach (var (key, value) in values) WriteXml(resource, key, value);
    }
}
