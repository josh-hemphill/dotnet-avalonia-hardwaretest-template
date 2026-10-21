namespace HardwareTest.Authoring;

public sealed record AuthoringSectionDestination(Guid? NodeId, string Name);

public sealed partial class AuthoringWorkspaceViewModel
{
    private readonly HashSet<Guid> _collapsedSectionIds = [];
    private string _newSectionName = string.Empty;
    private AuthoringSectionDestination? _selectedSectionDestination;
    public string NewSectionName { get => _newSectionName; set => SetField(ref _newSectionName, value); }
    public bool HasSectionEditor => SelectedMeasure is SectionNode;
    public bool CanToggleSelectedSection => SelectedSequence?.IsSection == true;
    public string SelectedSectionToggleLabel => SelectedSequence?.SectionToggleLabel ?? "Collapse section";
    public IReadOnlyList<AuthoringSectionDestination> SectionDestinations =>
        [new(null, "Measure — outside sections"), .. AuthoringSequence.Flatten(SelectedProgram)
            .Where(row => row.IsSection && (SelectedSequence is not { } selected ||
                !row.IndexPath.Take(selected.IndexPath.Count).SequenceEqual(selected.IndexPath)))
            .Select(row => new AuthoringSectionDestination(row.NodeId, new string(' ', row.Depth * 2) + row.Label))];
    public AuthoringSectionDestination? SelectedSectionDestination
    {
        get => SectionDestinations.FirstOrDefault(choice => choice.NodeId == _selectedSectionDestination?.NodeId) ?? SectionDestinations[0];
        set { if (SetField(ref _selectedSectionDestination, value)) OnPropertyChanged(nameof(CanMoveToSection)); }
    }
    public bool CanMoveToSection => CanEditSequence && SelectedSequence?.Section == SequenceSection.Measure && SelectedSectionDestination is not null;
    public void AddSection() => SequenceTransaction(draft => AuthoringSequenceOperations.AddSection(draft, NewSectionName), "Section added");
    public void MoveSelectedToSection()
    {
        var destination = SelectedSectionDestination?.NodeId;
        if (destination is { } id) _collapsedSectionIds.Remove(id);
        SequenceTransaction(draft => AuthoringSequenceOperations.MoveToSection(draft,
            SelectedSequence ?? throw new AuthoringWorkspaceException("Select a step or section."), destination), "Moved to section");
    }
    public void ToggleSelectedSection()
    {
        if (SelectedSequence is not { IsSection: true, NodeId: { } id }) return;
        if (!_collapsedSectionIds.Add(id)) _collapsedSectionIds.Remove(id);
        RefreshSequencePresentation();
        RaiseEditorProperties();
    }
    public void ExpandAllSections()
    {
        _collapsedSectionIds.Clear();
        RefreshSequencePresentation();
        RaiseEditorProperties();
    }
    private IReadOnlyList<SequenceRow> VisibleSequence(IReadOnlyList<SequenceRow> rows)
    {
        var hiddenParents = rows.Where(row => row.IsSection && row.NodeId is { } id && _collapsedSectionIds.Contains(id)).ToArray();
        foreach (var row in rows) row.IsExpanded = row.NodeId is not { } id || !_collapsedSectionIds.Contains(id);
        return rows.Where(row => row.Section != SequenceSection.Measure || !hiddenParents.Any(parent =>
            row.IndexPath.Count > parent.IndexPath.Count && row.IndexPath.Take(parent.IndexPath.Count).SequenceEqual(parent.IndexPath))).ToArray();
    }
    private SequenceRow? EndOfSelectedSection()
    {
        if (SelectedSequence is not { Section: SequenceSection.Measure } selected) return null;
        return AuthoringSequence.Flatten(SelectedProgram).LastOrDefault(row => row.IsSection &&
            row.IndexPath.Count <= selected.IndexPath.Count && selected.IndexPath.Take(row.IndexPath.Count).SequenceEqual(row.IndexPath));
    }
    private void RaiseSectionProperties()
    {
        OnPropertyChanged(nameof(HasSectionEditor));
        OnPropertyChanged(nameof(SectionDestinations));
        OnPropertyChanged(nameof(SelectedSectionDestination));
        OnPropertyChanged(nameof(CanMoveToSection));
        OnPropertyChanged(nameof(CanToggleSelectedSection));
        OnPropertyChanged(nameof(SelectedSectionToggleLabel));
    }
}
