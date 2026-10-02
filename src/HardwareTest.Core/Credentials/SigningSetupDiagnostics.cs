using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HardwareTest.Core.Settings;

namespace HardwareTest.Core.Credentials;

public sealed record SigningSetupDiagnostics(SmartCardSigningProviderMode Mode, string? ResolvedModule,
    string Architecture, bool Available, string Stage, ulong? NativeCode, string Message)
{
    /// Checks configuration and native loading only. Never enumerates cards or authenticates.
    public static SigningSetupDiagnostics Check(AppSettings settings)
        => Check(settings, OperatingSystem.IsWindows, () =>
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        });

    internal static SigningSetupDiagnostics Check(AppSettings settings, Func<bool> isWindows, Action checkWindowsStore)
    {
        var architecture = RuntimeInformation.ProcessArchitecture.ToString();
        string? module = null;
        var stage = "configuration";
        try
        {
            if (!Enum.IsDefined(settings.SmartCardSigningProviderMode))
                return new(settings.SmartCardSigningProviderMode, null, architecture, false, stage, null, "Unknown smart-card signing provider mode.");
            if (settings.SmartCardSigningProviderMode == SmartCardSigningProviderMode.Windows ||
                settings.SmartCardSigningProviderMode == SmartCardSigningProviderMode.Auto && isWindows() && string.IsNullOrWhiteSpace(settings.Pkcs11LibraryPath))
            {
                stage = "windows-store";
                if (!isWindows()) return new(settings.SmartCardSigningProviderMode, null, architecture, false, stage, null, "Windows smart-card signing requires Windows.");
                checkWindowsStore();
                return new(settings.SmartCardSigningProviderMode, null, architecture, true, stage, null, "Windows personal certificate store is accessible. Card key acquisition, native PIN dialogs, and signing have not been tested.");
            }
            stage = "module-discovery";
            module = Pkcs11ModuleResolver.Resolve(settings.Pkcs11LibraryPath);
            if (module is null) return new(settings.SmartCardSigningProviderMode, null, architecture, false, stage, null, "Compatible PKCS#11 middleware was not found.");
            stage = "architecture";
            if (!Pkcs11ModuleResolver.IsCompatibleArchitecture(module))
                return new(settings.SmartCardSigningProviderMode, module, architecture, false, stage, null, "Middleware architecture must match the application process.");
            stage = "module-load";
            if (NativeLibrary.TryLoad(module, out var handle))
            {
                NativeLibrary.Free(handle);
                return new(settings.SmartCardSigningProviderMode, module, architecture, true, stage, null, "Middleware loaded. Card authentication and signing have not been tested.");
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or CryptographicException) { }
        if (stage == "windows-store") return new(settings.SmartCardSigningProviderMode, null, architecture, false, stage, null, "Windows personal certificate store could not be opened. Card key acquisition and authentication have not been attempted.");
        return new(settings.SmartCardSigningProviderMode, module, architecture, false, stage, null, "Middleware could not be loaded. Check its path, dependencies, and architecture.");
    }

}
internal static class Pkcs11ModuleResolver
{
    public static string? Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var module = configured.Trim();
            if (module.Contains('\0')) throw new ArgumentException("Invalid module path.");
            return module;
        }
        IEnumerable<string> candidates = OperatingSystem.IsWindows() ? WindowsCandidates()
            : OperatingSystem.IsMacOS() ? ["/Library/OpenSC/lib/opensc-pkcs11.so", "/opt/homebrew/lib/opensc-pkcs11.so", "/usr/local/lib/opensc-pkcs11.so", "opensc-pkcs11.so"]
            : ["/usr/lib/x86_64-linux-gnu/opensc-pkcs11.so", "/usr/lib/aarch64-linux-gnu/opensc-pkcs11.so", "/usr/lib64/opensc-pkcs11.so", "/usr/lib/opensc-pkcs11.so", "opensc-pkcs11.so"];
        return ResolveCandidates(candidates.Where(path => IsCompatibleArchitecture(path)), File.Exists, CanLoad);
    }
    private static bool CanLoad(string library)
    {
        if (!NativeLibrary.TryLoad(library, out var handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    }
    private static IEnumerable<string> WindowsCandidates()
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct();
        foreach (var root in roots.Where(root => !string.IsNullOrEmpty(root)))
        {
            yield return Path.Combine(root, "HID Global", "ActivClient", "acpkcs211.dll");
            yield return Path.Combine(root, "ActivIdentity", "ActivClient", "acpkcs211.dll");
            yield return Path.Combine(root, "OpenSC Project", "OpenSC", "pkcs11", "opensc-pkcs11.dll");
            yield return Path.Combine(root, "OpenSC Project", "OpenSC", "pkcs11", "opensc-pkcs11-x64.dll");
        }
    }
    internal static bool IsCompatibleArchitecture(string path, Architecture? architecture = null)
    {
        if (!OperatingSystem.IsWindows() && !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return true;
        if (!File.Exists(path)) return true; // Explicit missing paths produce actionable load failures.
        try
        {
            using var input = File.OpenRead(path); using var pe = new PEReader(input);
            var expected = (architecture ?? RuntimeInformation.ProcessArchitecture) switch
            { Architecture.X86 => Machine.I386, Architecture.X64 => Machine.Amd64, Architecture.Arm64 => Machine.Arm64, _ => (Machine)0 };
            return pe.PEHeaders.CoffHeader.Machine == expected;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException) { return false; }
    }
    internal static string? ResolveCandidates(IEnumerable<string> candidates, Func<string, bool> fileExists, Func<string, bool> canLoad)
        => candidates.FirstOrDefault(candidate => Path.IsPathRooted(candidate) && fileExists(candidate))
            ?? candidates.FirstOrDefault(candidate => !Path.IsPathRooted(candidate) && canLoad(candidate));
}
