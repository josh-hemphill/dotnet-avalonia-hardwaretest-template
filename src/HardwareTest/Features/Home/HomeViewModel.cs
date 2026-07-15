using ReactiveUI;

namespace HardwareTest.Features.Home;

public sealed class HomeViewModel : ReactiveObject
{
    public string Title { get; } = "Hardware Test";

    public string Summary { get; } =
        "Run declarative instrument suites, manage VISA devices, and publish Typst reports — with live plots when a test needs them.";
}
