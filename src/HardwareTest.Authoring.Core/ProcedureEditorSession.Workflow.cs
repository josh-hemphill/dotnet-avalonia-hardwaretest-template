using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public sealed partial class ProcedureEditorSession
{
    public bool HasStepChildren(ProcedureEditorItem item) => item.IsStep && PlanCompiler.ImportedSteps(FindStep(item.Id).Element("ChildTestSteps")).Any();

    public IEnumerable<string> AncestorIds(ProcedureEditorItem item) => FindStep(item.Id).Ancestors("TestStep").Select(StepId);

    public IReadOnlyList<ProcedureStepEditor> AddableChildren(ProcedureEditorItem item)
    {
        if (!item.IsStep || !_steps.TryGetValue(PlanCompiler.SerializedStepType(FindStep(item.Id)), out var schema)) return [];
        return schema.ChildTypes.Select(type => _steps.GetValueOrDefault(type)).OfType<ProcedureStepEditor>()
            .Where(item => item.AllowAdd).ToArray();
    }

    public string AddChild(ProcedureEditorItem parent, ProcedureStepEditor schema)
    {
        EnsureEditable();
        if (!AddableChildren(parent).Contains(schema)) throw new AuthoringWorkspaceException("This step cannot contain that type.");
        var node = FindStep(parent.Id);
        if (node.Element("ChildTestSteps") is not { } children)
        {
            children = new XElement("ChildTestSteps");
            node.Add(children);
        }
        var id = Guid.NewGuid().ToString("D");
        var prototype = _xml.Descendants("TestStep").FirstOrDefault(step => PlanCompiler.SerializedStepType(step) == schema.Type
            && step.Element("CaseId") is not null);
        var child = prototype is null ? new XElement("TestStep", new XAttribute("type", schema.Type),
            new XElement("Name", new XAttribute("Metadata", "Step Name"), schema.Label), new XElement("ChildTestSteps")) : new XElement(prototype);
        child.SetAttributeValue("Id", id);
        child.Element("Name")!.Value = schema.Label;
        if (child.Element("CaseId") is { } caseElement) caseElement.Value = string.Empty;
        children.Add(child);
        _changedPlan = true;
        return id;
    }

    public bool CanRemoveStep(ProcedureEditorItem item)
    {
        if (!item.IsStep || !CanEdit) return false;
        var step = FindStep(item.Id);
        return step.Parent?.Name == "ChildTestSteps" && step.DescendantsAndSelf("TestStep")
            .All(child => _steps.GetValueOrDefault(PlanCompiler.SerializedStepType(child))?.AllowAdd == true);
    }

    public string RemoveStep(ProcedureEditorItem item)
    {
        EnsureEditable();
        if (!CanRemoveStep(item)) throw new AuthoringWorkspaceException("This plugin step must be preserved.");
        var step = FindStep(item.Id);
        var removed = step.DescendantsAndSelf("TestStep").Select(StepId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dependent = _xml.Descendants("TestStep").FirstOrDefault(candidate => !removed.Contains(StepId(candidate))
            && StepPrerequisites(candidate).Any(removed.Contains));
        if (dependent is not null) throw new AuthoringWorkspaceException($"'{dependent.Element("Name")?.Value}' depends on this step. Update its prerequisites before removing the step.");
        var parent = step.Parent!.Parent!;
        step.Remove();
        _changedPlan = true;
        return StepId(parent);
    }

    public bool CanMove(ProcedureEditorItem item, int offset)
    {
        if (!item.IsStep || !CanEdit) return false;
        var step = FindStep(item.Id);
        if (step.Parent?.Name != "ChildTestSteps" || !_steps.TryGetValue(PlanCompiler.SerializedStepType(step), out var profile)
            || Adjacent(step, offset) is not { } sibling) return false;
        if (offset > 0 && profile.IsHandoff) return false;
        return offset > 0 || _steps.GetValueOrDefault(PlanCompiler.SerializedStepType(sibling))?.IsHandoff != true;
    }

    public void Move(ProcedureEditorItem item, int offset)
    {
        EnsureEditable();
        if (!CanMove(item, offset)) return;
        var step = FindStep(item.Id);
        var sibling = Adjacent(step, offset)!;
        step.Remove();
        if (offset < 0) sibling.AddBeforeSelf(step);
        else sibling.AddAfterSelf(step);
        _changedPlan = true;
    }

    private static XElement? Adjacent(XElement step, int offset) => offset switch
    {
        -1 => step.ElementsBeforeSelf("TestStep").LastOrDefault(),
        1 => step.ElementsAfterSelf("TestStep").FirstOrDefault(),
        _ => null
    };

    public bool CanPreview(ProcedureEditorItem item) => item.IsStep && _steps.GetValueOrDefault(PlanCompiler.SerializedStepType(FindStep(item.Id)))?.HasOperatorPreview == true;

    public OperatorInteractionRequest? OperatorPrompt(ProcedureEditorItem item, IReadOnlySet<string> complete)
    {
        if (!item.IsStep || !CanEdit) return null;
        // The same plugin constructs the runtime and preview requests.
        var draft = Complete(validate: false);
        return PlanCompiler.WithProcedureSnapshot(draft.ImportedPlanXml!, _directory, draft.ImportedData, (_, plan) =>
            plan.ChildTestSteps.OfType<IProcedureAuthoring>().Select(plugin => plugin.PreviewInteraction(Guid.Parse(item.Id), complete))
                .FirstOrDefault(prompt => prompt is not null));
    }
}
