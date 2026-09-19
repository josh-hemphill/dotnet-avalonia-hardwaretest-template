namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private IReadOnlyList<SequenceRow> _sequenceItems = [];
    private int _selectedSequenceIndex = -1;
    private string? _selectedSequenceKey;

    public IReadOnlyList<SequenceRow> SequenceItems => _sequenceItems;

    public string ProgramsTitle => AuthoringChrome.ProgramsTitle;

    public string SequenceTitle => AuthoringChrome.SequenceTitle;

    public string SequencePurpose => AuthoringChrome.SequencePurpose;

    public string InspectorTitle => AuthoringChrome.InspectorTitle;

    public string InspectorPurpose => AuthoringChrome.InspectorPurpose;

    public string PreviewTitle => AuthoringChrome.PreviewTitle;

    public string PreviewPurpose => AuthoringChrome.PreviewPurpose;

    public string ProgramsPurpose => AuthoringChrome.ProgramsPurpose;

    public string ProgramSettingsPurpose => AuthoringChrome.ProgramSettingsPurpose;

    public string CatalogsPurpose => AuthoringChrome.CatalogsPurpose;

    public string RequiredFieldsPurpose => AuthoringChrome.RequiredFieldsPurpose;

    public string AddRecipeToolTip => AuthoringChrome.AddRecipeToolTip;

    public string ProgramSettingsTitle => AuthoringChrome.ProgramSettingsTitle;

    public string SettingsTitle => AuthoringChrome.SettingsTitle;

    public string SettingsPurpose => AuthoringChrome.SettingsPurpose;

    public string LastWorkspaceOffer => AuthoringChrome.LastWorkspaceOffer;

    public string RemoveSelectedTitle => AuthoringChrome.RemoveSelectedTitle;

    public string RemoveSelectedPurpose => AuthoringChrome.RemoveSelectedPurpose;

    public string RemoveProgramTitle => AuthoringChrome.RemoveProgramTitle;

    public string RemoveProgramPurpose => AuthoringChrome.RemoveProgramPurpose;

    public bool CanRemoveSelectedSequence
        => AuthoringSequence.CanRemove(SelectedSequence, SelectedProgram);

    public bool CanRemoveSelectedProgram
        => Workspace is not null && SelectedProgram is not null && !Workspace.IsReadOnly;

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

    public string InspectorBreadcrumb
        => SelectedSequence is null
            ? InspectorPurpose
            : $"{SelectedSequence.Section} › {SelectedSequence.Label}";

    public string RepeatCount
    {
        get => SelectedRepeat is { } repeat ? repeat.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        set
        {
            if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count)
                || count < 1
                || SelectedSequence is not { Kind: SequenceRowKind.Repeat } row
                || SelectedProgram is null
                || SelectedRepeat is { Count: var current } && current == count)
            {
                return;
            }

            var measure = AuthoringSequence.MutateMeasure(
                SelectedProgram.Measure,
                row.IndexPath,
                node => node is RepeatNode repeat ? repeat with { Count = count } : node);
            ReplaceSelected(SelectedProgram with { Measure = measure }, rebuildLists: false);
        }
    }

    public void RemoveSelectedSequence()
    {
        if (SelectedProgram is null || !CanRemoveSelectedSequence || SelectedSequence is not { } row)
        {
            return;
        }

        var label = row.Label;
        if (row.Kind == SequenceRowKind.Setup && row.IndexPath.Count == 1)
        {
            var index = row.IndexPath[0];
            if (index < 0 || index >= SelectedProgram.Setup.Count)
            {
                return;
            }

            ReplaceSelected(SelectedProgram with
            {
                Setup = [.. SelectedProgram.Setup.Where((_, i) => i != index)],
            });
        }
        else if (row.Kind is SequenceRowKind.Metric or SequenceRowKind.Repeat or SequenceRowKind.Raw)
        {
            ReplaceSelected(SelectedProgram with
            {
                Measure = AuthoringSequence.RemoveMeasure(SelectedProgram.Measure, row.IndexPath),
            });
        }
        else if (row.Kind == SequenceRowKind.Cleanup)
        {
            ReplaceSelected(SelectedProgram with
            {
                Cleanup = SelectedProgram.Cleanup with { IncludeSafeShutdown = false },
            });
        }
        else
        {
            return;
        }

        Status = $"Removed {label}";
        Error = null;
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

        if (index < 0)
        {
            if (_selectedSequenceIndex >= 0)
            {
                OnPropertyChanged(nameof(SelectedSequenceIndex));
            }

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
        var next = AuthoringSequence.Flatten(SelectedProgram);
        var structureChanged = !AuthoringSequence.SameKeys(_sequenceItems, next);
        _sequenceItems = next;
        if (structureChanged)
        {
            OnPropertyChanged(nameof(SequenceItems));
        }

        RaiseRawStepProperties();

        var restored = AuthoringSequence.IndexOfKey(_sequenceItems, _selectedSequenceKey);
        if (restored < 0)
        {
            restored = AuthoringSequence.IndexOfTopLevelMeasure(_sequenceItems, _selectedMeasureIndex);
        }

        if (restored < 0)
        {
            restored = AuthoringSequence.FirstSelectableIndex(_sequenceItems);
        }

        var indexChanged = _selectedSequenceIndex != restored;
        _selectedSequenceIndex = restored;
        _selectedSequenceKey = restored < 0 ? null : _sequenceItems[restored].Key;
        if (restored >= 0)
        {
            SyncMeasureIndexFromSequence(_sequenceItems[restored]);
        }

        if (indexChanged)
        {
            OnPropertyChanged(nameof(SelectedSequenceIndex));
        }

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
        var section = from >= 0 && from < _sequenceItems.Count
            ? _sequenceItems[from].Section
            : (SequenceSection?)null;
        var sameSection = FindNearestSelectable(from, row => row.Section == section);
        return sameSection >= 0
            ? sameSection
            : FindNearestSelectable(from, _ => true);
    }

    private int FindNearestSelectable(int from, Func<SequenceRow, bool> match)
    {
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < _sequenceItems.Count; i++)
        {
            if (!_sequenceItems[i].IsSelectable || !match(_sequenceItems[i]))
            {
                continue;
            }

            var distance = Math.Abs(i - from);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }
}
