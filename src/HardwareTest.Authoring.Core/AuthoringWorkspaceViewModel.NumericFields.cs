using System.Globalization;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private string NumericFieldText(Guid nodeId, string field, string formatted)
        => SelectedProgram?.AuthoringState.IncompleteNumericText.TryGetValue(
            AuthoringDocumentState.FieldKey(nodeId, field), out var text) == true ? text : formatted;

    private string SelectedFieldText(string field, string formatted)
        => SelectedSequence?.NodeId is { } nodeId ? NumericFieldText(nodeId, field, formatted) : formatted;

    private void SetNumericFieldText(Guid nodeId, string field, string text, bool valid,
        Action<AuthoringDocumentState> apply)
    {
        if (SelectedProgram is null) return;
        var state = SelectedProgram.AuthoringState.Clone();
        var key = AuthoringDocumentState.FieldKey(nodeId, field);
        if (valid) state.IncompleteNumericText.Remove(key);
        else state.IncompleteNumericText[key] = text;
        if (valid) apply(state);
        else ReplaceSelected(SelectedProgram with { AuthoringState = state }, rebuildLists: false);
        OnPropertyChanged(field);
    }

    private void ApplyNumericTf(AuthoringDocumentState state,
        Func<TransferFunctionAlgorithm, TransferFunctionAlgorithm> mutate)
    {
        _pendingNumericState = state;
        try { UpdateSelectedTf(mutate); }
        finally { _pendingNumericState = null; }
    }

    private void SetTfVectorText(string field, string text,
        Func<TransferFunctionAlgorithm, IReadOnlyList<double>, TransferFunctionAlgorithm> mutate)
    {
        if (SelectedTf is null || SelectedSequence?.NodeId is not { } nodeId) return;
        var empty = string.IsNullOrWhiteSpace(text);
        var valid = TryParseFiniteVector(text, out var vector);
        SetNumericFieldText(nodeId, field, text, empty || valid,
            state => ApplyNumericTf(state, tf => empty ? tf : mutate(tf, vector)));
    }

    private void SetTfSamplePeriodText(string text)
    {
        if (SelectedTf is null || SelectedSequence?.NodeId is not { } nodeId) return;
        var empty = string.IsNullOrWhiteSpace(text);
        var valid = TryPositiveFiniteNumber(text, out var ts);
        SetNumericFieldText(nodeId, nameof(TfTsSeconds), text, empty || valid,
            state => ApplyNumericTf(state, tf => empty ? tf : tf with { TsSeconds = ts }));
    }

    private void SetStationHealthAgeText(string text)
    {
        if (SelectedProgram is not { } program) return;
        var empty = string.IsNullOrWhiteSpace(text);
        var valid = TryPositiveFiniteNumber(text, out var hours);
        SetNumericFieldText(program.Cleanup.NodeId, nameof(StationHealthMaxAgeHours), text, empty || valid, state =>
        {
            var sidecar = PlanCompiler.CloneSidecar(program.Sidecar);
            sidecar.StationHealthMaxAgeHours = empty ? null : hours;
            ReplaceSelected(program with { Sidecar = sidecar, AuthoringState = state }, rebuildLists: false);
        });
    }

    private static bool TryPositiveFiniteNumber(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           && double.IsFinite(value) && value > 0;

    private static bool TryParseFiniteVector(string text, out IReadOnlyList<double> vector)
    {
        var parts = text.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numbers = new double[parts.Length];
        vector = numbers;
        if (parts.Length == 0) return false;
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i])
                || !double.IsFinite(numbers[i])) return false;
        return true;
    }
}
