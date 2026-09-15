namespace HardwareTest.Shell;

/// One guest page: descriptor plus factories. <see cref="CreateView"/> must return an Avalonia Control.
public sealed class ShellPageRegistration
{
    public required ShellPageDescriptor Descriptor { get; init; }
    public required Func<IServiceProvider, object> CreateViewModel { get; init; }
    public required Func<object> CreateView { get; init; }
}
