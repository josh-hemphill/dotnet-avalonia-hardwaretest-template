using HardwareTest.OpenTap.Plugins.Basic;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Editable field for operator interactions and (later) plan parameters.
public partial class InteractionFieldViewModel : ReactiveObject
{
    public InteractionFieldViewModel(OperatorInteractionField field)
    {
        Id = field.Id;
        Label = field.Label;
        Kind = field.Kind;
        Required = field.Required;
        ValidationHint = field.ValidationHint;
        Value = field.DefaultValue ?? string.Empty;
        if (field.Kind == OperatorInteractionFieldKind.Boolean
            && bool.TryParse(field.DefaultValue, out var b))
        {
            BoolValue = b;
        }
    }

    public string Id { get; }
    public string Label { get; }
    public OperatorInteractionFieldKind Kind { get; }
    public bool Required { get; }
    public string? ValidationHint { get; }

    public bool IsBoolean => Kind == OperatorInteractionFieldKind.Boolean;
    public bool IsNumber => Kind == OperatorInteractionFieldKind.Number;
    public bool IsTextOrNumber => !IsBoolean;

    [Reactive] private string _value = string.Empty;
    [Reactive] private bool _boolValue;

    public string ToResponseValue()
    {
        if (Kind == OperatorInteractionFieldKind.Boolean)
        {
            return BoolValue ? "true" : "false";
        }

        return Value.Trim();
    }
}
