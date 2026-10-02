using HardwareTest.Core.Credentials;
using HardwareTest.Core.Settings;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Settings;

public partial class SettingsViewModel
{
    [Reactive] private SmartCardSigningProviderMode _smartCardSigningProviderMode;
    [Reactive] private string _pkcs11LibraryPath = string.Empty;
    [Reactive] private bool _smartCardSigningProviderModeReadOnly;
    [Reactive] private bool _pkcs11LibraryPathReadOnly;
    [Reactive] private string _signingSetupStatus = string.Empty;
    public IReadOnlyList<SmartCardSigningProviderMode> SigningProviderModes { get; } = Enum.GetValues<SmartCardSigningProviderMode>();
    public ReactiveCommand<RxVoid, RxVoid> CheckSigningSetupCommand { get; private set; } = null!;
    [Reactive] private bool _useMockOperatorCredential = true;
    [Reactive] private bool _requireCredentialForOperator;
    [Reactive] private bool _requireAttestationBeforeExport;
    [Reactive] private bool _allowPresenceInLieuOfSigning = true;
    [Reactive] private bool _probeBadgeWhenTechnicianFocused;
    [Reactive] private bool _useMockOperatorCredentialReadOnly;
    [Reactive] private bool _requireCredentialForOperatorReadOnly;
    [Reactive] private bool _requireAttestationBeforeExportReadOnly;
    [Reactive] private bool _allowPresenceInLieuOfSigningReadOnly;
    [Reactive] private bool _probeBadgeWhenTechnicianFocusedReadOnly;

    /// Loads operator-credential flags and env/CLI read-only locks.
    private void InitCredentialSettings(ISettingsStore settingsStore)
    {
        var s = settingsStore.AppSettings;
        SmartCardSigningProviderMode = s.SmartCardSigningProviderMode;
        Pkcs11LibraryPath = s.Pkcs11LibraryPath;
        SmartCardSigningProviderModeReadOnly = settingsStore.IsOverridden(nameof(AppSettings.SmartCardSigningProviderMode));
        Pkcs11LibraryPathReadOnly = settingsStore.IsOverridden(nameof(AppSettings.Pkcs11LibraryPath));
        CheckSigningSetupCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var snapshot = new AppSettings
            {
                SmartCardSigningProviderMode = SmartCardSigningProviderModeReadOnly ? settingsStore.AppSettings.SmartCardSigningProviderMode : SmartCardSigningProviderMode,
                Pkcs11LibraryPath = Pkcs11LibraryPathReadOnly ? settingsStore.AppSettings.Pkcs11LibraryPath : Pkcs11LibraryPath,
            };
            var result = await Task.Run(() => SigningSetupDiagnostics.Check(snapshot));
            SigningSetupStatus = $"{result.Mode}; {result.Architecture}; module: {result.ResolvedModule ?? "none"}; stage: {result.Stage}; available: {result.Available}; code: {result.NativeCode?.ToString() ?? "none"}. {result.Message}";
        });
        UseMockOperatorCredential = s.UseMockOperatorCredential;
        RequireCredentialForOperator = s.RequireCredentialForOperator;
        RequireAttestationBeforeExport = s.RequireAttestationBeforeExport;
        AllowPresenceInLieuOfSigning = s.AllowPresenceInLieuOfSigning;
        ProbeBadgeWhenTechnicianFocused = s.ProbeBadgeWhenTechnicianFocused;
        UseMockOperatorCredentialReadOnly = settingsStore.IsOverridden(nameof(AppSettings.UseMockOperatorCredential));
        RequireCredentialForOperatorReadOnly = settingsStore.IsOverridden(nameof(AppSettings.RequireCredentialForOperator));
        RequireAttestationBeforeExportReadOnly = settingsStore.IsOverridden(nameof(AppSettings.RequireAttestationBeforeExport));
        AllowPresenceInLieuOfSigningReadOnly = settingsStore.IsOverridden(nameof(AppSettings.AllowPresenceInLieuOfSigning));
        ProbeBadgeWhenTechnicianFocusedReadOnly = settingsStore.IsOverridden(nameof(AppSettings.ProbeBadgeWhenTechnicianFocused));
    }

    /// Writes writable credential flags onto AppSettings before persist.
    private void ApplyCredentialSettings(AppSettings settings)
    {
        if (!SmartCardSigningProviderModeReadOnly) settings.SmartCardSigningProviderMode = SmartCardSigningProviderMode;
        if (!Pkcs11LibraryPathReadOnly) settings.Pkcs11LibraryPath = Pkcs11LibraryPath;
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

        if (!ProbeBadgeWhenTechnicianFocusedReadOnly)
        {
            settings.ProbeBadgeWhenTechnicianFocused = ProbeBadgeWhenTechnicianFocused;
        }
    }

    private bool IsCredentialPropertyOverridden(string? propertyName)
        => propertyName switch
        {
            nameof(SmartCardSigningProviderMode) => SmartCardSigningProviderModeReadOnly,
            nameof(Pkcs11LibraryPath) => Pkcs11LibraryPathReadOnly,
            nameof(UseMockOperatorCredential) => UseMockOperatorCredentialReadOnly,
            nameof(RequireCredentialForOperator) => RequireCredentialForOperatorReadOnly,
            nameof(RequireAttestationBeforeExport) => RequireAttestationBeforeExportReadOnly,
            nameof(AllowPresenceInLieuOfSigning) => AllowPresenceInLieuOfSigningReadOnly,
            nameof(ProbeBadgeWhenTechnicianFocused) => ProbeBadgeWhenTechnicianFocusedReadOnly,
            _ => false,
        };
}
