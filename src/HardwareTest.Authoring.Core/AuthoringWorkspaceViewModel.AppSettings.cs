namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private readonly AuthoringPreferences _ephemeralPreferences = new();
    private int _formulaCaret;

    /// Raised after a persisted theme change so the exe can map System/Light/Dark to ThemeVariant.
    public event Action<string>? ThemePreferenceChanged;

    private AuthoringPreferences Prefs => _preferences?.Current ?? _ephemeralPreferences;

    public IReadOnlyList<string> ThemeOptions => AuthoringThemePreference.Options;

    public bool PreferencesReadOnly => _preferences?.IsReadOnly == true;

    public bool PreferencesEditable => !PreferencesReadOnly;

    public string ThemePreference
    {
        get => AuthoringThemePreference.Normalize(Prefs.ThemePreference);
        set
        {
            var theme = AuthoringThemePreference.Normalize(value);
            if (PreferencesReadOnly || string.Equals(Prefs.ThemePreference, theme, StringComparison.Ordinal))
            {
                return;
            }

            Prefs.ThemePreference = theme;
            PersistPreferences();
            OnPropertyChanged();
            ThemePreferenceChanged?.Invoke(theme);
        }
    }

    public string OpenTapHomeOverride
    {
        get => Prefs.OpenTapHomeOverride ?? string.Empty;
        set
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (PreferencesReadOnly || string.Equals(Prefs.OpenTapHomeOverride, trimmed, StringComparison.Ordinal))
            {
                return;
            }

            Prefs.OpenTapHomeOverride = trimmed;
            PersistPreferences();
            OnPropertyChanged();
        }
    }

    public bool ShowRawStepXml
    {
        get => Prefs.ShowRawStepXml;
        set
        {
            if (PreferencesReadOnly || Prefs.ShowRawStepXml == value)
            {
                return;
            }

            Prefs.ShowRawStepXml = value;
            PersistPreferences();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRawStepEditor));
        }
    }

    public bool HasRawStepEditor => HasRawStep && ShowRawStepXml;

    public string LastWorkspacePath => Prefs.LastWorkspace ?? string.Empty;

    public bool CanOfferLastWorkspace
        => Workspace is null
           && !string.IsNullOrWhiteSpace(Prefs.LastWorkspace)
           && Directory.Exists(Prefs.LastWorkspace);

    public IReadOnlyList<FormulaCatalog.Item> FormulaPrefixCompletions
    {
        get
        {
            if (!HasFormula)
            {
                return [];
            }

            var ident = FormulaCatalog.IdentAt(FormulaSource, _formulaCaret).Text;
            return string.IsNullOrEmpty(ident)
                ? []
                : FormulaCatalog.CompletionsFor(ident, ChannelKeys);
        }
    }

    public bool HasFormulaPrefixCompletions => FormulaPrefixCompletions.Count > 0;

    public void RefreshFormulaCompletions(int caret)
    {
        _formulaCaret = Math.Max(0, caret);
        OnPropertyChanged(nameof(FormulaPrefixCompletions));
        OnPropertyChanged(nameof(HasFormulaPrefixCompletions));
    }

    public void OpenLastWorkspace()
    {
        if (!CanOfferLastWorkspace)
        {
            return;
        }

        Open(Prefs.LastWorkspace!);
    }

    public InstrumentRef? SelectedInstrument
    {
        get => Instruments.FirstOrDefault(instrument =>
            string.Equals(instrument.SlotName, SelectedInstrumentSlot, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null
                || string.Equals(SelectedInstrumentSlot, value.SlotName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SelectedInstrumentSlot = value.SlotName;
        }
    }

    internal BootstrapOptions ResolveBootstrapOptions(BootstrapOptions? options)
    {
        var home = options?.HomeDirectory;
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Prefs.OpenTapHomeOverride;
        }

        return new BootstrapOptions
        {
            HomeDirectory = string.IsNullOrWhiteSpace(home) ? null : home,
            InstrumentComponentsPackagePath = options?.InstrumentComponentsPackagePath,
            TuiPackagePath = options?.TuiPackagePath,
            Offline = options?.Offline ?? true,
        };
    }

    private void RememberLastWorkspace(string root)
    {
        var full = Path.GetFullPath(root);
        if (string.Equals(Prefs.LastWorkspace, full, StringComparison.OrdinalIgnoreCase))
        {
            OnPropertyChanged(nameof(LastWorkspacePath));
            OnPropertyChanged(nameof(CanOfferLastWorkspace));
            return;
        }

        if (!PreferencesReadOnly)
        {
            Prefs.LastWorkspace = full;
            PersistPreferences();
        }
        OnPropertyChanged(nameof(LastWorkspacePath));
        OnPropertyChanged(nameof(CanOfferLastWorkspace));
    }

    private void PersistPreferences()
    {
        if (_preferences is null || _preferences.IsReadOnly)
        {
            return;
        }

        try
        {
            _preferences.Save();
        }
        catch (Exception ex)
        {
            ReportError(ex.Message);
        }
    }
}
