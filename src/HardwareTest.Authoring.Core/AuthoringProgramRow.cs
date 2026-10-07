using System.ComponentModel;

namespace HardwareTest.Authoring;

public sealed class AuthoringProgramRow(string planId) : INotifyPropertyChanged
{
    public string PlanId { get; } = planId;
    public string DisplayName { get; private set; } = string.Empty;
    public string DirtyMarker { get; private set; } = string.Empty;
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Update(ProgramDraft draft, bool planDirty, bool sidecarDirty)
    {
        var marker = planDirty ? "Unsaved plan" : sidecarDirty ? "Unsaved settings" : string.Empty;
        if (DisplayName != draft.Sidecar.DisplayName)
        {
            DisplayName = draft.Sidecar.DisplayName ?? string.Empty;
            PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
        }
        if (DirtyMarker != marker)
        {
            DirtyMarker = marker;
            PropertyChanged?.Invoke(this, new(nameof(DirtyMarker)));
        }
    }
}

public sealed partial class AuthoringWorkspaceViewModel
{
    private IReadOnlyList<AuthoringProgramRow> _programRows = [];
    public IReadOnlyList<AuthoringProgramRow> ProgramRows => _programRows;
    public AuthoringProgramRow? SelectedProgramRow
    {
        get => ProgramRows.FirstOrDefault(row => string.Equals(row.PlanId, SelectedProgram?.PlanId, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null && !string.Equals(value.PlanId, SelectedProgram?.PlanId, StringComparison.OrdinalIgnoreCase))
                SelectProgram(value.PlanId);
        }
    }

    private void RefreshProgramRows()
    {
        var rows = Programs.Select(draft =>
        {
            var row = _programRows.FirstOrDefault(existing => string.Equals(existing.PlanId, draft.PlanId, StringComparison.OrdinalIgnoreCase))
                ?? new AuthoringProgramRow(draft.PlanId);
            row.Update(draft, _dirtyPlans.Contains(draft.PlanId), _dirtySidecars.Contains(draft.PlanId));
            return row;
        }).ToArray();
        if (!_programRows.SequenceEqual(rows))
        {
            _programRows = rows;
            OnPropertyChanged(nameof(ProgramRows));
            OnPropertyChanged(nameof(SelectedProgramRow));
        }
    }
}
