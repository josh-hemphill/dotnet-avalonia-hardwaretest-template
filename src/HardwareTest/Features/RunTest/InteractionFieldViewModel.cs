using HardwareTest.OpenTap.Plugins.Basic;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Editable field for operator interactions and plan/step parameters.
public partial class InteractionFieldViewModel : ReactiveObject
{
    public InteractionFieldViewModel(OperatorInteractionField field, bool isReadOnly = false)
    {
        Id = field.Id;
        Label = field.Label;
        Kind = field.Kind;
        Required = field.Required;
        ValidationHint = field.ValidationHint;
        IsReadOnly = isReadOnly;
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
    public bool IsReadOnly { get; }

    public bool IsBoolean => Kind == OperatorInteractionFieldKind.Boolean;
    public bool IsNumber => Kind == OperatorInteractionFieldKind.Number;
    public bool IsTextOrNumber => !IsBoolean;

    /// Label with a required marker so operators do not rely on asterisk-only placeholders.
    public string DisplayLabel => Required ? $"{Label} *" : Label;

    public string AutomationName => DisplayLabel;

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
