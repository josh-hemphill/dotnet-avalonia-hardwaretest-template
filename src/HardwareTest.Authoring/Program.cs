using Avalonia;

namespace HardwareTest.Authoring;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
