using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using ReactiveUI;
using Unit = ReactiveUI.Primitives.RxVoid;

namespace HardwareTest.ViewModels.Tests;

internal static class ReactiveCommandTestExtensions
{
    public static Task ExecuteAsync(this ReactiveCommand<Unit, Unit> command)
        => command.Execute(Unit.Default).ToTask();
}
