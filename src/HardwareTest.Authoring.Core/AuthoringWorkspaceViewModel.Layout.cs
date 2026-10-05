namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public bool ProgramsRailCollapsed
    {
        get => Prefs.ProgramsRailCollapsed;
        set { if (PreferencesReadOnly || Prefs.ProgramsRailCollapsed == value) return; Prefs.ProgramsRailCollapsed = value; PersistPreferences(); OnPropertyChanged(); }
    }
    public bool DockPreview
    {
        get => Prefs.DockPreview;
        set { if (PreferencesReadOnly || Prefs.DockPreview == value) return; Prefs.DockPreview = value; PersistPreferences(); OnPropertyChanged(); }
    }
    public bool IssuesDrawerOpen
    {
        get => Prefs.IssuesDrawerOpen;
        set { if (PreferencesReadOnly || Prefs.IssuesDrawerOpen == value) return; Prefs.IssuesDrawerOpen = value; PersistPreferences(); OnPropertyChanged(); }
    }
    public void ResetLayout() { ProgramsRailCollapsed = false; DockPreview = true; IssuesDrawerOpen = false; }
    public string SelectedDependencySummary
    {
        get
        {
            if (SelectedProgram is not { } program || SelectedSequence?.NodeId is not { } id) return "Select a step to see its dependencies.";
            var index = AuthoringDependencyIndex.Build(program);
            var node = index.Nodes.FirstOrDefault(candidate => candidate.NodeId == id);
            if (node is null) return "No selected dependency target.";
            return $"Produces: {node.ProducedChannel ?? "none"} · Inputs: {string.Join(", ", node.InputChannels.DefaultIfEmpty("none"))} · Instruments: {string.Join(", ", node.InstrumentSlots.DefaultIfEmpty("none"))}"
                + (node.IsOpaque ? " · References inside this raw/unsupported step cannot be inspected." : "");
        }
    }

    public void RefreshExternalCompiledChanges()
    {
        if (Workspace is null) return;
        foreach (var pair in _sourceDocuments)
            if (CompiledChanged(pair.Value)) _compiledConflicts.Add(pair.Key);
        InvalidateContractFindings();
        RaiseDraftState();
    }
}
