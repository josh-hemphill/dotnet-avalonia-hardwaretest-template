namespace HardwareTest.OpenTap.Host;

/// Read-only check and isolated publication dispatch, before configuration/logging/UI startup.
public static class OfflineBenchCli
{
    public static bool TryRun(string[] args, string operatorDirectory, TextWriter output, out int exitCode)
    {
        exitCode = 0;
        if (!args.Any(argument => argument.StartsWith("--check-offline-bundle", StringComparison.OrdinalIgnoreCase)
            || argument.StartsWith("--install-offline-bundle", StringComparison.OrdinalIgnoreCase))) return false;
        try
        {
            var check = args.Length == 2 && args[0] == "--check-offline-bundle";
            var install = args.Length == 6 && args[0] == "--install-offline-bundle" && args[2] == "--destination" && args[4] == "--data-directory";
            if (!check && !install)
                throw new InvalidDataException("Use --check-offline-bundle BUNDLE, or --install-offline-bundle BUNDLE --destination NEWAPP --data-directory FRESHDATA. No other options are accepted.");
            if (check)
            {
                var manifest = OfflineBenchRuntime.CheckBundle(args[1], operatorDirectory);
                output.WriteLine(OfflineBenchBundle.Describe(manifest));
                output.WriteLine("Loaded execution engine: " + OpenTapBuildInfo.ExecutionEngineIdentity);
                output.WriteLine("Offline bundle check passed. No package action, network access, data-folder write or application launch occurred.");
            }
            else output.WriteLine(OfflineBenchInstaller.Install(args[1], operatorDirectory, args[3], args[5]).Summary);
        }
        catch (Exception error)
        {
            exitCode = 1;
            output.WriteLine("Offline bundle operation refused: " + error.Message);
        }
        return true;
    }
}
