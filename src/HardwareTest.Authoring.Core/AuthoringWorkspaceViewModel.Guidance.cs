namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public bool SkipGuidance
    {
        get => Prefs.SkipGuidance;
        set
        {
            if (PreferencesReadOnly || Prefs.SkipGuidance == value) return;
            Prefs.SkipGuidance = value;
            PersistPreferences();
            OnPropertyChanged();
        }
    }
}
