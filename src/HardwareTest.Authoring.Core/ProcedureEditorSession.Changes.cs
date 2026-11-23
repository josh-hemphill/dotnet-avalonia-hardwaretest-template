using System.Text.Json.Nodes;
using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public sealed partial class ProcedureEditorSession
{
    public IReadOnlyList<string> ReviewChanges()
    {
        var changes = new List<string>();
        if (_changedPlan)
        {
            var initial = XDocument.Parse(_original.ImportedPlanXml!);
            var before = initial.Descendants("TestStep").ToDictionary(StepId, StringComparer.Ordinal);
            foreach (var step in _xml.Descendants("TestStep"))
            {
                var name = step.Element("Name")?.Value ?? StepId(step);
                if (!before.TryGetValue(StepId(step), out var old)) { changes.Add($"Added {name}"); continue; }
                if (_steps.TryGetValue(PlanCompiler.SerializedStepType(step), out var schema))
                    foreach (var field in schema.Fields.Prepend(new("Name", "Name")))
                        AddChange(changes, name, field.Label, XmlFieldValue(old, field), XmlFieldValue(step, field));
                var oldOrder = PlanCompiler.ImportedSteps(old.Element("ChildTestSteps")).Select(StepId);
                var newOrder = PlanCompiler.ImportedSteps(step.Element("ChildTestSteps")).Select(StepId);
                if (!oldOrder.SequenceEqual(newOrder)) changes.Add($"Changed child order or membership in {name}");
            }
        }
        foreach (var file in _files.Where(file => _changedData.Contains(file.Path)))
        {
            var initial = _original.ImportedData.GetValueOrDefault(file.Path);
            if (initial is not null) CompareFile(file, JsonNode.Parse(initial)!.AsObject(), _data[file.Path], changes);
            else changes.Add($"Updated {file.Label}");
        }
        return changes;
    }

    public IReadOnlyList<string> ReviewExternalChanges()
    {
        var changes = new List<string>();
        foreach (var file in _files)
        {
            var path = AuthoringProgramAssets.ResolvePath(_directory, file.Path);
            if (!File.Exists(path)) { changes.Add($"Removed from disk · {file.Path}"); continue; }
            var original = _original.ImportedData.GetValueOrDefault(file.Path);
            var disk = File.ReadAllText(path);
            if (original is null || original == disk) continue;
            CompareFile(file, JsonNode.Parse(original)!.AsObject(), JsonNode.Parse(disk)!.AsObject(), changes);
        }
        return changes;
    }

    private static void CompareFile(ProcedureDataEditor file, JsonObject original, JsonObject updated, List<string> changes)
    {
        foreach (var field in file.HeaderFields)
            AddChange(changes, file.Label, field.Label, Display(original[field.Key]), Display(updated[field.Key]));
        var before = AllRows(file, original).ToDictionary(row => row.Item.Id, StringComparer.Ordinal);
        foreach (var row in AllRows(file, updated))
        {
            var label = $"{file.Label} / {row.Parent?.Item.Name} / {row.Item.Name}";
            if (!before.Remove(row.Item.Id, out var old)) { changes.Add($"Added {label}"); continue; }
            foreach (var field in row.Fields)
                AddChange(changes, label, field.Label, Display(old.Node[field.Key]), Display(row.Node[field.Key]));
        }
        foreach (var row in before.Values) changes.Add($"Removed {file.Label} / {row.Parent?.Item.Name} / {row.Item.Name}");
    }

    private static IEnumerable<DataEntry> AllRows(ProcedureDataEditor file, JsonObject root)
    {
        var header = new DataEntry(new("file/" + file.Path, file.Label, "Data file", file.Path, 0, false), file.Path, "", root,
            file.HeaderFields, [], null);
        return file.Tables.SelectMany(table => Entries(header, table));
    }

    private static void AddChange(List<string> changes, string name, string label, string? before, string? after)
    {
        if (!string.Equals(before ?? "", after ?? "", StringComparison.Ordinal))
            changes.Add($"{name} · {label}: {before ?? ""} → {after ?? ""}");
    }
}
