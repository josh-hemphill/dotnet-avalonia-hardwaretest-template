namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    internal static void Materialize(BuildInputFile file, string destination)
    {
        File.WriteAllBytes(destination, file.Bytes);
        if (!OperatingSystem.IsWindows() && file.UnixMode is { } mode)
            File.SetUnixFileMode(destination, (UnixFileMode)mode);
    }

    private static void CopyPreservingMode(string source, string destination)
    {
        File.Copy(source, destination);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
    }
}
