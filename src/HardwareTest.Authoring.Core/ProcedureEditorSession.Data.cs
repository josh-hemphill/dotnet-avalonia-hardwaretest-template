using System.Text.Json.Nodes;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public sealed partial class ProcedureEditorSession
{
    private sealed record DataEntry(ProcedureEditorItem Item, string Path, string Table, JsonObject Node,
        IReadOnlyList<ProcedureField> Fields, IReadOnlyList<string> Identity, DataEntry? Parent);

    private IEnumerable<DataEntry> DataEntries()
    {
        foreach (var file in _files)
        {
            var root = _data[file.Path];
            var header = new DataEntry(new("file/" + file.Path, file.Label, "Data file", file.Path, 0, false), file.Path, "",
                root, file.HeaderFields, [], null);
            yield return header;
            foreach (var table in file.Tables)
                foreach (var entry in Entries(header, table)) yield return entry;
        }
    }

    private static IEnumerable<DataEntry> Entries(DataEntry parent, ProcedureTableEditor table)
    {
        if (parent.Node[table.Key] is not JsonArray rows) yield break;
        var index = 0;
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in rows.OfType<JsonObject>())
        {
            var identity = string.Join(" / ", table.IdentityFields.Select(field => Display(node[field])));
            var key = parent.Item.Id + "/" + table.Key + "/" + Uri.EscapeDataString(identity);
            if (!existing.Add(key)) key += "/duplicate/" + index;
            var detail = string.Join(" · ", table.Fields.Where(field => !table.IdentityFields.Contains(field.Key))
                .Select(field => $"{field.Label} {Display(node[field.Key])}"));
            var row = new DataEntry(new(key, identity, table.Label, detail, parent.Item.Depth + 1, false), parent.Path,
                table.Key, node, table.Fields, table.IdentityFields, parent);
            yield return row;
            foreach (var child in table.Children ?? [])
                foreach (var entry in Entries(row, child)) yield return entry;
            index++;
        }
    }

    private ProcedureEditorField ViewField(ProcedureField field, string value, bool readOnly)
    {
        var choices = field.Kind == ProcedureFieldKind.Instrument ? HardwareChoices(field).Select(option => option.Id.ToString("D")).ToArray()
            : field.ReferenceTable switch
            {
                null => Array.Empty<string>(),
                "sections" => Roots.SelectMany(root => PlanCompiler.ImportedSteps(root.Element("ChildTestSteps"))).Select(StepId).ToArray(),
                var reference => DataEntries().Where(entry => entry.Table == reference).Select(entry => entry.Item.Name).Distinct().ToArray()
            };
        return new(field, value, readOnly, choices);
    }

    public string ChoiceLabel(ProcedureEditorField field, string token)
        => field.Definition.Kind == ProcedureFieldKind.Instrument
            ? HardwareChoices(field.Definition).FirstOrDefault(item => item.Id.ToString("D") == token)?.Name ?? "Current inline settings"
            : field.Definition.ReferenceTable == "sections"
            ? Steps().FirstOrDefault(step => step.Id == token)?.Name ?? token : token;

    private static bool Refers(DataEntry from, DataEntry to) => from.Fields.Any(field => field.ReferenceTable == to.Table
        && (from.Node[field.Key] is JsonArray values ? values.Any(value => Display(value) == to.Item.Name)
            : Display(from.Node[field.Key]) == to.Item.Name));

    private static IEnumerable<ProcedureEditorItem> RelatedData(DataEntry source, DataEntry[] entries)
    {
        var direct = entries.Where(entry => entry != source && (Refers(source, entry) || Refers(entry, source) || entry.Parent == source || entry == source.Parent));
        foreach (var entry in direct) yield return entry.Item;
        var routes = entries.Where(route => route.Table == "routes" && Refers(source, route));
        foreach (var calibration in entries.Where(entry => entry.Table == "calibrations" && routes.Any(route => Refers(entry, route))))
            yield return calibration.Item;
    }

    private static IEnumerable<string> AffectedCases(DataEntry source, DataEntry[] entries)
    {
        var route = source.Table == "calibrations" ? Display(source.Node["route"]) : null;
        return entries.Where(entry => entry.Table == "cases" && (entry == source || entry == source.Parent || Refers(entry, source)
            || (route is not null && entry.Node["routes"] is JsonArray choices && choices.Any(choice => Display(choice) == route))))
            .Select(entry => entry.Item.Name);
    }

    public bool CanDuplicateData(ProcedureEditorItem item) => !item.IsStep && CanEdit
        && DataEntries().Any(row => row.Item.Id == item.Id && row.Parent is not null && row.Identity.SequenceEqual(["id"]));

    public string DuplicateData(ProcedureEditorItem item, string id)
    {
        EnsureEditable();
        if (!CanDuplicateData(item) || string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Enter a new row ID.");
        var row = DataEntries().Single(entry => entry.Item.Id == item.Id);
        var rows = row.Parent!.Node[row.Table]!.AsArray();
        id = id.Trim();
        if (rows.OfType<JsonObject>().Any(candidate => Display(candidate["id"]) == id))
            throw new InvalidDataException($"{row.Item.Kind} already contains '{id}'.");
        var copy = row.Node.DeepClone().AsObject();
        copy["id"] = id;
        rows.Add(copy);
        _changedData.Add(row.Path);
        return DataEntries().Single(entry => ReferenceEquals(entry.Node, copy)).Item.Id;
    }

    public bool CanRemoveData(ProcedureEditorItem item) => !item.IsStep && CanEdit
        && DataEntries().Any(row => row.Item.Id == item.Id && row.Parent is not null);

    public string RemoveData(ProcedureEditorItem item)
    {
        EnsureEditable();
        if (!CanRemoveData(item)) throw new InvalidDataException("Select a data row to remove.");
        var source = DataEntries().Single(row => row.Item.Id == item.Id);
        var references = Related(item).Where(related => related.IsStep).ToArray();
        if (references.Length > 0)
            throw new InvalidDataException($"'{item.Name}' is used by {string.Join(", ", references.Select(step => step.Name))}. Update those references before removing the row.");
        source.Parent!.Node[source.Table]!.AsArray().Remove(source.Node);
        _changedData.Add(source.Path);
        return source.Parent.Item.Id;
    }

}
