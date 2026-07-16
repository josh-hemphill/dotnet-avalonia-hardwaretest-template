using ReactiveUI;

namespace HardwareTest.Features.Home;

public sealed class HomeViewModel : ReactiveObject
{
    public string Title { get; } = "Hardware Test";

    public string Summary { get; } =
        "Confirm a DUT once, run locked OpenTAP programs from Avalonia, manage station instruments, and publish Typst reports with live plots when needed.";
}
