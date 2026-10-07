using System.Globalization;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private AuthoringDocumentState? _pendingNumericState;
    private string NumericText(string field, double? value)
        => SelectedSequence is { } row && SelectedProgram?.AuthoringState.IncompleteNumericText.TryGetValue(
            $"{row.NodeId:D}/{field}", out var text) == true ? text : FormatLimit(value);

    private void SetNumericText(string field, string text, Action<double?> apply)
    {
        if (SelectedProgram is null || SelectedSequence is not { } row) return;
        var key = $"{row.NodeId:D}/{field}";
        var valid = string.IsNullOrWhiteSpace(text) || double.TryParse(text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed);
        var state = SelectedProgram.AuthoringState.Clone();
        if (valid) state.IncompleteNumericText.Remove(key);
        else state.IncompleteNumericText[key] = text;
        if (valid)
        {
            _pendingNumericState = state;
            try { apply(string.IsNullOrWhiteSpace(text) ? null : double.Parse(text, CultureInfo.InvariantCulture)); }
            finally { _pendingNumericState = null; }
        }
        else ReplaceSelected(SelectedProgram with { AuthoringState = state }, rebuildLists: false);
        OnPropertyChanged(field);
    }

    public bool FormulaExplorationOnly
    {
        get => FormulaIntent == FormulaDeploymentIntent.Explore;
        set => FormulaIntent = value ? FormulaDeploymentIntent.Explore : FormulaDeploymentIntent.Deploy;
    }

    public FormulaDeploymentIntent FormulaIntent
    {
        get => SelectedSequence is { } row && SelectedProgram?.AuthoringState.FormulaIntent.TryGetValue(row.NodeId!.Value, out var intent) == true
            ? intent : FormulaDeploymentIntent.Deploy;
        set
        {
            if (SelectedProgram is null || SelectedSequence is not { } row) return;
            var state = SelectedProgram.AuthoringState.Clone();
            state.FormulaIntent[row.NodeId!.Value] = value;
            ReplaceSelected(SelectedProgram with { AuthoringState = state }, rebuildLists: false);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormulaExplorationOnly));
            RaiseFormulaDeployment();
        }
    }
}
