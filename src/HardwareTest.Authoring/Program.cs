using Avalonia;

namespace HardwareTest.Authoring;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == AuthoringOperationHost.Switch)
            return AuthoringOperationHost.Run(args[1]);
        if (args.Length == 2 && args[0] == AuthoringOperationChild.Switch)
            return AuthoringOperationChild.Run(args[1]);

        if (AuthoringCli.IsHeadless(args))
        {
            return AuthoringCli.Run(args, Console.Out, Console.Error);
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
