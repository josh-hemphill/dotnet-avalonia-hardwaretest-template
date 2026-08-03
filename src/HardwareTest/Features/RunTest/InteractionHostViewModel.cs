using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Mid-run operator interaction card: prompt, typed fields and response validation.
public partial class InteractionHostViewModel : ReactiveObject
{
    public ObservableCollection<InteractionFieldViewModel> InteractionFields { get; } = [];

    [Reactive] private bool _isAwaitingOperator;
    [Reactive] private string? _operatorPromptMessage;
    [Reactive] private string _interactionTitle = "Operator attention";
    [Reactive] private bool _hasInteractionFields;
    [Reactive] private string? _interactionValidationError;

    /// Shows the prompt card for a host request, falling back to a plain progress message.
    public void Apply(OperatorInteractionRequest? request, string? fallbackMessage)
    {
        InteractionValidationError = null;
        InteractionTitle = request?.Title ?? "Operator attention";
        OperatorPromptMessage = request?.Message ?? fallbackMessage;
        InteractionFields.Clear();
        if (request?.Fields is { Count: > 0 })
        {
            foreach (var field in request.Fields)
            {
                InteractionFields.Add(new InteractionFieldViewModel(field));
            }
        }

        HasInteractionFields = InteractionFields.Count > 0;
    }

    public void Clear()
    {
        InteractionFields.Clear();
        HasInteractionFields = false;
        InteractionTitle = "Operator attention";
        InteractionValidationError = null;
    }

    /// Validates required fields and returns the values to send back to the host.
    /// Sets <see cref="InteractionValidationError"/> and returns false when a field is unusable.
    public bool TryCollectResponse(
        OperatorInteractionRequest? request,
        out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request is not null && InteractionFields.Count > 0)
        {
            foreach (var field in InteractionFields.Where(f => f.Required))
            {
                if (field.IsBoolean)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(field.Value))
                {
                    InteractionValidationError = $"{field.Label} is required.";
                    return false;
                }

                if (field.Kind == OperatorInteractionFieldKind.Number
                    && !double.TryParse(
                        field.Value.Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out _))
                {
                    InteractionValidationError = $"{field.Label} must be a number.";
                    return false;
                }
            }
        }

        InteractionValidationError = null;
        values = InteractionFields.ToDictionary(
            f => f.Id,
            f => f.ToResponseValue(),
            StringComparer.OrdinalIgnoreCase);
        return true;
    }
}
