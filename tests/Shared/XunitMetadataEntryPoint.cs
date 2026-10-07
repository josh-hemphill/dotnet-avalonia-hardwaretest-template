using Xunit.Runner.InProc.SystemConsole;

internal static class XunitMetadataEntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "-assemblyInfo", StringComparison.OrdinalIgnoreCase))
        {
            using var runner = new ConsoleRunner(args);
            return await runner.EntryPoint();
        }
        return await ConsoleRunner.Run(args);
    }
}
