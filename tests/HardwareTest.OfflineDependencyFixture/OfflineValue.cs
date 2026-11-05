namespace HardwareTest.OfflineDependencyFixture;

public static class OfflineValue
{
    public static double Read() => 1.25;
    public static string LoadedFrom => typeof(OfflineValue).Assembly.Location;
}
