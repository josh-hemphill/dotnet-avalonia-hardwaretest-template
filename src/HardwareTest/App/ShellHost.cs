using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.Shell;

namespace HardwareTest;

/// Resolves contained `{DataDirectory}/shell-apps/{appId}/` folders for guest apps.
public sealed class ShellHost(ISettingsStore settingsStore) : IShellHost
{
    /// Creates the app folder under the settings data directory.
    public string GetAppDataDirectory(string appId)
        => ShellAppStorage.ResolveDirectory(settingsStore.RootDirectory, appId);
}
