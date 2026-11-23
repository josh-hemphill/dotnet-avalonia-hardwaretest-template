using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public sealed record ProcedureEditorItem(string Id, string Name, string Kind, string Detail, int Depth, bool IsStep)
{
    public override string ToString() => new string(' ', Depth * 3) + Name;
}

public sealed record ProcedureEditorField(ProcedureField Definition, string Value, bool ReadOnly, IReadOnlyList<string> Choices);

/// Edits one isolated draft. Unknown XML and JSON fields are retained in the source snapshot.
public sealed partial class ProcedureEditorSession
{
    private readonly ProgramDraft _original;
    private readonly string _directory;
    private readonly XDocument _xml;
    private readonly Dictionary<string, JsonObject> _data = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProcedureStepEditor> _steps;
    private readonly IReadOnlyList<AuthoringHardwareDefinition> _hardware;
    private readonly HashSet<string> _changedData = new(StringComparer.Ordinal);
    private readonly List<ProcedureDataEditor> _files = [];
    private bool _changedPlan;

    public ProcedureEditorSession(ProgramDraft draft, string path, AuthoringManifest manifest, string? pluginHome = null)
    {
        _original = draft;
        _directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        _xml = XDocument.Parse(draft.ImportedPlanXml ?? throw new AuthoringWorkspaceException("Select an imported procedure."), LoadOptions.PreserveWhitespace);
        _steps = new(StringComparer.Ordinal);
        _hardware = manifest.Catalogs?.Hardware ?? [];
        try
        {
            AuthoringPluginSearch.Search(pluginHome is null ? [] : [pluginHome]);
            var profiles = PlanCompiler.WithProcedureSnapshot(draft.ImportedPlanXml, _directory, draft.ImportedData,
                (_, plan) => plan.ChildTestSteps.OfType<IProcedureAuthoring>().Select(plugin => plugin.EditorProfile).ToArray());
            foreach (var profile in profiles)
            {
                if (profile.Version != 1) throw new AuthoringWorkspaceException($"Procedure editor contract {profile.Version} requires a compatible Authoring release.");
                foreach (var step in profile.Steps) _steps.TryAdd(step.Type, step);
                _files.AddRange(profile.DataFiles.Where(file => !_files.Any(known => known.Path == file.Path)));
            }
            foreach (var file in _files)
            {
                if (!AuthoringProgramAssets.ForPlan(manifest, draft.PlanId).Contains(file.Path, StringComparer.Ordinal))
                    throw new AuthoringWorkspaceException($"Declare '{file.Path}' in programAssets for '{draft.PlanId}' and reopen the workspace.");
                var contents = draft.ImportedData.GetValueOrDefault(file.Path)
                    ?? File.ReadAllText(AuthoringProgramAssets.ResolvePath(_directory, file.Path));
                _data.Add(file.Path, JsonNode.Parse(contents)?.AsObject() ?? throw new InvalidDataException($"Missing root object in {file.Path}."));
            }
            if (profiles.Length == 0) InspectionReason = "Install a procedure editor plugin to edit this tree. The original steps are available for inspection.";
        }
        catch (Exception error) when (error is AuthoringWorkspaceException or IOException or JsonException or InvalidDataException or InvalidOperationException)
        {
            InspectionReason = error.Message;
            _steps.Clear();
            _files.Clear();
            _data.Clear();
        }
    }

    public string? InspectionReason { get; }
    public bool CanEdit => InspectionReason is null;
    public bool HasChanges => _changedPlan || _changedData.Count > 0;
    private IEnumerable<XElement> Roots => PlanCompiler.ImportedSteps(_xml.Root?.Element("Steps"));

    public IReadOnlyList<ProcedureEditorItem> Steps() => Roots.SelectMany(root => DescribeSteps(root, 0)).ToArray();

    private IEnumerable<ProcedureEditorItem> DescribeSteps(XElement step, int depth)
    {
        var type = PlanCompiler.SerializedStepType(step);
        yield return new(StepId(step), step.Element("Name")?.Value ?? type, _steps.GetValueOrDefault(type)?.Label ?? "Plugin step",
            step.Element("CaseId")?.Value ?? type, depth, true);
        foreach (var child in PlanCompiler.ImportedSteps(step.Element("ChildTestSteps")))
            foreach (var item in DescribeSteps(child, depth + 1)) yield return item;
    }

    public IReadOnlyList<ProcedureEditorItem> Items(bool showData, string search)
        => (showData ? DataEntries().Select(entry => entry.Item) : Steps())
            .Where(item => string.IsNullOrWhiteSpace(search) || $"{item.Name} {item.Kind} {item.Detail}".Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

    public IReadOnlyList<ProcedureEditorField> Fields(ProcedureEditorItem item)
    {
        if (item.IsStep)
        {
            var step = FindStep(item.Id);
            if (!_steps.TryGetValue(PlanCompiler.SerializedStepType(step), out var schema)) return [];
            return new[] { new ProcedureField("Name", "Display name") }.Concat(schema.Fields)
                .Select(field => ViewField(field, XmlFieldValue(step, field), false)).ToArray();
        }
        var entry = DataEntries().Single(row => row.Item.Id == item.Id);
        return entry.Fields.Select(field => ViewField(field, Display(entry.Node[field.Key]), entry.Identity.Contains(field.Key))).ToArray();
    }

    public string Describe(ProcedureEditorItem item)
    {
        if (item.IsStep)
        {
            var step = FindStep(item.Id);
            if (!_steps.ContainsKey(PlanCompiler.SerializedStepType(step))) return step.ToString(SaveOptions.DisableFormatting);
            var links = Related(item).Where(related => !related.IsStep).ToArray();
            return links.Length == 0 ? item.Kind : string.Join(Environment.NewLine, links.Select(link => $"{link.Kind} · {link.Name}: {link.Detail}"));
        }
        return item.Detail;
    }

    public IReadOnlyList<ProcedureEditorItem> Related(ProcedureEditorItem item)
    {
        var entries = DataEntries().ToArray();
        if (item.IsStep)
        {
            var sourceStep = FindStep(item.Id);
            var id = sourceStep.Element("CaseId")?.Value;
            var test = entries.FirstOrDefault(entry => entry.Table == "cases" && entry.Item.Name == id);
            if (test is not null) return RelatedData(test, entries).Append(test.Item).DistinctBy(row => row.Id).ToArray();
            var required = StepPrerequisites(sourceStep);
            return Steps().Where(step => required.Contains(step.Id) || StepPrerequisites(FindStep(step.Id)).Contains(item.Id)).ToArray();
        }
        var source = entries.Single(entry => entry.Item.Id == item.Id);
        var cases = AffectedCases(source, entries).ToHashSet(StringComparer.Ordinal);
        return RelatedData(source, entries).Concat(Steps().Where(step => cases.Contains(FindStep(step.Id).Element("CaseId")?.Value ?? "")))
            .DistinctBy(row => row.Id).ToArray();
    }

    public void SetFields(ProcedureEditorItem item, IReadOnlyDictionary<string, string> values)
    {
        EnsureEditable();
        var changes = Fields(item).Where(field => !field.ReadOnly && values.TryGetValue(field.Definition.Key, out var value) && value != field.Value)
            .Select(field => (field.Definition, Value: Parse(field.Definition, values[field.Definition.Key]))).ToArray();
        if (changes.Length == 0) return;
        if (item.IsStep)
        {
            var step = FindStep(item.Id);
            foreach (var (definition, value) in changes)
            {
                if (definition.Kind == ProcedureFieldKind.Instrument) BindInstrument(step, definition, Display(value));
                else WriteXml(step, definition.Key, Display(value));
                if (definition.Key == "CaseId" && step.Element("HardwareTest.Presentation.ChannelKey") is { } channel)
                    channel.Value = Display(value);
            }
            _changedPlan = true;
        }
        else
        {
            var entry = DataEntries().Single(row => row.Item.Id == item.Id);
            foreach (var (definition, value) in changes) entry.Node[definition.Key] = value;
            _changedData.Add(entry.Path);
        }
    }

    public ProgramDraft Complete(bool validate = true)
    {
        if (!HasChanges) return _original;
        var data = new Dictionary<string, string>(_original.ImportedData, StringComparer.Ordinal);
        foreach (var path in _changedData)
            data[path] = _data[path].ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }) + "\n";
        var xml = _changedPlan ? _xml.Declaration + "\n" + _xml.ToString(SaveOptions.DisableFormatting) : _original.ImportedPlanXml!;
        if (validate)
        {
            PlanCompiler.WithProcedureSnapshot(xml, _directory, data, (_, plan) =>
            {
                foreach (var plugin in plan.ChildTestSteps.OfType<IProcedureAuthoring>()) plugin.ValidateAuthoring();
                return true;
            });
        }

        return _original with
        {
            ImportedPlanXml = xml,
            ImportedData = data,
            Measure = Roots.Select(step => new RawStepNode(PlanCompiler.SerializedStepType(step), step.ToString(SaveOptions.DisableFormatting))
            { NodeId = Guid.Parse(StepId(step)) }).ToArray()
        };
    }

    private static string[] StepPrerequisites(XElement step)
        => (step.Element("PrerequisiteIds")?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private XElement FindStep(string id) => _xml.Descendants("TestStep").Single(step => StepId(step) == id);
    private static string StepId(XElement step) => (string?)step.Attribute("Id") ?? string.Empty;
    private void EnsureEditable()
    {
        if (!CanEdit) throw new AuthoringWorkspaceException(InspectionReason!);
    }

    private static XElement? FindXml(XElement root, string path)
    {
        XElement? found = root;
        foreach (var part in path.Split('/')) found = found?.Element(part);
        return found;
    }

    private static void WriteXml(XElement root, string path, string value)
    {
        var current = root;
        foreach (var part in path.Split('/'))
        {
            if (current.Element(part) is not { } element)
            {
                element = new XElement(part);
                current.AddFirst(element);
            }
            current = element;
        }
        current.Value = value;
    }

    private static string Default(ProcedureField field) => field.DefaultValue ?? (field.Kind == ProcedureFieldKind.Boolean ? "false" : "");
    private static string Display(JsonNode? node) => node switch
    {
        null => "",
        JsonArray array => string.Join(", ", array.Select(Display)),
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString()
    };

    private static JsonNode? Parse(ProcedureField field, string value)
    {
        var text = value.Trim();
        if (text.Length == 0 && field.Optional && field.Kind == ProcedureFieldKind.Text) return null;
        if (text.Length == 0 && !field.Optional) throw new InvalidDataException($"Enter {field.Label}.");
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return field.Kind switch
        {
            ProcedureFieldKind.Text or ProcedureFieldKind.Instrument => JsonValue.Create(value),
            ProcedureFieldKind.Boolean when bool.TryParse(text, out var flag) => JsonValue.Create(flag),
            ProcedureFieldKind.Integer when int.TryParse(text, CultureInfo.InvariantCulture, out var integer) => JsonValue.Create(integer),
            ProcedureFieldKind.Number => JsonValue.Create(Number(field, text)),
            ProcedureFieldKind.TextList => new JsonArray(parts.Select(part => (JsonNode?)JsonValue.Create(part)).ToArray()),
            ProcedureFieldKind.NumberList => new JsonArray(parts.Select(part => (JsonNode?)JsonValue.Create(Number(field, part))).ToArray()),
            _ => throw new InvalidDataException($"Enter {field.Label} as {field.Kind.ToString().ToLowerInvariant()}.")
        };
    }

    private static double Number(ProcedureField field, string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number) ? number : throw new InvalidDataException($"Enter a finite number for {field.Label}.");
}
