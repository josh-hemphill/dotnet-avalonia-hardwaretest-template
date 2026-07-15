using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using ReactiveUI;

namespace HardwareTest.ViewModels.Tests;

internal static class ReactiveCommandTestExtensions
{
    public static Task ExecuteAsync(this ReactiveCommand<Unit, Unit> command)
        => command.Execute(Unit.Default).ToTask();
}
