using Microsoft.Extensions.DependencyInjection;

namespace HardwareTest.Shell;

/// Bake-time or launch-time guest that contributes pages to the operator shell.
public interface IShellApplication
{
    string Id { get; }
    string Title { get; }
    string Version { get; }
    int MinHostAbi { get; }

    /// Registers app services before the host container is built.
    void Configure(IServiceCollection services);

    IReadOnlyList<ShellPageRegistration> Pages { get; }
}
