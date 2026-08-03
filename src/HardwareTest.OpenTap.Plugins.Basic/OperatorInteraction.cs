namespace HardwareTest.OpenTap.Plugins.Basic;

public enum OperatorInteractionFieldKind
{
    String,
    Number,
    Boolean,
}

/// One editable field on an operator interaction (confirm-only requests have none).
public sealed class OperatorInteractionField
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public OperatorInteractionFieldKind Kind { get; init; } = OperatorInteractionFieldKind.String;
    public string? DefaultValue { get; init; }
    public bool Required { get; init; }
    public string? ValidationHint { get; init; }
}

/// Request from a plan step to the host/UI. Empty Fields = confirm-only.
/// Called on the OpenTAP plan thread — must not touch Avalonia controls directly.
public sealed class OperatorInteractionRequest
{
    public required string Id { get; init; }
    public string Title { get; init; } = "Operator attention";
    public required string Message { get; init; }
    public IReadOnlyList<OperatorInteractionField> Fields { get; init; } = [];

    public static OperatorInteractionRequest ConfirmOnly(string message, string? title = null)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title ?? "Operator attention",
            Message = message,
            Fields = [],
        };
}

/// Response from the host/UI after Continue or cancel.
public sealed class OperatorInteractionResponse
{
    public required string RequestId { get; init; }
    public bool Cancelled { get; init; }
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static OperatorInteractionResponse Continue(string requestId, Dictionary<string, string>? values = null)
        => new()
        {
            RequestId = requestId,
            Cancelled = false,
            Values = values ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

    public static OperatorInteractionResponse Cancel(string requestId)
        => new() { RequestId = requestId, Cancelled = true };
}
