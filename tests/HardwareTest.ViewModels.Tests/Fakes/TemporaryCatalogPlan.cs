namespace HardwareTest.ViewModels.Tests.Fakes;

/// Real catalog bytes for tests whose plan loader is FakeOpenTapSession.
internal sealed class TemporaryCatalogPlan : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "catalog-plan-" + Guid.NewGuid().ToString("N"));
    public TemporaryCatalogPlan(string id)
    {
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, id + ".TapPlan");
        File.WriteAllText(Path, "<TestPlan />");
    }
    public string Path { get; }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
