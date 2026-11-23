using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

    private string? _requestId;
    private InteractionFieldViewModel? _invalidChoice;

    internal Guid? ProducerStepId { get; private set; }
    internal string? ProducerStepName { get; private set; }

    [Reactive] private bool _isAwaitingOperator;
    [Reactive] private string? _operatorPromptMessage;
    [Reactive] private string _interactionTitle = "Operator attention";
    [Reactive] private bool _hasInteractionFields;
    [Reactive] private string? _interactionValidationError;

    /// Raised after fields are rebuilt so the view can focus the first editable control.
    public event EventHandler? RequestFocusFirstField;
    public event Action<string>? RequestFocusField;

    /// Shows the prompt card for a host request, falling back to a plain progress message.
    public void Apply(OperatorInteractionRequest? request, string? fallbackMessage)
    {
        if (request is not null && request.Id == _requestId)
        {
            return;
        }

        _requestId = request?.Id;
        ProducerStepId = request?.ProducerStepId;
        ProducerStepName = request?.ProducerStepName;
        InteractionValidationError = null;
        InteractionTitle = request?.Title ?? "Operator attention";
        OperatorPromptMessage = request?.Message ?? fallbackMessage;
        ClearFields();
        if (request?.Fields is { Count: > 0 })
        {
            foreach (var field in request.Fields)
            {
                var editor = new InteractionFieldViewModel(field);
                if (editor.IsChoice)
                    editor.PropertyChanged += OnChoiceChanged;
                InteractionFields.Add(editor);
            }
        }

        HasInteractionFields = InteractionFields.Count > 0;
        RequestFocusFirstField?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _requestId = null;
        ProducerStepId = null;
        ProducerStepName = null;
        ClearFields();
        HasInteractionFields = false;
        InteractionTitle = "Operator attention";
        InteractionValidationError = null;
    }

    /// Validates fields and returns the values to send back to the host.
    /// Required text must be present, required booleans must be checked, and any filled
    /// number (required or optional) must parse. Sets <see cref="InteractionValidationError"/>
    /// and returns false when a field is unusable.
    public bool TryCollectResponse(
        OperatorInteractionRequest? request,
        out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request?.Id != _requestId)
        {
            if (request is not null)
                Apply(request, fallbackMessage: null);

            InteractionValidationError = "The prompt changed. Review the current request before continuing.";
            return false;
        }

        _invalidChoice = null;
        foreach (var field in InteractionFields) field.ValidationError = string.Empty;
        if (request is not null && InteractionFields.Count > 0)
        {
            foreach (var field in InteractionFields)
            {
                if (field.IsBoolean)
                {
                    if (field.Required && !field.BoolValue)
                    {
                        RejectField(field, $"{field.Label} is required.");
                        return false;
                    }

                    continue;
                }

                var trimmed = field.ToResponseValue();
                if (field.IsChoice && (field.Required || trimmed.Length > 0) &&
                    !field.Choices.Contains(trimmed, StringComparer.Ordinal))
                {
                    _invalidChoice = field;
                    RejectField(field, $"Select an option for {field.Label.ToLowerInvariant()} before continuing.");
                    return false;
                }

                if (field.Required && string.IsNullOrWhiteSpace(trimmed))
                {
                    RejectField(field, $"{field.Label} is required.");
                    return false;
                }

                if (field.Kind == OperatorInteractionFieldKind.Number
                    && !string.IsNullOrWhiteSpace(trimmed)
                    && !double.TryParse(
                        trimmed,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out _))
                {
                    RejectField(field, $"{field.Label} must be a number.");
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

    private void RejectField(InteractionFieldViewModel field, string message)
    {
        field.ValidationError = message;
        InteractionValidationError = message;
        RequestFocusField?.Invoke(field.Id);
    }

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(InteractionFieldViewModel.SelectedChoice)
            && _invalidChoice is { SelectedChoice: { } selected } field
            && ReferenceEquals(sender, field)
            && field.Choices.Contains(selected, StringComparer.Ordinal))
        {
            _invalidChoice = null;
            field.ValidationError = string.Empty;
            InteractionValidationError = null;
        }
    }

    private void ClearFields()
    {
        foreach (var field in InteractionFields)
            field.PropertyChanged -= OnChoiceChanged;
        InteractionFields.Clear();
        _invalidChoice = null;
    }
}
