namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private IReadOnlyList<SequenceRow> _sequenceItems = [];
    private int _selectedSequenceIndex = -1;
    private string? _selectedSequenceKey;

    public IReadOnlyList<SequenceRow> SequenceItems => _sequenceItems;

    public string SequenceTitle => AuthoringChrome.SequenceTitle;

    public string SequencePurpose => AuthoringChrome.SequencePurpose;

    public string InspectorTitle => AuthoringChrome.InspectorTitle;

    public string InspectorPurpose => AuthoringChrome.InspectorPurpose;

    public string PreviewTitle => AuthoringChrome.PreviewTitle;

    public string PreviewPurpose => AuthoringChrome.PreviewPurpose;

    public string ProgramsPurpose => AuthoringChrome.ProgramsPurpose;

    public string ProgramSettingsPurpose => AuthoringChrome.ProgramSettingsPurpose;

    public int SelectedSequenceIndex
    {
        get => _selectedSequenceIndex;
        set => SelectSequence(value);
    }

    public SequenceRow? SelectedSequence
        => _selectedSequenceIndex < 0 || _selectedSequenceIndex >= _sequenceItems.Count
            ? null
            : _sequenceItems[_selectedSequenceIndex];

    public RepeatNode? SelectedRepeat
        => SelectedMeasure is RepeatNode repeat ? repeat : null;

    public SetupAction? SelectedSetup
        => SelectedProgram is null || SelectedSequence is not { Section: SequenceSection.Setup } row
            ? null
            : AuthoringSequence.ResolveSetup(SelectedProgram, row.IndexPath);

    public string RepeatCount
    {
        get => SelectedRepeat is { } repeat ? repeat.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        set
        {
            if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count)
                || count < 1
                || SelectedSequence is not { Kind: SequenceRowKind.Repeat } row
                || SelectedProgram is null)
            {
                return;
            }

            var measure = AuthoringSequence.MutateMeasure(
                SelectedProgram.Measure,
                row.IndexPath,
                node => node is RepeatNode repeat ? repeat with { Count = count } : node);
            ReplaceSelected(SelectedProgram with { Measure = measure });
        }
    }

    public void SelectSequence(int index)
    {
        if (_sequenceItems.Count == 0)
        {
            _selectedSequenceIndex = -1;
            _selectedSequenceKey = null;
            OnPropertyChanged(nameof(SelectedSequenceIndex));
            OnPropertyChanged(nameof(SelectedSequence));
            RaiseEditorProperties();
            return;
        }

        var clamped = Math.Clamp(index, 0, _sequenceItems.Count - 1);
        if (!_sequenceItems[clamped].IsSelectable)
        {
            var fallback = NearestSelectableIndex(clamped);
            if (fallback < 0)
            {
                _selectedSequenceIndex = -1;
                _selectedSequenceKey = null;
                OnPropertyChanged(nameof(SelectedSequenceIndex));
                OnPropertyChanged(nameof(SelectedSequence));
                RaiseEditorProperties();
                return;
            }

            clamped = fallback;
        }

        if (_selectedSequenceIndex == clamped
            && string.Equals(_selectedSequenceKey, _sequenceItems[clamped].Key, StringComparison.Ordinal))
        {
            SyncMeasureIndexFromSequence(_sequenceItems[clamped]);
            RaiseEditorProperties();
            return;
        }

        _selectedSequenceIndex = clamped;
        _selectedSequenceKey = _sequenceItems[clamped].Key;
        SyncMeasureIndexFromSequence(_sequenceItems[clamped]);
        OnPropertyChanged(nameof(SelectedSequenceIndex));
        OnPropertyChanged(nameof(SelectedSequence));
        RaiseEditorProperties();
    }

    private void RefreshSequencePresentation()
    {
        _sequenceItems = AuthoringSequence.Flatten(SelectedProgram);
        OnPropertyChanged(nameof(SequenceItems));
        var restored = AuthoringSequence.IndexOfKey(_sequenceItems, _selectedSequenceKey);
        if (restored < 0)
        {
            restored = AuthoringSequence.IndexOfTopLevelMeasure(_sequenceItems, _selectedMeasureIndex);
        }

        if (restored < 0)
        {
            restored = AuthoringSequence.FirstSelectableIndex(_sequenceItems);
        }

        _selectedSequenceIndex = restored;
        _selectedSequenceKey = restored < 0 ? null : _sequenceItems[restored].Key;
        if (restored >= 0)
        {
            SyncMeasureIndexFromSequence(_sequenceItems[restored]);
        }

        OnPropertyChanged(nameof(SelectedSequenceIndex));
        OnPropertyChanged(nameof(SelectedSequence));
    }

    private void SyncMeasureIndexFromSequence(SequenceRow row)
    {
        if (row.Section != SequenceSection.Measure || row.IndexPath.Count == 0)
        {
            return;
        }

        _selectedMeasureIndex = row.IndexPath[0];
        OnPropertyChanged(nameof(SelectedMeasureIndex));
    }

    private int NearestSelectableIndex(int from)
    {
        for (var i = from + 1; i < _sequenceItems.Count; i++)
        {
            if (_sequenceItems[i].IsSelectable)
            {
                return i;
            }
        }

        for (var i = from - 1; i >= 0; i--)
        {
            if (_sequenceItems[i].IsSelectable)
            {
                return i;
            }
        }

        return -1;
    }
}
