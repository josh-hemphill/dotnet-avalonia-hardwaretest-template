namespace HardwareTest.Shell;

/// Host-owned storage for guest shell apps. Session/DUT identity is not part of this surface.
public interface IShellHost
{
    /// `{DataDirectory}/shell-apps/{appId}/`, contained under the data directory.
    string GetAppDataDirectory(string appId);
}
