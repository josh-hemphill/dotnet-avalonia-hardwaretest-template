using HardwareTest.Core.Settings;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Settings;

public partial class SettingsViewModel
{
    [Reactive] private bool _useMockOperatorCredential = true;
    [Reactive] private bool _requireCredentialForOperator;
    [Reactive] private bool _requireAttestationBeforeExport;
    [Reactive] private bool _allowPresenceInLieuOfSigning = true;
    [Reactive] private bool _useMockOperatorCredentialReadOnly;
    [Reactive] private bool _requireCredentialForOperatorReadOnly;
    [Reactive] private bool _requireAttestationBeforeExportReadOnly;
    [Reactive] private bool _allowPresenceInLieuOfSigningReadOnly;

    /// Loads operator-credential flags and env/CLI read-only locks.
    private void InitCredentialSettings(ISettingsStore settingsStore)
    {
        var s = settingsStore.AppSettings;
        UseMockOperatorCredential = s.UseMockOperatorCredential;
        RequireCredentialForOperator = s.RequireCredentialForOperator;
        RequireAttestationBeforeExport = s.RequireAttestationBeforeExport;
        AllowPresenceInLieuOfSigning = s.AllowPresenceInLieuOfSigning;
        UseMockOperatorCredentialReadOnly = settingsStore.IsOverridden(nameof(AppSettings.UseMockOperatorCredential));
        RequireCredentialForOperatorReadOnly = settingsStore.IsOverridden(nameof(AppSettings.RequireCredentialForOperator));
        RequireAttestationBeforeExportReadOnly = settingsStore.IsOverridden(nameof(AppSettings.RequireAttestationBeforeExport));
        AllowPresenceInLieuOfSigningReadOnly = settingsStore.IsOverridden(nameof(AppSettings.AllowPresenceInLieuOfSigning));
    }

    /// Writes writable credential flags onto AppSettings before persist.
    private void ApplyCredentialSettings(AppSettings settings)
    {
        if (!UseMockOperatorCredentialReadOnly)
        {
            settings.UseMockOperatorCredential = UseMockOperatorCredential;
        }

        if (!RequireCredentialForOperatorReadOnly)
        {
            settings.RequireCredentialForOperator = RequireCredentialForOperator;
        }

        if (!RequireAttestationBeforeExportReadOnly)
        {
            settings.RequireAttestationBeforeExport = RequireAttestationBeforeExport;
        }

        if (!AllowPresenceInLieuOfSigningReadOnly)
        {
            settings.AllowPresenceInLieuOfSigning = AllowPresenceInLieuOfSigning;
        }
    }

    private bool IsCredentialPropertyOverridden(string? propertyName)
        => propertyName switch
        {
            nameof(UseMockOperatorCredential) => UseMockOperatorCredentialReadOnly,
            nameof(RequireCredentialForOperator) => RequireCredentialForOperatorReadOnly,
            nameof(RequireAttestationBeforeExport) => RequireAttestationBeforeExportReadOnly,
            nameof(AllowPresenceInLieuOfSigning) => AllowPresenceInLieuOfSigningReadOnly,
            _ => false,
        };
}
